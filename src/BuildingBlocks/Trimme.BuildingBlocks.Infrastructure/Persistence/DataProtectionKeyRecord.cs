namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// One ASP.NET Core Data Protection key (XML), stored in <c>infra.data_protection_keys</c> so every API process
/// (and the <c>migrate</c>/<c>seed</c> containers) shares the same key ring. Session cookies, password-reset tokens
/// and encrypted personal data all depend on it surviving restarts.
/// </summary>
public sealed class DataProtectionKeyRecord
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public required string Xml { get; set; }
}
