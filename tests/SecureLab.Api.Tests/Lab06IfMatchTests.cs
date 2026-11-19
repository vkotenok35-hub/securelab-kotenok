using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab06IfMatchTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static object Body(string title) => new
    {
        title,
        description = "Штучний запис для перевірки If-Match.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private static async Task<Guid> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", Body(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Incident> RowAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private async Task DeleteRowAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
    }

    // Відсутній If-Match: 428 для PUT, DELETE і triage; ресурс не змінюється
    [Fact]
    public async Task MissingIfMatch_Returns428_ForPutDeleteAndTriage_AndResourceIsUnchanged()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword());
        using var morgan = await factory.LoginAsync("morgan", SeedPassword());
        var title = $"IM428-{Guid.NewGuid():N}";
        var id = await CreateAsync(alice, title);
        try
        {
            var before = await RowAsync(id);

            using var put = await alice.PutAsJsonAsync($"/api/incidents/{id}", Body($"{title}-changed"));
            using var delete = await alice.DeleteAsync($"/api/incidents/{id}");
            using var triage = await morgan.PostAsync($"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.PreconditionRequired, put.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionRequired, delete.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionRequired, triage.StatusCode);

            var after = await RowAsync(id);
            Assert.Equal(before.Title, after.Title);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.Version, after.Version);
        }
        finally { await DeleteRowAsync(id); }
    }

    // Lost update для PUT: другий клієнт зі застарілою версією отримує 412, у БД зміна першого
    [Fact]
    public async Task StalePut_Returns412_AndKeepsFirstClientsChange()
    {
        using var first = await factory.LoginAsync("alice", SeedPassword());
        using var second = await factory.LoginAsync("alice", SeedPassword());
        var title = $"IM412-{Guid.NewGuid():N}";
        var id = await CreateAsync(first, title);
        var route = $"/api/incidents/{id}";
        try
        {
            var etag = await first.GetETagAsync(route);
            Assert.Equal(etag, await second.GetETagAsync(route));     // обидва клієнти читали одну версію

            using var firstPut = await first.SendWithIfMatchAsync(
                HttpMethod.Put, route, etag, JsonContent.Create(Body($"{title}-first")));
            Assert.Equal(HttpStatusCode.NoContent, firstPut.StatusCode);
            Assert.NotEqual(etag, firstPut.Headers.ETag!.Tag);        // успішний PUT видає новий ETag

            using var secondPut = await second.SendWithIfMatchAsync(
                HttpMethod.Put, route, etag, JsonContent.Create(Body($"{title}-second")));
            Assert.Equal(HttpStatusCode.PreconditionFailed, secondPut.StatusCode);

            Assert.Equal($"{title}-first", (await RowAsync(id)).Title);
        }
        finally { await DeleteRowAsync(id); }
    }

    // If-Match використовує строге порівняння: слабкий ETag не збігається навіть із правильним значенням
    [Fact]
    public async Task WeakEtag_DoesNotMatch_Returns412_AndResourceIsUnchanged()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword());
        var title = $"IMweak-{Guid.NewGuid():N}";
        var id = await CreateAsync(alice, title);
        var route = $"/api/incidents/{id}";
        try
        {
            var etag = await alice.GetETagAsync(route);
            using var response = await alice.SendWithIfMatchAsync(
                HttpMethod.Put, route, etag, JsonContent.Create(Body($"{title}-weak")), weak: true);

            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            Assert.Equal(title, (await RowAsync(id)).Title);
        }
        finally { await DeleteRowAsync(id); }
    }

    // Контрольний одиночний сценарій: GET → PUT → triage з актуальними ETag, кожна зміна видає новий
    [Fact]
    public async Task SingleFlow_ReadUpdateTriage_WorksAndIssuesNewETags()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword());
        using var morgan = await factory.LoginAsync("morgan", SeedPassword());
        var title = $"IMflow-{Guid.NewGuid():N}";
        var id = await CreateAsync(alice, title);
        var route = $"/api/incidents/{id}";
        try
        {
            var first = await alice.GetETagAsync(route);
            using var put = await alice.SendWithIfMatchAsync(
                HttpMethod.Put, route, first, JsonContent.Create(Body($"{title}-updated")));
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            var second = put.Headers.ETag!.Tag;
            Assert.NotEqual(first, second);
            Assert.Equal(second, await alice.GetETagAsync(route));    // GET повертає той самий ETag, що й PUT

            using var triage = await morgan.SendWithIfMatchAsync(HttpMethod.Post, $"{route}/triage", second);
            Assert.Equal(HttpStatusCode.OK, triage.StatusCode);
            Assert.NotEqual(second, triage.Headers.ETag!.Tag);
            Assert.Equal(IncidentStatus.Triaged, (await RowAsync(id)).Status);
        }
        finally { await DeleteRowAsync(id); }
    }
}