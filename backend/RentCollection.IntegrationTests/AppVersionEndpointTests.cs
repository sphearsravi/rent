using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RentCollection.Infrastructure.Data;

namespace RentCollection.IntegrationTests;

/// <summary>
/// Sprint 4 A8 — canonical integration-test shape:
///   * SQL Server in a Testcontainers container (collection fixture)
///   * WebApplicationFactory wires the real DI graph, swapping just the
///     connection string for the container one
///   * one HTTP test hitting an anonymous endpoint
///
/// Add new test classes by:
///   1. apply [Collection(nameof(SqlServerCollection))]
///   2. inject the fixture
///   3. build a factory the same way and reuse it across tests in the class
/// </summary>
[Collection(nameof(SqlServerCollection))]
public class AppVersionEndpointTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private WebApplicationFactory<Program>? _factory;

    public AppVersionEndpointTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _sql.ConnectionString,
                        // Disable Hangfire SQL storage init for tests — we don't
                        // run background jobs and the schema isn't there yet.
                        ["Hangfire:Enabled"] = "false",
                    });
                });
                builder.ConfigureServices(services =>
                {
                    using var scope = services.BuildServiceProvider().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    db.Database.EnsureCreated();
                });
            });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetVersion_ReturnsConfiguredMinSupportedVersion()
    {
        using var client = _factory!.CreateClient();
        var response = await client.GetAsync("/api/app/version?platform=android");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"minSupportedVersion\"");
        body.Should().Contain("\"latestVersion\"");
    }
}
