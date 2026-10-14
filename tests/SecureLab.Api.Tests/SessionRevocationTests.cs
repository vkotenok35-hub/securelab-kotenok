using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class SessionRevocationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task SecurityStampChange_InvalidatesPreviouslyIssuedSession()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options =>
                options.ValidationInterval = TimeSpan.Zero)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

        var userName = "stamp" + Guid.NewGuid().ToString("N")[..8];
        var password = SeedPassword();
        try
        {
            using (var register = await client.PostAsJsonAsync("/api/auth/register",
                new { userName, email = userName + "@example.test", displayName = "Stamp Test", password }))
                Assert.Equal(HttpStatusCode.Created, register.StatusCode);

            using (var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password }))
                Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            using (var before = await client.GetAsync("/api/me"))
                Assert.Equal(HttpStatusCode.OK, before.StatusCode);

            // Серверна подія: оновлення security stamp акаунта.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByNameAsync(userName);
                Assert.NotNull(user);
                var update = await users.UpdateSecurityStampAsync(user);
                Assert.True(update.Succeeded);
            }

            // Та сама cookie, але stamp у ній уже не збігається зі збереженим.
            using var after = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        }
        finally
        {
            await using var scope = host.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(userName);
            if (user is not null) await users.DeleteAsync(user);
        }
    }
}