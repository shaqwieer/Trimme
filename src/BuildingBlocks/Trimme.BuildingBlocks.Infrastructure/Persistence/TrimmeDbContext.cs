using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Media;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// The single application DbContext (D-038). Modules contribute their schema and entity
/// configurations through <see cref="IModelContributor"/>; they never create their own contexts.
/// It also enforces tenant isolation centrally (spec §7, D-059): every <see cref="IShopOwned"/> entity gets the
/// <see cref="TenantFilterName"/> query filter, a foreign key to the tenant root, server-side stamping and an
/// immutable <c>ShopId</c>.
/// </summary>
public sealed class TrimmeDbContext : DbContext
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string MigrationsHistorySchema = "public";

    /// <summary>Schema for platform infrastructure tables that belong to no feature module.</summary>
    public const string InfrastructureSchema = "infra";

    /// <summary>Name of the tenant query filter on every shop-owned entity.</summary>
    public const string TenantFilterName = "tenant";

    private readonly IModelContributor[] _contributors;
    private readonly ICurrentTenant? _tenant;
    private int _unrestrictedDepth;
    private bool _publicScope;
    private ShopId? _publicShopId;

    public TrimmeDbContext(DbContextOptions<TrimmeDbContext> options, IEnumerable<IModelContributor> contributors)
        : this(options, contributors, tenant: null)
    {
    }

    public TrimmeDbContext(DbContextOptions<TrimmeDbContext> options, IEnumerable<IModelContributor> contributors, ICurrentTenant? tenant)
        : base(options)
    {
        _contributors = contributors.ToArray();
        _tenant = tenant;
        ModelSignature = string.Join('|', _contributors.Select(c => c.GetType().FullName).Order(StringComparer.Ordinal));
    }

    /// <summary>Identifies the model shape, so contexts built from different contributor sets never share a cached model.</summary>
    internal string ModelSignature { get; }

    // Read by the query filters at query time (not at construction): the tenant is resolved during authentication,
    // which can happen after this context was created.
    private bool Unrestricted => _unrestrictedDepth > 0;

    private bool HasTenant => _tenant?.ShopId is not null;

    private ShopId TenantShopId => _tenant?.ShopId ?? default;

    // A public (anonymous-safe) read scope replaces the caller's tenant with one published shop; see EnterPublicScope.
    private bool PublicScope => _publicScope;

    private bool HasPublicShop => _publicShopId is not null;

    private ShopId PublicShopId => _publicShopId ?? default;

    /// <summary>
    /// Lifts the tenant filter and write checks until disposed. Only the admin and system scope services call this;
    /// feature code uses <see cref="IAdminDataScope"/> or <see cref="ISystemDataScope"/>.
    /// </summary>
    internal IDisposable EnterUnrestrictedScope()
    {
        _unrestrictedDepth++;
        return new UnrestrictedScope(this);
    }

    /// <summary>
    /// Read-only view of public data until disposed (D-066): every shop row is visible (the public directory) and
    /// shop-owned rows only of <paramref name="shopId"/> (none when null), whoever the caller is. The caller's own
    /// tenant is ignored, and saving changes throws while the scope is open. Only <see cref="IPublicDataScope"/> calls this.
    /// </summary>
    internal IDisposable EnterPublicScope(ShopId? shopId)
    {
        if (_publicScope)
        {
            throw new InvalidOperationException("A public data scope is already open.");
        }

        _publicScope = true;
        _publicShopId = shopId;
        return new PublicScopeHandle(this);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

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

        StoredMediaModel.Configure(modelBuilder.Entity<StoredMedia>());

        foreach (var contributor in _contributors)
        {
            contributor.ConfigureModel(modelBuilder);
        }

        ApplyTenancyConventions(modelBuilder);
        ApplyConcurrencyTokenConvention(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        var assemblies = _contributors.Select(c => c.Assembly).Append(typeof(ShopId).Assembly).Distinct();
        foreach (var idType in assemblies.SelectMany(FindEntityIdTypes).Distinct())
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

    private void ApplyTenancyConventions(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType is null && !e.IsOwned()).ToArray();
        var roots = entityTypes.Where(e => typeof(ITenantRoot).IsAssignableFrom(e.ClrType)).ToArray();
        if (roots.Length > 1)
        {
            throw new InvalidOperationException("Exactly one entity may implement ITenantRoot.");
        }

        var root = roots.SingleOrDefault();
        if (root is not null)
        {
            Invoke(nameof(ApplyTenantRootFilter), root.ClrType, modelBuilder);
        }

        foreach (var entityType in entityTypes)
        {
            var shopOwned = typeof(IShopOwned).IsAssignableFrom(entityType.ClrType);
            var member = typeof(ITenantMember).IsAssignableFrom(entityType.ClrType);
            if (shopOwned)
            {
                Invoke(nameof(ApplyShopOwnedFilter), entityType.ClrType, modelBuilder);
            }

            if ((shopOwned || member) && root is not null)
            {
                var shopId = entityType.FindProperty(nameof(IShopOwned.ShopId))
                             ?? throw new InvalidOperationException($"{entityType.ClrType.Name} must map ShopId.");
                if (entityType.FindForeignKeys(shopId).All(fk => fk.PrincipalEntityType != root))
                {
                    var foreignKey = entityType.AddForeignKey(shopId, root.FindPrimaryKey()!, root);
                    foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
                    foreignKey.IsRequired = shopOwned;
                }
            }
        }
    }

    private void Invoke(string method, Type entityClrType, ModelBuilder modelBuilder) =>
        GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityClrType)
            .Invoke(this, [modelBuilder]);

    /// <summary>Shop users see only their shop's rows; everyone else sees none unless an explicit scope is open.</summary>
    private void ApplyShopOwnedFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IShopOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            TenantFilterName,
            entity => Unrestricted
                      || (PublicScope && HasPublicShop && entity.ShopId == PublicShopId)
                      || (!PublicScope && HasTenant && entity.ShopId == TenantShopId));

    /// <summary>Shops are a public directory, but a shop user sees only their own shop row (outside a public scope).</summary>
    private void ApplyTenantRootFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantRoot =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            TenantFilterName,
            shop => Unrestricted || PublicScope || !HasTenant || shop.Id == TenantShopId);

    private void EnforceTenantRules()
    {
        if (_publicScope)
        {
            throw new TenantViolationException("Changes cannot be saved inside a public (read-only) data scope.");
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IShopOwned owned)
            {
                EnforceShopOwned(entry, owned);
            }
            else if (entry is { Entity: ITenantMember, State: EntityState.Modified })
            {
                var shop = entry.Property(nameof(ITenantMember.ShopId));
                if (shop.IsModified && !Equals(shop.OriginalValue, shop.CurrentValue))
                {
                    throw new TenantViolationException($"{entry.Metadata.ClrType.Name}.ShopId cannot change once set.");
                }
            }
        }
    }

    private void EnforceShopOwned(EntityEntry entry, IShopOwned owned)
    {
        var name = entry.Metadata.ClrType.Name;
        var shop = entry.Property(nameof(IShopOwned.ShopId));

        switch (entry.State)
        {
            case EntityState.Added when HasTenant && owned.ShopId.Value == Guid.Empty:
                shop.CurrentValue = TenantShopId;
                break;
            case EntityState.Added when HasTenant && owned.ShopId != TenantShopId:
                throw new TenantViolationException($"Cannot create a {name} for another shop.");
            case EntityState.Added when !HasTenant && !Unrestricted:
                throw new TenantViolationException($"Cannot create a {name} outside a shop or an explicit admin/system scope.");
            case EntityState.Added when owned.ShopId.Value == Guid.Empty:
                throw new TenantViolationException($"A {name} created in an admin/system scope needs an explicit ShopId.");
            case EntityState.Modified when shop.IsModified && !Equals(shop.OriginalValue, shop.CurrentValue):
                throw new TenantViolationException($"{name}.ShopId cannot change.");
            case EntityState.Modified or EntityState.Deleted when !Unrestricted && (!HasTenant || owned.ShopId != TenantShopId):
                throw new TenantViolationException($"Cannot change a {name} of another shop.");
        }
    }

    private sealed class PublicScopeHandle(TrimmeDbContext context) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                context._publicScope = false;
                context._publicShopId = null;
            }
        }
    }

    private sealed class UnrestrictedScope(TrimmeDbContext context) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                context._unrestrictedDepth--;
            }
        }
    }
}
