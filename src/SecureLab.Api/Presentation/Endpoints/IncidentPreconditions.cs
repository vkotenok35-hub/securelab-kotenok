using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Presentation.Endpoints;

// Умовні запити (RFC 9110, If-Match) проти втрачених оновлень; відсутній заголовок: RFC 6585, 428.
public static class IncidentPreconditions
{
    // Сильний ETag у лапках, побудований із версії запису (xmin)
    public static string ETagFor(Incident incident) => $"\"{incident.Version}\"";

    public static void SetETag(HttpContext http, Incident incident) =>
        http.Response.Headers.ETag = ETagFor(incident);

    // null: умову виконано; інакше готова відповідь 428 (заголовка немає) або 412 (версія не збігається)
    public static IResult? Check(HttpContext http, Incident incident)
    {
        var raw = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Results.Problem(
                title: "Потрібна умовна перевірка",
                detail: "Передайте заголовок If-Match із версією запису, отриманою в GET.",
                statusCode: StatusCodes.Status428PreconditionRequired,
                type: "urn:securelab:problem:precondition-required");
        }

        // Строге порівняння: слабкий W/, символ * чи чужий формат не збігаються з поточним ETag
        var expected = ETagFor(incident);
        var matches = raw.Split(',').Select(value => value.Trim()).Contains(expected, StringComparer.Ordinal);
        return matches ? null : IncidentMutationEndpoints.ConcurrencyConflict();
    }
}