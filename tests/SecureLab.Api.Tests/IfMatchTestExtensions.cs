using System.Net.Http.Headers;

namespace SecureLab.Api.Tests;

public static class IfMatchTestExtensions
{
    // Читає поточний сильний ETag ресурсу (разом із лапками)
    public static async Task<string> GetETagAsync(this HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag?.Tag ?? throw new InvalidOperationException("Missing ETag.");
    }

    public static Task<HttpResponseMessage> SendWithIfMatchAsync(
        this HttpClient client, HttpMethod method, string route, string? etag,
        HttpContent? content = null, bool weak = false)
    {
        var request = new HttpRequestMessage(method, route) { Content = content };
        if (etag is not null) request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag, weak));
        return client.SendAsync(request);
    }
}