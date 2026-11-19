using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    // F-03, стан ДО виправлення: характеризаційний тест. Він проходить, поки дефект є.
    [Fact]
    public async Task F03_BeforeFix_ConcurrentTriage_ProducesDuplicateTransition()
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

            var responses = await Task.WhenAll(
                first.PostAsync($"/api/incidents/{id}/triage", null),
                second.PostAsync($"/api/incidents/{id}/triage", null));

            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var rows = await db.IncidentStatusHistory.CountAsync(
                item => item.Incident.Id == id && item.NewStatus == IncidentStatus.Triaged);

            var statuses = responses.Select(r => (int)r.StatusCode).Order().ToArray();
            output.WriteLine($"triage responses: {string.Join(", ", statuses)}; Triaged history rows: {rows}");

            Assert.Equal(new[] { 200, 200 }, statuses);   // обидва пройшли перевірку New
            Assert.Equal(2, rows);                          // дубль переходу в історії
            foreach (var response in responses) response.Dispose();
        }
        finally
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
        }
    }
}