namespace Trimme.Modules.Identity.Domain;

/// <summary>
/// Seed roles (D-018, D-019, D-051). <see cref="RoleDefinition.Managed"/> roles have their grants reset to these
/// defaults on every <c>migrate</c>; the others receive the defaults once, when first created, and are then editable
/// by an admin with <c>Admin.Roles.Manage</c> (Phase 14).
/// </summary>
public static class SystemRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string OperationsManager = "OperationsManager";
    public const string Support = "Support";
    public const string ShopOwner = "ShopOwner";
    public const string ShopStaff = "ShopStaff";
    public const string Customer = "Customer";

    public static IReadOnlyList<RoleDefinition> All { get; } =
    [
        new(SuperAdmin, UserType.PlatformAdmin, Managed: true,
            Permissions.All.Where(p => p.UserType == UserType.PlatformAdmin).Select(p => p.Code).ToArray()),
        new(OperationsManager, UserType.PlatformAdmin, Managed: false,
            Permissions.All
                .Where(p => p.UserType == UserType.PlatformAdmin && p.Scope == "Admin")
                .Select(p => p.Code)
                .Except([Permissions.Admin.RolesManage, Permissions.Admin.StaffManage, Permissions.Admin.SettingsEdit])
                .ToArray()),
        new(Support, UserType.PlatformAdmin, Managed: false,
        [
            Permissions.Admin.DashboardView,
            Permissions.Admin.ShopsView,
            Permissions.Admin.ProfessionalsView,
            Permissions.Admin.ShopServicesView,
            Permissions.Admin.BookingsView,
            Permissions.Admin.BookingsIntervene,
            Permissions.Admin.CustomersView,
            Permissions.Admin.CustomersViewContact,
            Permissions.Admin.ReviewsView,
            Permissions.Admin.ReviewsFlag,
            Permissions.Admin.SubscriptionsView,
            Permissions.Admin.QrView,
            Permissions.Admin.WhatsAppView,
        ]),
        new(ShopOwner, UserType.ShopUser, Managed: true,
            Permissions.All.Where(p => p.UserType == UserType.ShopUser).Select(p => p.Code).ToArray()),
        new(ShopStaff, UserType.ShopUser, Managed: true,
        [
            Permissions.Shop.BookingsRead,
            Permissions.Shop.BookingsUpdateStatus,
            Permissions.Shop.BookingsCreateWalkIn,
            Permissions.Shop.BookingsResendNotification,
            Permissions.Shop.ScheduleRead,
        ]),
        new(Customer, UserType.Customer, Managed: true, []),
    ];

    public static RoleDefinition? Find(string name) => All.FirstOrDefault(r => r.Name == name);
}

public sealed record RoleDefinition(string Name, UserType UserType, bool Managed, IReadOnlyList<string> DefaultPermissions);
