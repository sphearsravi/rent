using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RentCollection.Infrastructure.Data;

namespace RentCollection.IntegrationTests;

/// <summary>
/// Sprint 5 A6 — variant of <see cref="WebApplicationFactory{T}"/> that
/// swaps the SQL Server registration for an InMemory provider. Lets us
/// exercise anonymous endpoints (health probes, /api/app/version, OpenAPI
/// spec) in CI without Docker, complementing the Testcontainers suite
/// that covers real-DB flows.
///
/// Don't use this for anything that actually depends on SQL semantics
/// (transactions, raw SQL, triggers, sequences) — point those at
/// <see cref="SqlServerFixture"/> instead.
/// </summary>
public sealed class InMemoryAppFactory : WebApplicationFactory<Program>
{
    // Static ctor — set env vars BEFORE WebApplication.CreateBuilder runs.
    // The Hangfire SQL init in Program.cs is evaluated against the host
    // builder's config at registration time; ConfigureAppConfiguration
    // below sometimes loses the race. Env vars are read before either.
    static InMemoryAppFactory()
    {
        Environment.SetEnvironmentVariable("Hangfire__Enabled", "false");
        Environment.SetEnvironmentVariable("OpenTelemetry__Endpoint", "");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Anything that hits ConnectionStrings:DefaultConnection
                // won't be reached because we override the DbContext below.
                ["ConnectionStrings:DefaultConnection"] = "Server=skipped;",
                // OpenTelemetry endpoint left blank → exporter is a no-op
                // in tests, matches Sprint 3 A4 fail-open default.
                ["OpenTelemetry:Endpoint"] = "",
                // Skip Hangfire SQL init — the InMemory DbContext can't
                // serve Hangfire's schema. Recurring jobs are out of scope
                // for endpoint tests anyway.
                ["Hangfire:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Strip every EF Core SQL-Server descriptor and replace with
            // InMemory. The single `DbContextOptions<T>` remove isn't
            // enough — DI registers a handful of related types when
            // `UseSqlServer` is called.
            var efDescriptors = services
                .Where(d => d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true
                            || (d.ImplementationType?.FullName?.StartsWith("Microsoft.EntityFrameworkCore.SqlServer") ?? false)
                            || d.ServiceType == typeof(ApplicationDbContext)
                            || d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                            || d.ServiceType == typeof(DbContextOptions))
                .ToList();
            foreach (var d in efDescriptors) services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseInMemoryDatabase("integration-test-" + Guid.NewGuid()));
        });

        // Force a known environment so the app picks up dev-only defaults
        // (e.g. Swagger is enabled).
        builder.UseEnvironment("Development");
    }
}
