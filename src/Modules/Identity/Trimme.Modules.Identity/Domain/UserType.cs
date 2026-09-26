namespace Trimme.Modules.Identity.Domain;

/// <summary>
/// The three authenticated user types (spec §2). The names are part of the API contract and of the access-cookie
/// claim, so they must match <c>Trimme.BuildingBlocks.Web.Security.UserTypes</c>.
/// </summary>
public enum UserType
{
    Customer,
    ShopUser,
    PlatformAdmin,
}
