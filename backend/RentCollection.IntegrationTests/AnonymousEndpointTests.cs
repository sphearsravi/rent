using FluentAssertions;
using System.Net;
using System.Text.Json;

namespace RentCollection.IntegrationTests;

/// <summary>
/// Sprint 5 A6 — anonymous endpoints that don't need a real database.
/// Runs against <see cref="InMemoryAppFactory"/> so the suite is fast
/// and Docker-free; the Testcontainers suite covers anything that
/// actually needs SQL.
/// </summary>
public sealed class AnonymousEndpointTests : IClassFixture<InMemoryAppFactory>
{
    private readonly InMemoryAppFactory _factory;
    public AnonymousEndpointTests(InMemoryAppFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthLive_ReturnsHealthyJson()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
    }

    [Fact]
    public async Task HealthLegacyAlias_StillWorks()
    {
        // /health was the original path; we kept it as an alias for
        // anything pointing at the pre-Sprint-5 endpoint.
        using var client = _factory.CreateClient();
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAppVersion_Android_ReturnsConfiguredValues()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/app/version?platform=android");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("minSupportedVersion").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("latestVersion").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAppVersion_Ios_DoesNotErrorWithMissingStoreUrl()
    {
        // The iOS config slot ships with StoreUrl="" — make sure the
        // controller returns it gracefully rather than 500ing.
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/app/version?platform=ios");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SwaggerJson_IsServed()
    {
        // Forces the spec to render; failure here means a [HttpGet]/[Route]
        // somewhere has a duplicate or unresolvable schema and CI should fail.
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"openapi\"");
        body.Should().Contain("\"paths\"");
    }

    [Fact]
    public async Task UnauthenticatedApiCall_Returns401NotRedirect()
    {
        // Tenant API expects JWT; without it the response must be 401
        // (not 302 to the cookie login). This catches the regression where
        // someone forgets to wire JwtBearer as the API scheme.
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var response = await client.GetAsync("/api/tenant/1/profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
