using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Administration.Domain;

namespace Trimme.Modules.Administration.Infrastructure;

internal sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings", t =>
        {
            t.HasCheckConstraint("ck_platform_settings_singleton", $"id = '{PlatformSettingsId.Singleton.Value}'");
            t.HasCheckConstraint("ck_platform_settings_ranges",
                "min_lead_time_minutes BETWEEN 0 AND 1440 AND booking_horizon_days BETWEEN 1 AND 365 " +
                "AND cancellation_cutoff_minutes BETWEEN 0 AND 10080 AND review_window_days BETWEEN 1 AND 90 " +
                "AND reminder_offset_minutes BETWEEN 5 AND 1440 AND expiring_soon_threshold_days BETWEEN 1 AND 90 " +
                "AND map_default_zoom BETWEEN 3 AND 18");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ExpiredSubscriptionEnforcement).HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.DefaultLocale).HasMaxLength(5);
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(s => s.TimeZone).HasMaxLength(60);
        builder.Property(s => s.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Ignore(s => s.Editable);
    }
}

/// <summary>
/// Inserts the default settings row when it is missing (<c>migrate</c> and test hosts). It never overwrites an admin's
/// edit: an existing row is left exactly as it is.
/// </summary>
internal sealed class PlatformSettingsSynchronizer : IReferenceDataSynchronizer
{
    public int Order => 50;

    public string Name => "platform-settings";

    public async Task SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        if (!await db.Set<PlatformSettings>().AnyAsync(cancellationToken))
        {
            db.Add(PlatformSettings.CreateDefault(services.GetRequiredService<TimeProvider>().GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Reads the settings once per scope; the defaults stand in if the row has not been created yet.</summary>
internal sealed class PlatformSettingsReader(TrimmeDbContext db) : IPlatformSettings
{
    private PlatformSettingsSnapshot? _snapshot;

    public async Task<PlatformSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is null)
        {
            var row = await db.Set<PlatformSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            _snapshot = (row ?? PlatformSettings.CreateDefault(DateTimeOffset.UnixEpoch)).ToSnapshot();
        }

        return _snapshot;
    }
}
