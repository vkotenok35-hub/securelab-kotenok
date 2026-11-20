using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class Lab06SearchRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // Окремий host: лічильники належать host, тому інші тести не отримають 429
    [Fact]
    public async Task Search_ExceedingQuota_Returns429ForOneUser_NotForAnother_ThenRecovers()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        using var alice = await host.LoginAsync("alice", SeedPassword());
        using var bob = await host.LoginAsync("bob", SeedPassword());

        for (var i = 0; i < 20; i++)
        {
            using var accepted = await alice.GetAsync("/api/incidents/search?q=USB");
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var denied = await alice.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);

        // інший partition має власний лічильник
        using var otherUser = await bob.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);

        // відновлення після вікна
        await Task.Delay(TimeSpan.FromSeconds(12));
        using var recovered = await alice.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }
}