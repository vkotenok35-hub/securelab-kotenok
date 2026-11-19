using System.Net.Http.Json;

namespace SecureLab.Api.Tests;

public static class AntiforgeryTestExtensions
{
    public const string TokenRoute = "/api/csrf-token";
    public const string HeaderName = "X-CSRF-TOKEN";

    private sealed record TokenResponse(string Token);

    // Одержує request token тим самим клієнтом і додає його до всіх наступних запитів
    public static async Task UseAntiforgeryTokenAsync(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(HeaderName);
        using var response = await client.GetAsync(TokenRoute);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrWhiteSpace(body?.Token))
            throw new InvalidOperationException("Missing request token.");
        client.DefaultRequestHeaders.Add(HeaderName, body.Token);
    }
}