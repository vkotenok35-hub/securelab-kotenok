using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab04MutationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private Task<HttpClient> LoginAs(string userName) => factory.LoginAsync(userName, SeedPassword());

    private static object ContentBody(string title) => new
    {
        title,
        description = "Штучний запис для автоматичного тесту.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    // Тестовий ресурс створюється через API від імені потрібного користувача: status завжди New.
    private static async Task<Guid> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", ContentBody(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    // State assertion: знімок захищеного стану ресурсу в базі.
    private async Task<object> SnapshotAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var row = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == id);
        return new { row.OwnerUserId, row.Title, row.Description, row.Severity, row.Status, row.UpdatedAtUtc };
    }

    private async Task<Incident?> LoadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
    }

    private async Task DeleteIncidentAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents
            .Include(item => item.Comments).Include(item => item.StatusHistory)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (incident is null) return;
        db.Incidents.Remove(incident);
        await db.SaveChangesAsync();
    }

    // A-04: чужий ресурс — update і delete заборонені, стан Bob без змін
    [Fact]
    public async Task A04_OtherReporter_UpdateAndDelete_AreDenied_AndDoNotChangeIncident()
    {
        using var alice = await LoginAs("alice");
        var before = await SnapshotAsync(DbSeeder.BobIncidentId);

        using var update = await alice.PutAsJsonAsync($"/api/incidents/{DbSeeder.BobIncidentId}", ContentBody("Змінено Алісою"));
        using var delete = await alice.DeleteAsync($"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(before, await SnapshotAsync(DbSeeder.BobIncidentId));
    }

    // Owner видаляє власний новий інцидент
    [Fact]
    public async Task Delete_AsOwner_WhenNew_Succeeds()
    {
        using var alice = await LoginAs("alice");
        var id = await CreateAsync(alice, $"A04-del-{Guid.NewGuid():N}");
        try
        {
            using var delete = await alice.DeleteAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Null(await LoadAsync(id));
        }
        finally { await DeleteIncidentAsync(id); }
    }

    // Owner не може видалити власний інцидент не в статусі New: 403, інцидент лишається
    [Fact]
    public async Task Delete_AsOwner_WhenNotNew_Returns403_AndKeepsIncident()
    {
        using var alice = await LoginAs("alice");
        var before = await SnapshotAsync(DbSeeder.AliceIncidentId); // seed: Triaged

        using var delete = await alice.DeleteAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(before, await SnapshotAsync(DbSeeder.AliceIncidentId));
    }

    // A-06: Reporter не виконує привілейований transition навіть над власним ресурсом
    [Fact]
    public async Task A06_Reporter_Triage_Returns403_AndStatusUnchanged()
    {
        using var alice = await LoginAs("alice");
        var before = await SnapshotAsync(DbSeeder.SafeTextIncidentId); // власний, status New

        using var response = await alice.PostAsync($"/api/incidents/{DbSeeder.SafeTextIncidentId}/triage", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(DbSeeder.SafeTextIncidentId));
    }

    // A-07: Analyst виконує дозволений New → Triaged; повторний transition — 409
    [Fact]
    public async Task A07_Analyst_Triage_MovesNewToTriaged_AndRepeatConflicts()
    {
        using var alice = await LoginAs("alice");
        using var morgan = await LoginAs("morgan");
        var id = await CreateAsync(alice, $"A07-{Guid.NewGuid():N}");
        try
        {
            using var first = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            using var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            Assert.Equal("Triaged", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(IncidentStatus.Triaged, (await LoadAsync(id))!.Status);

            using var second = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
        finally { await DeleteIncidentAsync(id); }
    }

    // A-08a: forged owner/role/status/id у звичайному update не змінюють захищений стан
    [Fact]
    public async Task A08a_UpdateWithForgedProtectedFields_ChangesContentOnly()
    {
        using var alice = await LoginAs("alice");
        var id = await CreateAsync(alice, $"A08-{Guid.NewGuid():N}");
        var newTitle = $"A08-changed-{Guid.NewGuid():N}";
        try
        {
            var body = new
            {
                title = newTitle,
                description = "Оновлений опис без зміни захищених полів.",
                severity = "Medium",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                ownerUserId = DbSeeder.BobId,
                role = "Administrator",
                status = "Closed",
                id = Guid.NewGuid()
            };
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", body);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var saved = await LoadAsync(id);
            Assert.NotNull(saved);
            Assert.Equal(newTitle, saved.Title);                      // дозволений content змінився
            Assert.Equal(DbSeeder.AliceId, saved.OwnerUserId);        // owner не змінився
            Assert.Equal(IncidentStatus.New, saved.Status);           // protected status не змінився
        }
        finally { await DeleteIncidentAsync(id); }
    }

    // A-12: Reporter не читає чужий і internal коментар; власний звичайний — читає
    [Fact]
    public async Task A12_Reporter_CannotReadForeignOrInternalComment()
    {
        using var alice = await LoginAs("alice");

        using var foreign = await alice.GetAsync($"/api/incident-comments/{DbSeeder.CommentToBobId}");
        using var internalComment = await alice.GetAsync($"/api/incident-comments/{DbSeeder.InternalCommentId}");
        using var own = await alice.GetAsync($"/api/incident-comments/{DbSeeder.CommentToAliceId}");
        var details = await alice.GetStringAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, internalComment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.DoesNotContain("Внутрішня примітка", details, StringComparison.Ordinal);
    }

    // A-12: Analyst читає internal коментар після авторизації parent
    [Fact]
    public async Task A12_Analyst_ReadsInternalComment_ThroughParent()
    {
        using var morgan = await LoginAs("morgan");

        using var internalComment = await morgan.GetAsync($"/api/incident-comments/{DbSeeder.InternalCommentId}");
        var details = await morgan.GetStringAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, internalComment.StatusCode);
        Assert.Contains("Внутрішня примітка", details, StringComparison.Ordinal);
    }
}