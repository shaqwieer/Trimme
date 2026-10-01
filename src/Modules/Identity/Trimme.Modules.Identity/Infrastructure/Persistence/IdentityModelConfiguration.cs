using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Maps the ASP.NET Core Identity types onto the shared context (what <c>IdentityDbContext</c> would do), with every
/// table explicitly in the <c>identity</c> schema: the generic Identity types live outside this module's assembly, so
/// the schema-per-module convention would not reach them.
/// </summary>
internal static class IdentityModel
{
    public const string Schema = "identity";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("users", Schema);
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasMaxLength(256);
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasMaxLength(256);
            user.Ignore(u => u.PhoneNumber);
            user.Property(u => u.UserType).HasConversion<string>().HasMaxLength(20);
            user.Property(u => u.DisplayName).HasMaxLength(100);
            user.Property(u => u.PreferredLocale).HasMaxLength(5);
            user.Property(u => u.PhoneLookupHash).HasMaxLength(64);

            user.HasIndex(u => u.NormalizedUserName).IsUnique();
            user.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("normalized_email IS NOT NULL");
            user.HasIndex(u => u.PhoneLookupHash).IsUnique().HasFilter("phone_lookup_hash IS NOT NULL");
            user.HasIndex(u => u.ShopId);

            // The customers directory (newest first) and the overview's new customers (Phase 17).
            user.HasIndex(u => new { u.UserType, u.CreatedAt });

            user.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
            user.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
            user.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
            user.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(r => r.UserId).IsRequired();
        });

        builder.Entity<ApplicationRole>(role =>
        {
            role.ToTable("roles", Schema);
            role.HasKey(r => r.Id);
            role.Property(r => r.Id).ValueGeneratedNever();
            role.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
            role.Property(r => r.Name).HasMaxLength(256);
            role.Property(r => r.NormalizedName).HasMaxLength(256);
            role.Property(r => r.UserType).HasConversion<string>().HasMaxLength(20);
            role.HasIndex(r => r.NormalizedName).IsUnique();

            role.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(r => r.RoleId).IsRequired();
            role.HasMany<IdentityRoleClaim<Guid>>().WithOne().HasForeignKey(c => c.RoleId).IsRequired();
        });

        builder.Entity<IdentityUserClaim<Guid>>(claim =>
        {
            claim.ToTable("user_claims", Schema);
            claim.HasKey(c => c.Id);
        });

        builder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.ToTable("user_logins", Schema);
            login.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            login.Property(l => l.LoginProvider).HasMaxLength(128);
            login.Property(l => l.ProviderKey).HasMaxLength(128);
        });

        builder.Entity<IdentityUserToken<Guid>>(token =>
        {
            token.ToTable("user_tokens", Schema);
            token.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            token.Property(t => t.LoginProvider).HasMaxLength(128);
            token.Property(t => t.Name).HasMaxLength(128);
        });

        builder.Entity<IdentityUserRole<Guid>>(userRole =>
        {
            userRole.ToTable("user_roles", Schema);
            userRole.HasKey(r => new { r.UserId, r.RoleId });
        });

        builder.Entity<IdentityRoleClaim<Guid>>(claim =>
        {
            claim.ToTable("role_claims", Schema);
            claim.HasKey(c => c.Id);
        });
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", IdentityModel.Schema);
        builder.HasKey(p => p.Code);
        builder.Property(p => p.Code).HasMaxLength(100);
        builder.Property(p => p.UserType).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", IdentityModel.Schema);
        builder.HasKey(g => new { g.RoleId, g.PermissionCode });
        builder.Property(g => g.PermissionCode).HasMaxLength(100);
        builder.HasOne<ApplicationRole>().WithMany().HasForeignKey(g => g.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(g => g.PermissionCode).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions", IdentityModel.Schema);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.RevocationReason).HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.DeviceLabel).HasMaxLength(80);
        builder.Property(s => s.IpHash).HasMaxLength(64);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.UserId);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", IdentityModel.Schema);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasOne<UserSession>().WithMany().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges", IdentityModel.Schema);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.PhoneHash).HasMaxLength(64);
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        builder.Property(c => c.Locale).HasMaxLength(5);
        builder.Property(c => c.IpHash).HasMaxLength(64);
        builder.HasIndex(c => new { c.PhoneHash, c.CreatedAt });
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations", IdentityModel.Schema);
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.NormalizedEmail).HasMaxLength(256);
        builder.Property(i => i.UserType).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.RoleName).HasMaxLength(256);
        builder.Property(i => i.TokenHash).HasMaxLength(64);
        builder.Property(i => i.Locale).HasMaxLength(5);
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.NormalizedEmail);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
