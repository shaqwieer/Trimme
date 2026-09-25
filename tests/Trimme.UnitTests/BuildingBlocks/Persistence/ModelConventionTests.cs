using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shouldly;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.UnitTests.BuildingBlocks.Persistence;

/// <summary>
/// Builds the real Npgsql model (no connection is opened) to verify the shared persistence conventions:
/// schema per module, snake_case naming, strongly typed ID conversion and xmin concurrency tokens.
/// </summary>
public sealed class ModelConventionTests
{
    [Fact]
    public void Module_entities_default_to_the_module_schema_with_snake_case_names()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ConventionWidget))!;

        entity.GetSchema().ShouldBe("conventions");
        entity.GetTableName().ShouldBe("convention_widgets");
        entity.FindProperty(nameof(ConventionWidget.DisplayName))!.GetColumnName().ShouldBe("display_name");
    }

    [Fact]
    public void Strongly_typed_ids_are_stored_as_uuid()
    {
        using var context = CreateContext();
        var id = context.Model.FindEntityType(typeof(ConventionWidget))!.FindProperty(nameof(ConventionWidget.Id))!;

        id.GetValueConverter().ShouldBeOfType<EntityIdValueConverter<WidgetId>>();
        id.GetColumnType().ShouldBe("uuid");
    }

    [Fact]
    public void Concurrency_versioned_entities_use_xmin_row_version()
    {
        using var context = CreateContext();
        var version = context.Model.FindEntityType(typeof(ConventionWidget))!.FindProperty(nameof(ConventionWidget.Version))!;

        version.IsConcurrencyToken.ShouldBeTrue();
        version.GetColumnName().ShouldBe("xmin");
        version.GetColumnType().ShouldBe("xid");
    }

    [Fact]
    public void Required_extensions_are_declared()
    {
        using var context = CreateContext();
        var extensions = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetAnnotations()
            .Select(a => a.Name)
            .Where(n => n.StartsWith("Npgsql:PostgresExtension:", StringComparison.Ordinal))
            .ToArray();

        extensions.ShouldContain("Npgsql:PostgresExtension:postgis");
        extensions.ShouldContain("Npgsql:PostgresExtension:btree_gist");
    }

    private static TrimmeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrimmeDbContext>();
        options.UseTrimmeNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
        return new TrimmeDbContext(options.Options, [new ConventionModule()]);
    }
}

internal readonly record struct WidgetId(Guid Value) : IEntityId<WidgetId>
{
    public static WidgetId From(Guid value) => new(value);
}

internal sealed class ConventionWidget : AggregateRoot<WidgetId>, IConcurrencyVersioned
{
    public ConventionWidget(WidgetId id, string displayName)
        : base(id)
    {
        DisplayName = displayName;
    }

    private ConventionWidget()
    {
    }

    public string DisplayName { get; private set; } = string.Empty;

    public uint Version { get; private set; }
}

internal sealed class ConventionWidgetConfiguration : IEntityTypeConfiguration<ConventionWidget>
{
    public void Configure(EntityTypeBuilder<ConventionWidget> builder)
    {
        builder.ToTable("convention_widgets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.DisplayName).HasMaxLength(100);
        builder.Ignore(w => w.DomainEvents);
    }
}

/// <summary>Test module living in the unit-test assembly, so only the widget above is picked up.</summary>
internal sealed class ConventionModule : ModuleBase
{
    public override string Name => "Conventions";

    public override string Schema => "conventions";
}
