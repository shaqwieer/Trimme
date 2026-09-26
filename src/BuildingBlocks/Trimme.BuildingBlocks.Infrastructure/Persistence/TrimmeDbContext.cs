using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// The single application DbContext (D-038). Modules contribute their schema and entity
/// configurations through <see cref="IModelContributor"/>; they never create their own contexts.
/// </summary>
public sealed class TrimmeDbContext(DbContextOptions<TrimmeDbContext> options, IEnumerable<IModelContributor> contributors)
    : DbContext(options)
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string MigrationsHistorySchema = "public";

    /// <summary>Schema for platform infrastructure tables that belong to no feature module.</summary>
    public const string InfrastructureSchema = "infra";

    private readonly IModelContributor[] _contributors = contributors.ToArray();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<DataProtectionKeyRecord>(key =>
        {
            key.ToTable("data_protection_keys", InfrastructureSchema);
            key.Property(k => k.Id).UseIdentityAlwaysColumn();
            key.Property(k => k.FriendlyName).HasMaxLength(200);
        });

        foreach (var contributor in _contributors)
        {
            contributor.ConfigureModel(modelBuilder);
        }

        ApplyConcurrencyTokenConvention(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        foreach (var idType in _contributors.SelectMany(c => FindEntityIdTypes(c.Assembly)).Distinct())
        {
            var converterType = typeof(EntityIdValueConverter<>).MakeGenericType(idType);
            configurationBuilder.Properties(idType).HaveConversion(converterType);
        }
    }

    internal static IEnumerable<Type> FindEntityIdTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsValueType: true, IsGenericTypeDefinition: false }
                                       && typeof(IEntityId).IsAssignableFrom(t));

    /// <summary>Maps <see cref="IConcurrencyVersioned.Version"/> to PostgreSQL <c>xmin</c> for optimistic concurrency.</summary>
    private static void ApplyConcurrencyTokenConvention(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(IConcurrencyVersioned).IsAssignableFrom(e.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(IConcurrencyVersioned.Version))
                .IsRowVersion();
        }
    }
}
