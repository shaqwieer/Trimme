namespace Trimme.Modules.Identity.Domain;

/// <summary>
/// Seed roles (D-018, D-019, D-051). <see cref="RoleDefinition.Managed"/> roles have their grants reset to these
/// defaults on every <c>migrate</c>; the others receive the defaults once, when first created, and are then editable
/// by an admin with <c>Admin.Roles.Manage</c> (Phase 14). The shop desk role (ShopStaff) is gone (D-145): each barber
/// signs in as their own desk, and the migration <c>RemoveShopDesk</c> disabled the desk accounts.
/// </summary>
public static class SystemRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string OperationsManager = "OperationsManager";
    public const string Support = "Support";
    public const string ShopOwner = "ShopOwner";

    /// <summary>A barber's own login (D-145): their bookings, walk-ins, breaks and figures only.</summary>
    public const string ShopBarber = "ShopBarber";
    public const string Customer = "Customer";

    /// <summary>
    /// The shop permissions only barbers hold (D-147: «صاحب المحل للمتابعة بس»). Declared before <see cref="All"/>, which
    /// reads it: static initializers run in textual order.
    /// </summary>
    public static IReadOnlyList<string> OperatedByBarbers { get; } =
    [
        Permissions.Shop.BookingsCreateWalkIn,
        Permissions.Shop.BookingsUpdateStatus,
        Permissions.Shop.BookingsResendNotification,
        Permissions.Shop.BookingsReschedule,
        Permissions.Shop.BookingsChangeProfessional,
        Permissions.Shop.BreaksManage,
    ];

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
        // The owner watches and manages the shop; the barbers operate the bookings (D-146, D-147): no walk-ins, no status
        // changes or notes, no change of time or barber, no barber's breaks. D-155 (the client's 1-1): pausing online booking
        // is the owner's (and the admin's), no longer the barbers'.
        new(ShopOwner, UserType.ShopUser, Managed: true,
            Permissions.All
                .Where(p => p.UserType == UserType.ShopUser)
                .Select(p => p.Code)
                .Except(OperatedByBarbers)
                .ToArray()),
        new(ShopBarber, UserType.ShopUser, Managed: true,
        [
            // Scoped to the barber's own professional by the handlers (D-145), with the reviewed exceptions of D-147: the
            // shop's pending and red bookings, and walk-in or phone bookings made for a free colleague.
            Permissions.Shop.BookingsRead,
            Permissions.Shop.BookingsUpdateStatus,
            Permissions.Shop.BookingsCreateWalkIn,
            Permissions.Shop.BookingsReschedule,
            Permissions.Shop.BookingsChangeProfessional,
            Permissions.Shop.BreaksManage,
        ]),
        new(Customer, UserType.Customer, Managed: true, []),
    ];

    public static RoleDefinition? Find(string name) => All.FirstOrDefault(r => r.Name == name);
}

public sealed record RoleDefinition(string Name, UserType UserType, bool Managed, IReadOnlyList<string> DefaultPermissions);
