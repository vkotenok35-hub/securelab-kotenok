using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = dbContext.Incidents.AsNoTracking().VisibleTo(user);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    // Рішення про доступ ухвалено раніше (resource handler); тут лише проєкція.
    // includeInternal — лише для Analyst: internal comment не потрапляє у відповідь Reporter.
    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        bool includeInternal,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => includeInternal || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // Обмеження за суб'єктом стоїть до групування, а не після.
        var counts = await dbContext.Incidents
            .AsNoTracking()
            .VisibleTo(user)
            .GroupBy(incident => incident.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var summary = Enum.GetValues<IncidentSeverity>()
            .Select(severity =>
            {
                var found = counts.FirstOrDefault(c => c.Severity == severity);
                return new IncidentSeveritySummaryResponse(
                    severity.ToString(),
                    found?.Count ?? 0);
            })
            .ToList();

        logger.LogInformation(
            "Severity summary built with {GroupCount} groups",
            summary.Count);

        return summary;
    }
}