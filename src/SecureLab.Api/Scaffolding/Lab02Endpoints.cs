using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
                app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            // sortBy: лише allowlist, значення клієнта ніколи не потрапляє в SQL
            if (!(sortBy is null or "" or "createdAtUtc" or "severity" or "status"))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });

            // q — значення, а не структура запиту; спецсимволи LIKE екрануємо, щоб шукати їх буквально
            var escaped = (q ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = "%" + escaped + "%";

            var filtered = db.Incidents.AsNoTracking().Where(row =>
                EF.Functions.ILike(row.Title, pattern, "\\")
                || EF.Functions.ILike(row.Description, pattern, "\\"));

            // ранги, а не алфавіт назв
            var ordered = sortBy switch
            {
                "severity" => filtered.OrderBy(row =>
                    row.Severity == IncidentSeverity.Critical ? 0
                    : row.Severity == IncidentSeverity.High ? 1
                    : row.Severity == IncidentSeverity.Medium ? 2 : 3),
                "status" => filtered.OrderBy(row =>
                    row.Status == IncidentStatus.New ? 0
                    : row.Status == IncidentStatus.Triaged ? 1
                    : row.Status == IncidentStatus.InProgress ? 2
                    : row.Status == IncidentStatus.Resolved ? 3 : 4),
                _ => filtered.OrderByDescending(row => row.CreatedAtUtc)
            };

            // стабільний другий ключ і ліміт 50 рядків, як було в scaffold
            var rows = await ordered.ThenBy(row => row.Id).Take(50).ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
		
                app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow; // фіксуємо час один раз
            var errors = new Dictionary<string, string[]>();

            // Довжини вимірюємо на надісланому значенні, до Trim()
            if (request.Title is { Length: > 160 })
                errors["title"] = ["Заголовок не може перевищувати 160 символів."];
            if (request.Description is { Length: > 4000 })
                errors["description"] = ["Опис не може перевищувати 4000 символів."];

            // Required і cross-field — після Trim(), кожен рядок нормалізуємо один раз
            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";
            if (title.Length == 0 && !errors.ContainsKey("title"))
                errors["title"] = ["Заголовок обов'язковий."];
            if (description.Length == 0 && !errors.ContainsKey("description"))
                errors["description"] = ["Опис обов'язковий."];
			// T-10 (відмінний): заголовок має містити хоча б одну літеру
            if (title.Length > 0 && !title.Any(char.IsLetter) && !errors.ContainsKey("title"))
                errors["title"] = ["Заголовок має містити хоча б одну літеру."];
			
            // TryParse приймає й числові рядки ("7"), тому потрібен ще IsDefined
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

            // T-09 (добрий): для High/Critical опис після Trim() щонайменше 40 символів
            if (severityValid
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && description.Length > 0 && description.Length < 40
                && !errors.ContainsKey("description"))
                errors["description"] = ["Для High і Critical опис має містити щонайменше 40 символів."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // T-03 (добрий): активний інцидент із таким самим title (з урахуванням регістру)
            var duplicate = await db.Incidents.AnyAsync(
                item => item.Title == title && item.Status != IncidentStatus.Closed, ct);
            if (duplicate)
                return Results.Problem(
                    title: "Інцидент уже існує",
                    detail: "Незакритий інцидент із таким заголовком уже зареєстровано.",
                    statusCode: StatusCodes.Status409Conflict);

            var incident = new Incident
            {
                Id = Guid.NewGuid(), OwnerUserId = DbSeeder.AliceId,
                Title = title, Description = description, Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = occurred!.Value.ToUniversalTime(),
                CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/incidents/{incident.Id}", new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(),
                incident.Status.ToString(), incident.OccurredAtUtc, incident.CreatedAtUtc));
        });
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
	
public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);