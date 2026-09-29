using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using Shouldly;
using Trimme.Api;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.Modules.Administration.Domain;

namespace Trimme.ArchitectureTests;

/// <summary>
/// Tenancy guard rails (R-TEN-02, R-TEN-05). Each rule is also run against a deliberately violating probe in this
/// assembly, so a rule that silently matches nothing fails the build.
/// </summary>
public sealed partial class TenancyRules
{
    /// <summary>Entities that carry a shop id but are neither shop-owned nor tenant members, with the reason.</summary>
    private static readonly Dictionary<Type, string> UnscopedShopIdAllowList = new()
    {
        [typeof(AuditEntry)] = "Platform audit trail; shops never read it.",
        [typeof(Trimme.Modules.Shops.Domain.ShopLocation)] = "Owned value stored in the shop row itself; its key is that shop's id.",
        [typeof(Trimme.Modules.Subscriptions.Domain.SubscriptionCoverage)] =
            "Platform read model (end date, suspended flag) that discovery and booking gates read for any shop; no commercial data (D-078).",
        [typeof(Trimme.Modules.Shops.Domain.OnlineBookingPause)] =
            "Pause state keyed by the shop id, read for any shop by the bookability gate and discovery; written only by the shop's own pause command, tenant from claims (D-083).",
        [typeof(Trimme.Modules.Reviews.Domain.RatingAggregate)] =
            "Platform read model of rating totals (counts per star, no personal data) that discovery sorts and filters for any shop; written only with a review in the same unit of work (D-092).",
    };

    private static readonly Assembly[] ProductionAssemblies =
    [
        .. ModuleCatalog.All.Select(m => m.Assembly),
        typeof(IEntityId).Assembly,
        typeof(IAdminDataScope).Assembly,
        typeof(TrimmeDbContext).Assembly,
        typeof(IModule).Assembly,
        typeof(ModuleCatalog).Assembly,
    ];

    [Fact]
    public void AllShopOwnedEntities_HaveTenantFilter()
    {
        using var context = CreateContext(includeProbes: true);
        var model = context.Model;
        var shopOwned = model.GetEntityTypes().Where(e => typeof(IShopOwned).IsAssignableFrom(e.ClrType)).ToArray();

        shopOwned.ShouldContain(e => e.ClrType == typeof(ProbeShopOwned), "the probe proves the convention runs");
        var root = model.GetEntityTypes().Single(e => typeof(ITenantRoot).IsAssignableFrom(e.ClrType));
        foreach (var entity in shopOwned)
        {
            entity.GetDeclaredQueryFilters().Select(f => f.Key).ShouldContain(TrimmeDbContext.TenantFilterName, entity.ClrType.Name);
            entity.GetForeignKeys().ShouldContain(fk => fk.PrincipalEntityType == root, $"{entity.ClrType.Name} must reference the tenant root");
        }
    }

    [Fact]
    public void EntitiesWithAShopId_AreTenantScoped()
    {
        using (var production = CreateContext(includeProbes: false))
        {
            UnscopedShopIdEntities(production.Model).ShouldBeEmpty();
        }

        using var probed = CreateContext(includeProbes: true);
        UnscopedShopIdEntities(probed.Model).ShouldBe([nameof(ProbeUnscoped)]);
    }

    [Fact]
    public void IgnoreQueryFilters_IsNeverCalled()
    {
        ProductionAssemblies.SelectMany(CallersOf("IgnoreQueryFilters")).ShouldBeEmpty(
            "Bypass the tenant filter only through IAdminDataScope or ISystemDataScope (R-TEN-05).");

        CallersOf("IgnoreQueryFilters")(typeof(TenancyRules).Assembly).ShouldContain(m => m.Contains(nameof(ProbeQueries), StringComparison.Ordinal));
    }

    [Fact]
    public void AdminDataScope_IsUsedOnlyByAdminUseCases()
    {
        ScopeUsersOutside<IAdminDataScope>(ProductionAssemblies, AdminNamespace()).ShouldBeEmpty();
        ScopeUsersOutside<IAdminDataScope>([typeof(TenancyRules).Assembly], AdminNamespace()).ShouldContain(typeof(ProbeMisplacedScopeUser).FullName);
    }

    [Fact]
    public void SystemDataScope_IsUsedOnlyByHostingSeedingAndJobs()
    {
        ScopeUsersOutside<ISystemDataScope>(ProductionAssemblies, SystemNamespace()).ShouldBeEmpty();
        ScopeUsersOutside<ISystemDataScope>([typeof(TenancyRules).Assembly], SystemNamespace()).ShouldContain(typeof(ProbeMisplacedScopeUser).FullName);
    }

