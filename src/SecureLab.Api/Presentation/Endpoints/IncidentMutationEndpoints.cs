using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentMutationEndpoints
{
    public static IEndpointRouteBuilder MapIncidentMutationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var incidents = endpoints.MapGroup("/api/incidents").WithTags("Incidents").RequireAuthorization();
        incidents.MapPut("/{id:guid}", UpdateAsync);
        incidents.MapDelete("/{id:guid}", DeleteAsync);
        incidents.MapPost("/{id:guid}/triage", TriageAsync);

        endpoints.MapGroup("/api/incident-comments").WithTags("Incidents").RequireAuthorization()
            .MapGet("/{id:guid}", GetCommentAsync);

        return endpoints;
    }

    // Той самий correlation id, що потрапляє в Problem Details (traceId).
    private static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    // Користувач бачить ресурс, але дія заборонена → 403; не бачить → 404 (як для неіснуючого).
    private static async Task<IResult> DenyAsync(
        IAuthorizationService authorization, ClaimsPrincipal user, Incident incident)
    {
        var canSee = (await authorization.AuthorizeAsync(user, incident, IncidentOperations.Read)).Succeeded;
        return canSee
            ? Results.Problem(title: "Заборонено", detail: "Дію над цим ресурсом не дозволено.",
                statusCode: StatusCodes.Status403Forbidden)
            : IncidentErrors.NotFound();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        CancellationToken ct)
    {
        var user = httpContext.User;

        // Tracked entity (без AsNoTracking), інакше SaveChanges нічого не збереже.
        var incident = await db.Incidents.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (incident is null) return IncidentErrors.NotFound();

        // Рішення передує будь-якій зміні стану.
        if (!(await authorization.AuthorizeAsync(user, incident, IncidentOperations.Update)).Succeeded)
            return await DenyAsync(authorization, user, incident);

        var now = DateTimeOffset.UtcNow;
        var errors = new Dictionary<string, string[]>();

        if (request.Title is { Length: > 160 })
            errors["title"] = ["Заголовок не може перевищувати 160 символів."];
        if (request.Description is { Length: > 4000 })
            errors["description"] = ["Опис не може перевищувати 4000 символів."];

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        if (title.Length == 0 && !errors.ContainsKey("title"))
            errors["title"] = ["Заголовок обов'язковий."];
        if (description.Length == 0 && !errors.ContainsKey("description"))
            errors["description"] = ["Опис обов'язковий."];
        if (title.Length > 0 && !title.Any(char.IsLetter) && !errors.ContainsKey("title"))
            errors["title"] = ["Заголовок має містити хоча б одну літеру."];

        IncidentSeverity severity = default;
        var severityValid = Enum.TryParse<IncidentSeverity>(request.Severity, true, out severity)
                            && Enum.IsDefined(severity);
        if (!severityValid)
            errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

        var occurred = request.OccurredAtUtc;
        if (occurred is null)
            errors["occurredAtUtc"] = ["Дата й час події обов'язкові."];
        else if (occurred.Value > now.AddMinutes(5))
            errors["occurredAtUtc"] = ["Подія не може бути в майбутньому."];

        if (severityValid
            && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
            && description.Length > 0 && description.Length < 40
            && !errors.ContainsKey("description"))
            errors["description"] = ["Для High і Critical опис має містити щонайменше 40 символів."];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (await db.Incidents.AnyAsync(
                item => item.Id != id && item.Title == title && item.Status != IncidentStatus.Closed, ct))
            return Results.Problem(title: "Інцидент уже існує",
                detail: "Незакритий інцидент із таким заголовком уже зареєстровано.",
                statusCode: StatusCodes.Status409Conflict);

        // Змінюються лише content-поля; owner, status і id лишаються серверними.
        incident.Title = title;
        incident.Description = description;
        incident.Severity = severity;
        incident.OccurredAtUtc = occurred!.Value.ToUniversalTime();
        incident.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        CancellationToken ct)
    {
        var user = httpContext.User;
        var incident = await db.Incidents
            .Include(item => item.Comments)
            .Include(item => item.StatusHistory)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (incident is null) return IncidentErrors.NotFound();

        if (!(await authorization.AuthorizeAsync(user, incident, IncidentOperations.Delete)).Succeeded)
            return await DenyAsync(authorization, user, incident);

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var user = httpContext.User;
        var userId = user.GetUserId();

        // Привілейована дія: роль перевіряється до завантаження ресурсу, тож відповідь не залежить від id.
        if (!user.IsAnalyst())
        {
            // Безпечна аудиторська подія: subject, action, resource, result, UTC і той самий trace id.
            logger.LogWarning(
                "authorization.denied subject={SubjectId} action={Action} resource={ResourceId} result={Result} occurredAtUtc={OccurredAtUtc:O} trace={TraceId}",
                userId, "incident.triage", id, "denied", DateTimeOffset.UtcNow, TraceId(httpContext));
            return Results.Problem(title: "Заборонено", detail: "Дію не дозволено для вашої ролі.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var incident = await db.Incidents.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (incident is null) return IncidentErrors.NotFound();
        if (!(await authorization.AuthorizeAsync(user, incident, IncidentOperations.Triage)).Succeeded)
            return await DenyAsync(authorization, user, incident);

        // Допустимий лише New → Triaged; поточний статус читає сервер, наступний встановлює він же.
        if (incident.Status != IncidentStatus.New)
            return Results.Problem(title: "Перехід неможливий",
                detail: "Тріаж можливий лише для нового інциденту.",
                statusCode: StatusCodes.Status409Conflict);

        var analyst = await db.Users.FirstAsync(item => item.Id == userId!.Value, ct);
        var now = DateTimeOffset.UtcNow;
        var oldStatus = incident.Status;
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        db.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            Incident = incident,
            ChangedByUser = analyst,
            OldStatus = oldStatus,
            NewStatus = incident.Status,
            Note = "Тріаж виконано аналітиком.",
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = incident.Id, status = incident.Status.ToString() });
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        CancellationToken ct)
    {
        var user = httpContext.User;
        var comment = await db.IncidentComments.AsNoTracking()
            .Include(item => item.Incident)
            .Include(item => item.Author)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        // Child через parent: неіснуючий, чужий і internal (для Reporter) коментар — та сама 404.
        if (comment is null
            || !(await authorization.AuthorizeAsync(user, comment.Incident, IncidentOperations.Read)).Succeeded
            || (comment.IsInternal && !user.IsAnalyst()))
        {
            return IncidentErrors.NotFound();
        }

        return Results.Ok(new IncidentCommentResponse(
            comment.Id, comment.Author.DisplayName, comment.Text, comment.CreatedAtUtc));
    }
}