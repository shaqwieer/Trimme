using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Administration.Domain;

namespace Trimme.Modules.Administration.Application.Admin;

// Platform settings (D-076). Locale, currency, time zone and country are shown but fixed in v1; the rest is editable
// with Admin.Settings.Edit, version-checked and audited with the changed field names.

public sealed record PlatformSettingsResponse(
    int MinLeadTimeMinutes,
    int BookingHorizonDays,
    int SlotStepMinutes,
    int CancellationCutoffMinutes,
    int ReviewWindowDays,
    int ReminderOffsetMinutes,
    int ExpiringSoonThresholdDays,
    SubscriptionEnforcement ExpiredSubscriptionEnforcement,
    bool HidePausedShopsFromDiscovery,
    string DefaultLocale,
    string Currency,
    string TimeZone,
    string CountryCode,
    double MapDefaultLatitude,
    double MapDefaultLongitude,
    int MapDefaultZoom,
    DateTimeOffset UpdatedAt,
    uint Version);

internal sealed record GetPlatformSettingsQuery : IQuery<PlatformSettingsResponse>;

internal sealed record UpdatePlatformSettingsCommand(
    int MinLeadTimeMinutes,
    int BookingHorizonDays,
    int SlotStepMinutes,
    int CancellationCutoffMinutes,
    int ReviewWindowDays,
    int ReminderOffsetMinutes,
    int ExpiringSoonThresholdDays,
    SubscriptionEnforcement ExpiredSubscriptionEnforcement,
    bool HidePausedShopsFromDiscovery,
    double MapDefaultLatitude,
    double MapDefaultLongitude,
    int MapDefaultZoom,
    uint Version) : ICommand<Result<PlatformSettingsResponse>>;

internal sealed class UpdatePlatformSettingsValidator : AbstractValidator<UpdatePlatformSettingsCommand>
{
    public UpdatePlatformSettingsValidator()
    {
        RuleFor(c => c.MinLeadTimeMinutes).InclusiveBetween(0, 1440).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.BookingHorizonDays).InclusiveBetween(1, 365).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.SlotStepMinutes).Must(PlatformSettings.SlotSteps.Contains).WithErrorCode("validation.invalid");
        RuleFor(c => c.CancellationCutoffMinutes).InclusiveBetween(0, 10080).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.ReviewWindowDays).InclusiveBetween(1, 90).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.ReminderOffsetMinutes).InclusiveBetween(5, 1440).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.ExpiringSoonThresholdDays).InclusiveBetween(1, 90).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.ExpiredSubscriptionEnforcement).IsInEnum().WithErrorCode("validation.invalid");
        RuleFor(c => c.MapDefaultLatitude).InclusiveBetween(-90, 90).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.MapDefaultLongitude).InclusiveBetween(-180, 180).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.MapDefaultZoom).InclusiveBetween(3, 18).WithErrorCode("validation.out_of_range");
    }
}

internal static class SettingsReader
{
    public static async Task<PlatformSettings> LoadAsync(TrimmeDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var settings = await db.Set<PlatformSettings>().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            // Normally written by migrate; a fresh database without it still works.
            settings = PlatformSettings.CreateDefault(clock.GetUtcNow());
            db.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
        }

        return settings;
    }

    public static PlatformSettingsResponse Response(PlatformSettings s) => new(
        s.MinLeadTimeMinutes, s.BookingHorizonDays, s.SlotStepMinutes, s.CancellationCutoffMinutes, s.ReviewWindowDays, s.ReminderOffsetMinutes,
        s.ExpiringSoonThresholdDays, s.ExpiredSubscriptionEnforcement, s.HidePausedShopsFromDiscovery, s.DefaultLocale, s.Currency, s.TimeZone,
        s.CountryCode, s.MapDefaultLatitude, s.MapDefaultLongitude, s.MapDefaultZoom, s.UpdatedAt, s.Version);
}

internal sealed class GetPlatformSettingsHandler(TrimmeDbContext db, TimeProvider clock) : IQueryHandler<GetPlatformSettingsQuery, PlatformSettingsResponse>
{
    public async Task<PlatformSettingsResponse> Handle(GetPlatformSettingsQuery query, CancellationToken cancellationToken) =>
        SettingsReader.Response(await SettingsReader.LoadAsync(db, clock, cancellationToken));
}

internal sealed class UpdatePlatformSettingsHandler(TrimmeDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<UpdatePlatformSettingsCommand, Result<PlatformSettingsResponse>>
{
    public async Task<Result<PlatformSettingsResponse>> Handle(UpdatePlatformSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await SettingsReader.LoadAsync(db, clock, cancellationToken);
        db.Entry(settings).Property(s => s.Version).OriginalValue = command.Version;

        var changed = settings.Update(
            new EditablePlatformSettings(
                command.MinLeadTimeMinutes, command.BookingHorizonDays, command.SlotStepMinutes, command.CancellationCutoffMinutes,
                command.ReviewWindowDays, command.ReminderOffsetMinutes, command.ExpiringSoonThresholdDays, command.ExpiredSubscriptionEnforcement,
                command.HidePausedShopsFromDiscovery, command.MapDefaultLatitude, command.MapDefaultLongitude, command.MapDefaultZoom),
            user.UserId,
            clock.GetUtcNow());

        if (changed.Count > 0)
        {
            audit.Record(new AuditRecord("platform_settings.updated", "PlatformSettings", settings.Id.Value.ToString(), null, $"Changed: {string.Join(", ", changed)}", null));
        }

        await db.SaveChangesAsync(cancellationToken);
        return SettingsReader.Response(settings);
    }
}
