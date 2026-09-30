namespace Trimme.Modules.Identity.Domain;

/// <summary>
/// The permission catalogue (spec §7, D-018, D-019, design analysis 03 §4.4). It is synchronised into
/// <c>identity.permissions</c> by the <c>migrate</c> command; roles reference it by code.
/// There is deliberately no permission to move a professional between shops (D-011), to export customer data,
/// or to give a shop access to customer contact details.
/// </summary>
public static class Permissions
{
    public static class Shop
    {
        public const string BookingsRead = "Shop.Bookings.Read";
        public const string BookingsUpdateStatus = "Shop.Bookings.UpdateStatus";
        public const string BookingsCreateWalkIn = "Shop.Bookings.CreateWalkIn";
        public const string BookingsResendNotification = "Shop.Bookings.ResendNotification";
        public const string ScheduleRead = "Shop.Schedule.Read";
        public const string ScheduleManage = "Shop.Schedule.Manage";
        public const string OnlineBookingPause = "Shop.OnlineBooking.Pause";
        public const string ServicesManage = "Shop.Services.Manage";
        public const string ProfileEdit = "Shop.Profile.Edit";
        public const string LocationEdit = "Shop.Location.Edit";
        public const string SubscriptionRead = "Shop.Subscription.Read";
        public const string NotificationsManage = "Shop.Notifications.Manage";
    }

    public static class Admin
    {
        public const string DashboardView = "Admin.Dashboard.View";
        public const string ShopsView = "Admin.Shops.View";
        public const string ShopsCreate = "Admin.Shops.Create";
        public const string ShopsEdit = "Admin.Shops.Edit";
        public const string ShopsSuspend = "Admin.Shops.Suspend";
        public const string ShopsManageAccount = "Admin.Shops.ManageAccount";
        public const string ProfessionalsView = "Admin.Professionals.View";
        public const string ProfessionalsCreate = "Admin.Professionals.Create";
        public const string ProfessionalsEdit = "Admin.Professionals.Edit";
        public const string ProfessionalsDisable = "Admin.Professionals.Disable";
        public const string ProfessionalsAssignServices = "Admin.Professionals.AssignServices";
        public const string ProfessionalsManageWhatsApp = "Admin.Professionals.ManageWhatsApp";
        public const string ProfessionalsRevealWhatsApp = "Admin.Professionals.RevealWhatsApp";
        public const string ServiceCategoriesManage = "Admin.ServiceCategories.Manage";
        public const string ShopServicesView = "Admin.ShopServices.View";
        public const string ShopServicesModerate = "Admin.ShopServices.Moderate";
        public const string ShopServicesSupportOverride = "Admin.ShopServices.SupportOverride";
        public const string PackagesManage = "Admin.Packages.Manage";
        public const string BookingsView = "Admin.Bookings.View";
        public const string BookingsIntervene = "Admin.Bookings.Intervene";
        public const string CustomersView = "Admin.Customers.View";
        public const string CustomersViewContact = "Admin.Customers.ViewContact";
        public const string ReviewsView = "Admin.Reviews.View";
        public const string ReviewsModerate = "Admin.Reviews.Moderate";
        public const string ReviewsFlag = "Admin.Reviews.Flag";
        public const string SubscriptionsView = "Admin.Subscriptions.View";
        public const string SubscriptionsAssign = "Admin.Subscriptions.Assign";
        public const string SubscriptionsRenew = "Admin.Subscriptions.Renew";
        public const string SubscriptionsSuspend = "Admin.Subscriptions.Suspend";
        public const string QrView = "Admin.Qr.View";
        public const string QrManage = "Admin.Qr.Manage";
        public const string WhatsAppView = "Admin.WhatsApp.View";
        public const string WhatsAppTemplatesEdit = "Admin.WhatsApp.Templates.Edit";
        public const string WhatsAppTemplatesActivate = "Admin.WhatsApp.Templates.Activate";
        public const string WhatsAppTestSend = "Admin.WhatsApp.TestSend";
        public const string WhatsAppDispatchesRetry = "Admin.WhatsApp.Dispatches.Retry";
        public const string JobsView = "Admin.Jobs.View";
        public const string RolesView = "Admin.Roles.View";
        public const string RolesManage = "Admin.Roles.Manage";
        public const string StaffManage = "Admin.Staff.Manage";
        public const string AuditView = "Admin.Audit.View";
        public const string SettingsView = "Admin.Settings.View";
        public const string SettingsEdit = "Admin.Settings.Edit";
    }

    public static class SuperAdmin
    {
        public const string SubscriptionPlansManage = "SuperAdmin.SubscriptionPlans.Manage";
        public const string SubscriptionsOverride = "SuperAdmin.Subscriptions.Override";
    }

    /// <summary>Every permission with the user type allowed to hold it.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } = Build();

    private static PermissionDefinition[] Build()
    {
        static IEnumerable<string> Codes(Type holder) => holder
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        return
        [
            .. Codes(typeof(Shop)).Select(c => new PermissionDefinition(c, UserType.ShopUser)),
            .. Codes(typeof(Admin)).Select(c => new PermissionDefinition(c, UserType.PlatformAdmin)),
            .. Codes(typeof(SuperAdmin)).Select(c => new PermissionDefinition(c, UserType.PlatformAdmin)),
        ];
    }
}

/// <param name="Code">Stable code, e.g. <c>Admin.Shops.View</c>. Part of the API contract.</param>
/// <param name="UserType">Only roles of this user type may be granted the permission.</param>
public sealed record PermissionDefinition(string Code, UserType UserType)
{
    /// <summary>First segment of the code (<c>Shop</c>, <c>Admin</c>, <c>SuperAdmin</c>).</summary>
    public string Scope => Code[..Code.IndexOf('.', StringComparison.Ordinal)];
}