    [Fact]
    public void CrossModuleShopScopedReferences_ResolveToTheRealEntity()
    {
        // Services references professionals by entity type name (D-073). EF must bind it to the real Professional
        // entity, not invent a CLR-less shared-type entity, and the FK must be the composite (shop_id, professional_id).
        using var context = CreateContext(includeProbes: false);
        var professionals = context.Model.GetEntityTypes().Where(e => e.Name == "Trimme.Modules.Professionals.Domain.Professional").ToList();
        professionals.Count.ShouldBe(1);
        professionals[0].ClrType.Name.ShouldBe("Professional");
        professionals[0].HasSharedClrType.ShouldBeFalse();

        var assignment = context.Model.GetEntityTypes().Single(e => e.ClrType.Name == "ProfessionalServiceAssignment");
        var toProfessional = assignment.GetForeignKeys().Single(fk => fk.PrincipalEntityType == professionals[0]);
        toProfessional.Properties.Select(p => p.GetColumnName()).ShouldBe(["shop_id", "professional_id"]);
        toProfessional.PrincipalKey.Properties.Select(p => p.GetColumnName()).ShouldBe(["shop_id", "id"]);
    }

    [Fact]
    public void PublicDataScope_IsUsedOnlyByPublicUseCases()
    {
        ScopeUsersOutside<IPublicDataScope>(ProductionAssemblies, PublicNamespace()).ShouldBeEmpty();
        ScopeUsersOutside<IPublicDataScope>([typeof(TenancyRules).Assembly], PublicNamespace()).ShouldContain(typeof(ProbeMisplacedScopeUser).FullName);
    }

    private static IEnumerable<string> UnscopedShopIdEntities(IModel model) =>
        model.GetEntityTypes()
            .Where(e => e.FindProperty(nameof(IShopOwned.ShopId)) is not null)
            .Where(e => !typeof(IShopOwned).IsAssignableFrom(e.ClrType)
                        && !typeof(ITenantMember).IsAssignableFrom(e.ClrType)
                        && !UnscopedShopIdAllowList.ContainsKey(e.ClrType))
            .Select(e => e.ClrType.Name);

    /// <summary>Types that depend on <typeparamref name="TScope"/> outside the allowed namespaces (the implementation and its registration excepted).</summary>
    private static List<string?> ScopeUsersOutside<TScope>(IEnumerable<Assembly> assemblies, Regex allowedNamespace)
    {
        var users = Types.InAssemblies(assemblies)
            .That().HaveDependencyOnAny(typeof(TScope).FullName!)
            .GetTypes()
            .Select(t => t.ReflectionType)
            .Where(t => t != typeof(TScope))
            .Where(t => !typeof(TScope).IsAssignableFrom(t))
            .Where(t => t.Name != "SecuritySetup")
            .Where(t => !allowedNamespace.IsMatch(t.Namespace ?? string.Empty));
        return [.. users.Select(t => t.FullName)];
    }

    private static Func<Assembly, IEnumerable<string>> CallersOf(string methodName) => assembly =>
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        return module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody && m.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference target
                && target.Name == methodName))
            .Select(m => m.FullName)
            .ToList();
    };

    private static TrimmeDbContext CreateContext(bool includeProbes)
    {
        var options = new DbContextOptionsBuilder<TrimmeDbContext>();
        options.UseTrimmeNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
        IEnumerable<IModelContributor> contributors = includeProbes ? [.. ModuleCatalog.All, new ProbeModule()] : ModuleCatalog.All;
        return new TrimmeDbContext(options.Options, contributors);
    }

    [GeneratedRegex(@"\.Application\.Admin(\.|$)")]
    private static partial Regex AdminNamespace();

    [GeneratedRegex(@"(\.Seeding(\.|$)|\.Jobs(\.|$)|^Trimme\.Api\.Hosting$)")]
    private static partial Regex SystemNamespace();

    /// <summary>
    /// Public read models, and customer use cases (D-085): a customer's booking reads the booked shop's schedule and
    /// bookings through the read-only public scope for that one shop.
    /// </summary>
    [GeneratedRegex(@"\.Application\.(Public|Customer)(\.|$)")]
    private static partial Regex PublicNamespace();
}

// ---------------------------------------------------------------- deliberate violations (probes)

internal sealed class ProbeModule : IModelContributor
{
    public string Schema => "arch_probes";

    public Assembly Assembly => typeof(ProbeModule).Assembly;

    public void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeShopOwned>().ToTable("probe_shop_owned", Schema).HasKey(p => p.Id);
        modelBuilder.Entity<ProbeUnscoped>().ToTable("probe_unscoped", Schema).HasKey(p => p.Id);
    }
}

internal sealed class ProbeShopOwned : IShopOwned
{
    public Guid Id { get; set; }

    public ShopId ShopId { get; set; }
}

/// <summary>Has a ShopId but opts out of tenancy: exactly what the rule must reject.</summary>
internal sealed class ProbeUnscoped
{
    public Guid Id { get; set; }

    public ShopId ShopId { get; set; }
}

internal static class ProbeQueries
{
    public static IQueryable<ProbeShopOwned> Bypass(IQueryable<ProbeShopOwned> query) => query.IgnoreQueryFilters();
}

internal sealed class ProbeMisplacedScopeUser(IAdminDataScope admin, ISystemDataScope system, IPublicDataScope published)
{
    public IDisposable Admin() => admin.Begin();

    public IDisposable System() => system.Begin();

    public IDisposable Public() => published.Begin(shopId: null);
}
