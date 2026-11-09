using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Application.Admin;

// A shop's WhatsApp switches (D-142): the platform admin turns each of the nine booking messages on or off per shop.
// Only admins see or change them; the shop is never told. The sender checks them when a message is due.

/// <summary>One switch: an audience and event, whether it is on, and the Arabic message it sends (Meta name and active text).</summary>
public sealed record ShopMessageResponse(MessageAudience Audience, MessageEvent Event, bool Enabled, string? TemplateName, string? BodyAr);

/// <summary>The nine switches of a shop, customer first, in the order of a booking's life.</summary>
public sealed record ShopMessagesResponse(Guid ShopId, IReadOnlyList<ShopMessageResponse> Items);

/// <summary>A switch to set. A slot left out keeps its state.</summary>
public sealed record ShopMessageSwitch(MessageAudience Audience, MessageEvent Event, bool Enabled);

internal sealed record GetShopMessagesQuery(Guid ShopId) : IQuery<Result<ShopMessagesResponse>>;

internal sealed record SaveShopMessagesCommand(Guid ShopId, IReadOnlyList<ShopMessageSwitch>? Messages) : ICommand<Result<ShopMessagesResponse>>;

internal static class ShopMessagesErrors
{
    public static Error ShopNotFound() => Error.NotFound("shop.not_found", "The shop was not found.");
}

internal static class ShopMessagesMapper
{
    public static async Task<ShopMessagesResponse> MapAsync(TrimmeDbContext db, ShopId shopId, CancellationToken cancellationToken)
    {
        var off = await db.Set<ShopMessageOptOut>().AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .Select(o => new { o.Audience, o.Event })
            .ToListAsync(cancellationToken);
        var arabic = await db.Set<WhatsAppTemplate>().AsNoTracking().Include(t => t.Versions)
            .Where(t => t.Locale == "ar")
            .ToListAsync(cancellationToken);

        return new ShopMessagesResponse(shopId.Value, [
            .. ShopMessageOptOut.Slots.Select(slot =>
            {
                var active = arabic.FirstOrDefault(t => t.Audience == slot.Audience && t.Event == slot.Event)?.ActiveVersion;
                return new ShopMessageResponse(
                    slot.Audience, slot.Event, !off.Any(o => o.Audience == slot.Audience && o.Event == slot.Event),
                    active?.ProviderTemplateName, active?.Body);
            }),
        ]);
    }
}

internal sealed class GetShopMessagesHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops)
    : IQueryHandler<GetShopMessagesQuery, Result<ShopMessagesResponse>>
{
    public async Task<Result<ShopMessagesResponse>> Handle(GetShopMessagesQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(query.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return ShopMessagesErrors.ShopNotFound();
        }

        return await ShopMessagesMapper.MapAsync(db, shopId, cancellationToken);
    }
}

internal sealed class SaveShopMessagesValidator : AbstractValidator<SaveShopMessagesCommand>
{
    public SaveShopMessagesValidator()
    {
        RuleFor(c => c.Messages).NotNull().WithErrorCode("validation.required");
        RuleForEach(c => c.Messages)
            .Must(m => m is not null && ShopMessageOptOut.IsSlot(m.Audience, m.Event))
            .WithErrorCode("validation.invalid");
        RuleFor(c => c.Messages)
            .Must(messages => messages!.Select(m => (m.Audience, m.Event)).Distinct().Count() == messages!.Count)
            .When(c => c.Messages is not null && c.Messages.All(m => m is not null))
            .WithErrorCode("validation.invalid");
    }
}

/// <summary>Turns a shop's messages on or off (D-142). Idempotent; audited with the slots that changed.</summary>
internal sealed class SaveShopMessagesHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SaveShopMessagesCommand, Result<ShopMessagesResponse>>
{
    public async Task<Result<ShopMessagesResponse>> Handle(SaveShopMessagesCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return ShopMessagesErrors.ShopNotFound();
        }

        var now = clock.GetUtcNow();
        var off = await db.Set<ShopMessageOptOut>().Where(o => o.ShopId == shopId).ToListAsync(cancellationToken);
        var changes = new List<string>();
        foreach (var message in command.Messages!)
        {
            var existing = off.FirstOrDefault(o => o.Audience == message.Audience && o.Event == message.Event);
            if (message.Enabled && existing is not null)
            {
                db.Remove(existing);
                changes.Add($"{message.Audience}.{message.Event} on");
            }
            else if (!message.Enabled && existing is null)
            {
                db.Add(ShopMessageOptOut.Create(shopId, message.Audience, message.Event, now));
                changes.Add($"{message.Audience}.{message.Event} off");
            }
        }

        if (changes.Count > 0)
        {
            audit.Record(new AuditRecord(
                "whatsapp_shop_messages.saved", "Shop", shopId.Value.ToString(), shopId, string.Join(", ", changes)));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ShopMessagesMapper.MapAsync(db, shopId, cancellationToken);
    }
}
