using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Infrastructure;

internal static class JsonColumns
{
    public static readonly ValueComparer<Dictionary<string, string>> DictionaryComparer = new(
        (a, b) => a!.Count == b!.Count && a.All(pair => b.ContainsKey(pair.Key) && b[pair.Key] == pair.Value),
        d => d.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
        d => new Dictionary<string, string>(d, StringComparer.Ordinal));

    public static PropertyBuilder<Dictionary<string, string>> AsJson(this PropertyBuilder<Dictionary<string, string>> property) =>
        property
            .HasConversion(
                d => JsonSerializer.Serialize(d, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? new Dictionary<string, string>(),
                DictionaryComparer)
            .HasColumnType("jsonb");
}

internal sealed class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> builder)
    {
        builder.ToTable("whatsapp_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Event).HasConversion<string>().HasMaxLength(40);
        builder.Property(t => t.Audience).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Locale).HasMaxLength(5);
        builder.HasIndex(t => new { t.Event, t.Audience, t.Locale }).IsUnique();
        builder.HasMany(t => t.Versions).WithOne().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(t => t.ActiveVersion);
        builder.Ignore(t => t.Draft);
    }
}

internal sealed class WhatsAppTemplateVersionConfiguration : IEntityTypeConfiguration<WhatsAppTemplateVersion>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplateVersion> builder)
    {
        builder.ToTable("whatsapp_template_versions", t =>
            t.HasCheckConstraint("ck_whatsapp_template_versions_body", $"char_length(body) BETWEEN 1 AND {TemplateValidator.MaxBodyLength}"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Body).HasMaxLength(TemplateValidator.MaxBodyLength);
        builder.Property(v => v.ProviderTemplateName).HasMaxLength(TemplateValidator.MaxProviderNameLength);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.OwnsMany(v => v.Buttons, b =>
        {
            b.ToJson();
            b.Property(x => x.Target).HasConversion<string>();
        });
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.HasIndex(v => new { v.TemplateId, v.Number }).IsUnique();

        // One draft and one active version per template are kept by the aggregate: every draft save and activation
        // changes the template row, whose version (xmin) makes concurrent edits fail with 409.
        builder.HasIndex(v => new { v.TemplateId, v.Status });
        builder.Ignore(v => v.Content);
    }
}

internal sealed class WhatsAppDispatchConfiguration : IEntityTypeConfiguration<WhatsAppDispatch>
{
    public void Configure(EntityTypeBuilder<WhatsAppDispatch> builder)
    {
        builder.ToTable("whatsapp_dispatches");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Event).HasConversion<string>().HasMaxLength(40);
        builder.Property(d => d.Audience).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Locale).HasMaxLength(5);
        builder.Property(d => d.DedupeKey).HasMaxLength(200);
        builder.Property(d => d.RecipientMasked).HasMaxLength(40);
        builder.Property(d => d.Body).HasMaxLength(TemplateValidator.MaxBodyLength * 4);
        builder.Property(d => d.ProviderTemplateName).HasMaxLength(TemplateValidator.MaxProviderNameLength);
        builder.Property(d => d.ContentHash).HasMaxLength(64);
        builder.Property(d => d.LastError).HasMaxLength(WhatsAppDispatch.MaxErrorLength);
        builder.Property(d => d.ProviderMessageId).HasMaxLength(200);
        builder.Property(d => d.JobId).HasMaxLength(100);
        builder.OwnsMany(d => d.Buttons, b => b.ToJson());
        builder.HasOne<WhatsAppTemplateVersion>().WithMany().HasForeignKey(d => d.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppTemplate>().WithMany().HasForeignKey(d => d.TemplateId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.DedupeKey).IsUnique();
        builder.HasIndex(d => d.CreatedAt);
        builder.HasIndex(d => new { d.Status, d.CreatedAt });
        builder.HasIndex(d => d.BookingId);
        builder.HasIndex(d => new { d.ShopId, d.CreatedAt });
        builder.HasIndex(d => d.ProviderMessageId).HasFilter("provider_message_id IS NOT NULL");
    }
}

internal sealed class ReminderScheduleConfiguration : IEntityTypeConfiguration<ReminderSchedule>
{
    public void Configure(EntityTypeBuilder<ReminderSchedule> builder)
    {
        builder.ToTable("reminder_schedules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Audience).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.JobId).HasMaxLength(100);
        builder.HasIndex(r => r.BookingId);
        builder.HasIndex(r => new { r.BookingId, r.Audience }).IsUnique().HasFilter("status = 'Scheduled'");
        builder.HasIndex(r => new { r.Status, r.DueAt });
    }
}

internal sealed class ShopNotificationConfiguration : IEntityTypeConfiguration<ShopNotification>
{
    public void Configure(EntityTypeBuilder<ShopNotification> builder)
    {
        builder.ToTable("shop_notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasMaxLength(ShopNotification.MaxKindLength);
        builder.Property(n => n.DedupeKey).HasMaxLength(ShopNotification.MaxDedupeKeyLength);
        builder.Property(n => n.Parameters).AsJson();
        builder.HasIndex(n => new { n.ShopId, n.DedupeKey }).IsUnique();
        builder.HasIndex(n => new { n.ShopId, n.CreatedAt });
        builder.HasIndex(n => n.ShopId).HasFilter("read_at IS NULL").HasDatabaseName("ix_shop_notifications_unread");
    }
}

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("user_notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasMaxLength(ShopNotification.MaxKindLength);
        builder.Property(n => n.DedupeKey).HasMaxLength(ShopNotification.MaxDedupeKeyLength);
        builder.Property(n => n.Parameters).AsJson();
        builder.HasIndex(n => new { n.UserId, n.DedupeKey }).IsUnique();
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => n.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_user_notifications_unread");
    }
}
