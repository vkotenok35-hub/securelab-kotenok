using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class Lab04ReadAccessTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid BobSearchIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000004");

    private static string SeedPassword() => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private Task<HttpClient> LoginAs(string userName) => factory.LoginAsync(userName, SeedPassword());

    private static async Task<List<JsonElement>> ReadArrayAsync(HttpClient client, string url)
    {
        var items = await client.GetFromJsonAsync<List<JsonElement>>(url);
        Assert.NotNull(items);
        return items;
    }

    private static async Task<Dictionary<string, int>> SummaryAsync(HttpClient client)
    {
        var items = await ReadArrayAsync(client, "/api/incidents/severity-summary");
        return items.ToDictionary(
            item => item.GetProperty("severity").GetString()!,
            item => item.GetProperty("count").GetInt32());
    }

    // A-01: анонімний запит до захищеного маршруту — 401, не HTML redirect
    [Fact]
    public async Task A01_AnonymousDetails_Returns401WithoutRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // A-02: owner читає власний ресурс
    [Fact]
    public async Task A02_Owner_ReadsOwnIncident()
    {
        using var client = await LoginAs("alice");
        using var response = await client.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceIncidentId, document.RootElement.GetProperty("id").GetGuid());
    }

    // A-03: інший Reporter дістає 404 і жодних даних Bob
    [Fact]
    public async Task A03_OtherReporter_GetsNotFound_WithoutForeignData()
    {
        using var client = await LoginAs("alice");
        using var response = await client.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Боб Мельник", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Невідома спроба входу", text, StringComparison.Ordinal);
    }

    // A-03R: Analyst читає ресурс Alice для аналізу
    [Fact]
    public async Task A03R_Analyst_ReadsReporterIncidentForAnalysis()
    {
        using var client = await LoginAs("morgan");
        using var response = await client.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A-05: список містить лише дозволену множину
    [Fact]
    public async Task A05_List_ContainsOnlyOwnIncidents()
    {
        using var client = await LoginAs("alice");
        var incidents = await client.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.DoesNotContain(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
        Assert.DoesNotContain(incidents, incident => incident.Id == BobSearchIncidentId);
    }

    // A-10: чужий і неіснуючий id нерозрізнювані (однакові status, Content-Type і тіло без traceId)
    [Fact]
    public async Task A10_ForeignAndUnknownId_ReturnIdenticalNotFound()
    {
        using var client = await LoginAs("alice");
        using var foreign = await client.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.ToString(),
            foreign.Content.Headers.ContentType?.ToString());

        var foreignBody = JsonNode.Parse(await foreign.Content.ReadAsStringAsync())!.AsObject();
        var unknownBody = JsonNode.Parse(await unknown.Content.ReadAsStringAsync())!.AsObject();
        foreignBody.Remove("traceId"); // єдина дозволена відмінність
        unknownBody.Remove("traceId");
        Assert.True(JsonNode.DeepEquals(unknownBody, foreignBody));
    }

    // A-11: search і severity-summary для Reporter обмежено власними даними
    [Fact]
    public async Task A11_Reporter_SearchAndSummary_AreLimitedToOwnData()
    {
        using var client = await LoginAs("alice");
        var summary = await SummaryAsync(client);
        var list = await client.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");
        var search = await ReadArrayAsync(client,
            $"/api/incidents/search?q={Uri.EscapeDataString("O'Brien")}");

        Assert.NotNull(list);
        Assert.Equal(0, summary["High"]);                       // High — лише в Bob
        Assert.Equal(list.Count, summary.Values.Sum());          // агрегат збігається з дозволеною множиною
        Assert.Empty(search);                                    // O'Brien належить Bob
    }

    // A-11: Analyst бачить усі дані для аналізу
    [Fact]
    public async Task A11_Analyst_SearchAndSummary_SeeAllIncidents()
    {
        using var client = await LoginAs("morgan");
        var summary = await SummaryAsync(client);
        var search = await ReadArrayAsync(client,
            $"/api/incidents/search?q={Uri.EscapeDataString("O'Brien")}");

        Assert.True(summary["High"] >= 1);
        Assert.Contains(search, item => item.GetProperty("id").GetGuid() == BobSearchIncidentId);
    }
}