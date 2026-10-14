using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab03SecurityTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private HttpClient Anonymous() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });

    private static object IncidentBody(string title) => new
    {
        title, description = "Штучний запис для автоматичного тесту.", severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task DeleteIncidentAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }

    private async Task<Guid> OwnerOfAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().Where(item => item.Title == title)
            .Select(item => item.OwnerUserId).SingleAsync();
    }

    private async Task DeleteUserAsync(string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName);
        if (user is not null) await users.DeleteAsync(user);
    }

    private static string[] PropertyNames(JsonElement root) =>
        root.EnumerateObject().Select(item => item.Name).Order().ToArray();

    // A-01: анонімне створення дає 401, а не redirect чи HTML
    [Fact]
    public async Task A01_AnonymousCreate_Returns401WithoutRedirect()
    {
        using var client = Anonymous();
        var title = $"A01-{Guid.NewGuid():N}";
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", IncidentBody(title));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            Assert.False(await db.Incidents.AnyAsync(item => item.Title == title));
        }
        finally { await DeleteIncidentAsync(title); }
    }

    // A-02: після login /api/me повертає користувача з principal і лише безпечні поля
    [Fact]
    public async Task A02_LoginThenMe_ReturnsPrincipalUserWithSafeFields()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword());
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(DbSeeder.AliceId, root.GetProperty("id").GetGuid());
        Assert.Equal("alice", root.GetProperty("userName").GetString(), ignoreCase: true);
        Assert.Equal(new[] { "displayName", "id", "userName" }, PropertyNames(root));
    }

    // A-03: чужий X-Demo-UserId не змінює поточного користувача
    [Fact]
    public async Task A03_ForeignDemoHeader_DoesNotChangeCurrentUser()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, document.RootElement.GetProperty("id").GetGuid());
    }

    // A-04: створення від Bob без owner у DTO дає owner = Bob
    [Fact]
    public async Task A04_CreateByBobWithoutOwner_SetsOwnerFromPrincipal()
    {
        using var client = await factory.LoginAsync("bob", SeedPassword());
        var title = $"A04-{Guid.NewGuid():N}";
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", IncidentBody(title));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.BobId, await OwnerOfAsync(title));
        }
        finally { await DeleteIncidentAsync(title); }
    }

    // A-05: чужий ownerUserId у JSON не дає ескалації; owner = Bob
    [Fact]
    public async Task A05_ForgedOwnerInJson_IsIgnored()
    {
        using var client = await factory.LoginAsync("bob", SeedPassword());
        var title = $"A05-{Guid.NewGuid():N}";
        var body = new
        {
            title, description = "Підроблений власник має ігноруватися.", severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ownerUserId = DbSeeder.AliceId
        };
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.BobId, await OwnerOfAsync(title));
        }
        finally { await DeleteIncidentAsync(title); }
    }

    // A-06: logout → повторний захищений запит тим самим клієнтом дає 401
    [Fact]
    public async Task A06_Logout_ThenProtectedRequest_Returns401()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword());
        using (var before = await client.GetAsync("/api/me"))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using (var logout = await client.PostAsync("/api/auth/logout", null))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    // A-07: невідомий користувач і хибний пароль мають однаковий зовнішній результат
    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_LookIdentical()
    {
        using var client = Anonymous();
        var wrong = "Wrong-" + Guid.NewGuid().ToString("N") + "!";
        using var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "nobody" + Guid.NewGuid().ToString("N")[..8], password = wrong });
        using var badPassword = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = wrong });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, badPassword.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.MediaType,
            badPassword.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await ProblemShape.ReadAsync(unknown), await ProblemShape.ReadAsync(badPassword));
    }

    // T-01 + duplicate-контракт: 201 із безпечними полями, повтор — 409 без пароля
    [Fact]
    public async Task Register_Valid_Returns201_AndDuplicateReturns409()
    {
        using var client = Anonymous();
        var userName = "reg" + Guid.NewGuid().ToString("N")[..8];
        var password = SeedPassword();
        var body = new { userName, email = userName + "@example.test", displayName = "Reg Test", password };
        try
        {
            using var first = await client.PostAsJsonAsync("/api/auth/register", body);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            using var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "displayName", "id", "userName" }, PropertyNames(document.RootElement));

            using var second = await client.PostAsJsonAsync("/api/auth/register", body);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var text = await second.Content.ReadAsStringAsync();
            var leaked = text.Contains(password, StringComparison.Ordinal);
            Assert.True(!leaked, "Відповідь register не має містити пароль.");
        }
        finally { await DeleteUserAsync(userName); }
    }

    // T-02: слабкий пароль дає безпечний 400 і акаунт не створюється
    [Fact]
    public async Task Register_WeakPassword_Returns400AndCreatesNothing()
    {
        using var client = Anonymous();
        var userName = "reg" + Guid.NewGuid().ToString("N")[..8];
        var body = new { userName, email = userName + "@example.test", displayName = "Reg Test", password = "short" };
        try
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Null(await users.FindByNameAsync(userName));
        }
        finally { await DeleteUserAsync(userName); }
    }

    // Фактичні атрибути cookie (HTTPS client), значення не друкується
    [Fact]
    public async Task Login_OverHttps_SetsSecureHttpOnlySameSiteLaxCookie()
    {
        using var client = Anonymous();
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = SeedPassword() });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        const string cookieName = ".AspNetCore.Identity.Application=";
        var matching = response.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(cookieName)).ToArray();
        Assert.True(matching.Length == 1);
        var attributes = matching[0].Split(';').Skip(1)
            .Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=lax", attributes);
    }
}

public sealed class LoginRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    // Окремий host: лічильники належать host, тому інші тести не отримають 429
    [Fact]
    public async Task Login_ExceedingLimit_Returns429_ThenRecoversAfterWindow()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateClient();
        var wrong = "Wrong-" + Guid.NewGuid().ToString("N") + "!";

        for (var i = 0; i < 20; i++)
        {
            using var accepted = await client.PostAsJsonAsync("/api/auth/login",
                new { userName = "nobody", password = wrong });
            Assert.Equal(HttpStatusCode.Unauthorized, accepted.StatusCode);
        }

        using var denied = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "nobody", password = wrong });
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(16));
        using var recovered = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "nobody", password = wrong });
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
    }
}