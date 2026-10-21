namespace SecureLab.Api.Presentation.Contracts;

// Лише content-поля. OwnerUserId, role, status, id і службові часи визначає сервер.
public sealed record UpdateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);