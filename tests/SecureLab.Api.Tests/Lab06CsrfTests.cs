using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class Lab06CsrfTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private async Task<int> CountAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.CountAsync(item => item.Title == title);
    }

    private async Task DeleteAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }

    // REG-CSRF-01: login → відмова без токена → стан незмінний → невалідний токен → валідна пара → стан змінено
    [Fact]
    public async Task RegCsrf01_Create_WithoutValidToken_IsRejectedAndStateUnchanged_ThenValidPairSucceeds()
    {
        var title = $"CSRF-{Guid.NewGuid():N}";
        using var client = await factory.LoginAsync("alice", SeedPassword(), withAntiforgery: false);
        var body = new
        {
            title,
            description = "Контрольний запис REG-CSRF-01.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        try
        {
            Assert.Equal(0, await CountAsync(title));

            using var withoutToken = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
            Assert.Equal(0, await CountAsync(title));                 // стан не змінився

            using var badRequest = new HttpRequestMessage(HttpMethod.Post, "/api/incidents")
            {
                Content = JsonContent.Create(body)
            };
            badRequest.Headers.Add(AntiforgeryTestExtensions.HeaderName, "invalid-test-value");
            using var invalidToken = await client.SendAsync(badRequest);
            Assert.Equal(HttpStatusCode.BadRequest, invalidToken.StatusCode);
            Assert.Equal(0, await CountAsync(title));                 // стан не змінився

            await client.UseAntiforgeryTokenAsync();
            using var accepted = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            Assert.Equal(1, await CountAsync(title));                 // саме очікувана зміна
        }
        finally { await DeleteAsync(title); }
    }

    // Увесь периметр: кожен cookie-authenticated змінювальний маршрут відхиляє запит без токена.
    // Використано неіснуючий id: якщо фільтр не підключено, відповідь буде 404, а не 400, і дані не постраждають.
    [Theory]
    [InlineData("PUT", "/api/incidents/99999999-9999-9999-9999-999999999999")]
    [InlineData("DELETE", "/api/incidents/99999999-9999-9999-9999-999999999999")]
    [InlineData("POST", "/api/incidents/99999999-9999-9999-9999-999999999999/triage")]
    [InlineData("POST", "/api/auth/logout")]
    public async Task RegCsrf01_EveryCookieMutationRoute_RejectsRequestWithoutToken(string method, string route)
    {
        using var client = await factory.LoginAsync("alice", SeedPassword(), withAntiforgery: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { })
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}