namespace SecureLab.Api.Application.Incidents;

// Тестовий шов: у звичайній роботі нічого не робить; тест вставляє бар'єр між читанням і збереженням.
public interface ITriageProbe
{
    Task AfterReadAsync(CancellationToken ct);
}

public sealed class NoOpTriageProbe : ITriageProbe
{
    public Task AfterReadAsync(CancellationToken ct) => Task.CompletedTask;
}