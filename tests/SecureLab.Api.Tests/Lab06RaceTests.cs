using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class Lab06RaceTests(SecureLabApiFactory factory, ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // Бар'єр: обидва запити зупиняються після перевірки статусу й продовжують лише разом
    private sealed class BarrierProbe(int participants) : ITriageProbe
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task AfterReadAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == participants) _released.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
    }

    // F-03, стан ПІСЛЯ виправлення: токен конкурентності й обмеження в БД лишають один перехід.
    [Fact]
    public async Task F03_AfterFix_ConcurrentTriage_ProducesSingleTransition()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITriageProbe>();
            services.AddSingleton<ITriageProbe>(new BarrierProbe(2));
        }));

        var title = $"F03-{Guid.NewGuid():N}";
        using var alice = await host.LoginAsync("alice", SeedPassword());
        using var created = await alice.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Контрольний запис F-03.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        created.EnsureSuccessStatusCode();
        var id = Guid.Parse((await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetString()!);

        try
        {
            using var first = await host.LoginAsync("morgan", SeedPassword());
            using var second = await host.LoginAsync("morgan", SeedPassword());

            var etag = await first.GetETagAsync($"/api/incidents/{id}");
            var responses = await Task.WhenAll(
                first.SendWithIfMatchAsync(HttpMethod.Post, $"/api/incidents/{id}/triage", etag),
                second.SendWithIfMatchAsync(HttpMethod.Post, $"/api/incidents/{id}/triage", etag));
				
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var rows = await db.IncidentStatusHistory.CountAsync(
                item => item.Incident.Id == id && item.NewStatus == IncidentStatus.Triaged);

            var statuses = responses.Select(r => (int)r.StatusCode).Order().ToArray();
            output.WriteLine($"triage responses: {string.Join(", ", statuses)}; Triaged history rows: {rows}");

            Assert.Equal(new[] { 200, 412 }, statuses);   // один пройшов, другий відхилено як конфлікт
            Assert.Equal(1, rows);                         // один запис переходу
            foreach (var response in responses) response.Dispose();
        }
        finally
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
        }
    }

    private static IncidentStatusHistory TriagedRow(Guid incidentId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = incidentId,
        ChangedByUserId = DbSeeder.MorganId,
        OldStatus = IncidentStatus.New,
        NewStatus = IncidentStatus.Triaged,
        Note = "Тест обмеження.",
        CreatedAtUtc = now
    };

    // Захист у глибину: обмеження в БД діє незалежно від токена конкурентності.
    [Fact]
    public async Task F03_Index_RejectsSecondTriagedTransition_IndependentlyOfToken()
    {
        var now = DateTimeOffset.UtcNow;
        var incidentId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        db.Incidents.Add(new Incident
        {
            Id = incidentId,
            OwnerUserId = DbSeeder.AliceId,
            Title = $"F03-index-{Guid.NewGuid():N}",
            Description = "Контрольний запис обмеження F-03.",
            Severity = IncidentSeverity.Low,
            Status = IncidentStatus.Triaged,
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        db.IncidentStatusHistory.Add(TriagedRow(incidentId, now));
        await db.SaveChangesAsync();

        try
        {
            db.IncidentStatusHistory.Add(TriagedRow(incidentId, now));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        }
        finally
        {
            await using var cleanup = factory.Services.CreateAsyncScope();
            var cleanupDb = cleanup.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await cleanupDb.Incidents.Where(item => item.Id == incidentId).ExecuteDeleteAsync();
        }
    }
}