using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Persistence;

public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_ApplyToEmptyDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("migrations", ct));

        await factory.MigrateAsync(ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldNotBeEmpty();

        var extensions = await ReadInstalledExtensionsAsync(factory.ConnectionString, ct);
        extensions.ShouldContain("postgis");
        extensions.ShouldContain("btree_gist");
    }

    [Fact]
    public async Task Migrations_AreIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("migrations_twice", ct));

        await factory.MigrateAsync(ct);
        await factory.MigrateAsync(ct);

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_HasNoPendingChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("model", ct));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();

        db.Database.HasPendingModelChanges().ShouldBeFalse(
            "The EF model differs from the last migration. Run: dotnet ef migrations add <Name> --project src/Trimme.Migrations --startup-project apps/api/Trimme.Api");
    }

    [Fact]
    public async Task Migration_history_is_stored_in_public_schema()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "history", ct);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__ef_migrations_history'",
            connection);
        ((long)(await command.ExecuteScalarAsync(ct))!).ShouldBe(1);
    }

    private static async Task<List<string>> ReadInstalledExtensionsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT extname FROM pg_extension", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
