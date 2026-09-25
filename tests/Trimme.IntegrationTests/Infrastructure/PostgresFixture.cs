using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Trimme.IntegrationTests.Infrastructure.PostgresFixture))]

namespace Trimme.IntegrationTests.Infrastructure;

/// <summary>
/// One PostGIS container per test run (D-038: <c>postgis/postgis:17-3.5</c>).
/// Each test class that needs isolation creates its own database via <see cref="CreateDatabaseAsync"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgis/postgis:17-3.5";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("trimme_tests")
        .WithUsername("trimme")
        .WithPassword("trimme_tests")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)].ToLowerInvariant();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }
}
