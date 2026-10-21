using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Access;

public static class CurrentUser
{
    // Користувач береться лише з перевіреного principal, а не з body, URL чи заголовка.
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool IsAnalyst(this ClaimsPrincipal user) => user.IsInRole(DbSeeder.AnalystRole);
}

public static class IncidentOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
    public static readonly OperationAuthorizationRequirement Triage = new() { Name = nameof(Triage) };
}

// Єдине відокремлене рішення: principal + resource + operation.
public sealed class IncidentAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Incident>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Incident incident)
    {
        var userId = context.User.GetUserId();
        if (userId is null) return Task.CompletedTask;

        var isOwner = incident.OwnerUserId == userId;
        var isAnalyst = context.User.IsAnalyst();

        var allowed = requirement.Name switch
        {
            nameof(IncidentOperations.Read) => isOwner || isAnalyst,
            nameof(IncidentOperations.Update) => isOwner,
            nameof(IncidentOperations.Delete) => isOwner && incident.Status == IncidentStatus.New,
            nameof(IncidentOperations.Triage) => isAnalyst,
            _ => false
        };

        if (allowed) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class IncidentScoping
{
    // Обмеження множини в самому запиті: заборонені рядки не завантажуються взагалі.
    public static IQueryable<Incident> VisibleTo(this IQueryable<Incident> query, ClaimsPrincipal user)
    {
        if (user.IsAnalyst()) return query;
        var userId = user.GetUserId();
        return userId is { } id
            ? query.Where(item => item.OwnerUserId == id)
            : query.Where(_ => false);
    }
}

public static class IncidentErrors
{
    // Однакова відповідь для чужого й неіснуючого ресурсу; id у тілі не повторюється.
    public static IResult NotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        detail: "Запитаний інцидент недоступний або не існує.",
        statusCode: StatusCodes.Status404NotFound);
}