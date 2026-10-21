using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Уся група вимагає автентифікації: анонімний запит одержує 401.
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, httpContext.User, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var user = httpContext.User;
        var incident = await db.Incidents.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        // Рішення передує відповіді; чужий і неіснуючий ресурс дають однакову 404.
        if (incident is null
            || !(await authorization.AuthorizeAsync(user, incident, IncidentOperations.Read)).Succeeded)
        {
            return IncidentErrors.NotFound();
        }

        var details = await queries.GetDetailsAsync(id, user.IsAnalyst(), cancellationToken);
        return details is null ? IncidentErrors.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var summary = await queries.GetSeveritySummaryAsync(httpContext.User, cancellationToken);
        return Results.Ok(summary);
    }
}