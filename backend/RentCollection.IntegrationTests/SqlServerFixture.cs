using Testcontainers.MsSql;

namespace RentCollection.IntegrationTests;

/// <summary>
/// xUnit collection fixture that spins up SQL Server 2022 in a Docker
/// container once per test session. Tests reach it via <see cref="ConnectionString"/>.
///
/// Skip behaviour: if Docker isn't running the fixture's `InitializeAsync`
/// throws — xUnit reports a clear "Docker required" failure rather than
/// the cryptic mid-test connection error you'd get otherwise. CI sets up
/// Docker via the runner's built-in service; locally, `docker desktop`
/// must be running before `dotnet test` is invoked.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container not started");

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("IntegrationTest!Pwd123")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            // Surface a clearer message than the raw Testcontainers
            // "Docker daemon not responding" stack trace.
            throw new InvalidOperationException(
                "Integration tests require Docker. Start Docker Desktop / Docker " +
                "Engine and re-run. To skip integration tests locally use: " +
                "dotnet test --filter \"FullyQualifiedName!~IntegrationTests\".",
                ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(SqlServerCollection))]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
