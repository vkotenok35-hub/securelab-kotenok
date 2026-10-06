using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab02SecurityTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object ValidBody(string title) => new
    {
        title,
        description = "Штучний запис для автоматичного тесту.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }

    // T-02: некоректний severity дає 400 з ключем severity і нічого не зберігається
    [Fact]
    public async Task Post_WithNumericSeverity_Returns400AndStoresNothing()
    {
        var title = $"T02-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Штучний опис.",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
            Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            Assert.False(await db.Incidents.AnyAsync(item => item.Title == title));
        }
        finally { await DeleteByTitleAsync(title); }
    }

    // T-10: заголовок без літер дає 400 з ключем title
    [Fact]
    public async Task Post_WithTitleWithoutLetters_Returns400WithTitleKey()
    {
        var body = new
        {
            title = "12345 ---",
            description = "Штучний опис.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("title", out _));
    }

    // T-03: повторний активний title дає 409, у базі лишається один запис
    [Fact]
    public async Task Post_WithDuplicateActiveTitle_Returns409AndStoresOnlyOne()
    {
        var title = $"T03-{Guid.NewGuid():N}";
        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", ValidBody(title));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await _client.PostAsJsonAsync("/api/incidents", ValidBody(title));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            Assert.Equal(1, await db.Incidents.CountAsync(item => item.Title == title));
        }
        finally { await DeleteByTitleAsync(title); }
    }

    // S-02: контрольний ввід більше не змінює логіку запиту; очікувана множина порожня
    [Fact]
    public async Task Search_ControlInput_ReturnsEmptySet()
    {
        var q = Uri.EscapeDataString("zz-no-match' OR TRUE -- ");
        var incidents = await _client.GetFromJsonAsync<List<JsonElement>>($"/api/incidents/search?q={q}");

        Assert.NotNull(incidents);
        Assert.Empty(incidents);
    }

    // S-02, позитивний контроль: пошук не став надмірно суворим
    [Fact]
    public async Task Search_Usb_ReturnsExactlyOneSeedIncident()
    {
        var incidents = await _client.GetFromJsonAsync<List<JsonElement>>("/api/incidents/search?q=USB");

        Assert.NotNull(incidents);
        var single = Assert.Single(incidents);
        Assert.Equal(Guid.Parse("20000000-0000-0000-0000-000000000005"),
            single.GetProperty("id").GetGuid());
    }

    // A-02: server-managed поля з тіла запиту ігноруються
    [Fact]
    public async Task Post_WithExtraServerManagedFields_IgnoresThemAndSetsServerValues()
    {
        var title = $"A02-{Guid.NewGuid():N}";
        var sentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var body = new
        {
            title,
            description = "Зайві поля мають ігноруватися.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            id = sentId,
            ownerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            status = "Closed",
            createdAtUtc = "2000-01-01T00:00:00Z"
        };
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.NotEqual(sentId, root.GetProperty("id").GetGuid());
            Assert.Equal("New", root.GetProperty("status").GetString());

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var saved = await db.Incidents.AsNoTracking().SingleAsync(item => item.Title == title);
            Assert.Equal(DbSeeder.AliceId, saved.OwnerUserId);
            Assert.Equal(IncidentStatus.New, saved.Status);
            Assert.True(saved.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        }
        finally { await DeleteByTitleAsync(title); }
    }
}