# TRIMME — Permissions matrix

> Source of truth: `src/Modules/Identity/Trimme.Modules.Identity/Domain/Permissions.cs` (catalogue) and `SystemRoles.cs` (seed roles). The `migrate` command synchronises both into `identity.permissions` / `identity.role_permissions` (D-051). Update this file with every catalogue change: `PermissionsMatrixDocTests` fails when a permission or role is missing here.

## Rules

- The API enforces every rule; the web app only hides navigation (spec §7, R-WEB-13).
- Every `/api/v1` endpoint is **default-deny**: it requires a signed-in user unless it explicitly allows anonymous access. The endpoint matrix test (`AuthorizationMatrixTests.Endpoint_WithoutPermission_Returns403`) fails if an endpoint is anonymous without being on the reviewed allow-list, or authenticated without a permission, a user type or a reviewed self-service entry.
- A role belongs to one user type (Customer, ShopUser, PlatformAdmin) and can hold only permissions of that type. `SuperAdmin.*` permissions are held only by the SuperAdmin role.
- Managed roles (SuperAdmin, ShopOwner, ShopStaff, Customer) are reset to these grants on every `migrate`. OperationsManager and Support receive these defaults once and are editable by `Admin.Roles.Manage` (UI in Phase 14).
- **Never** in the catalogue: moving/transferring a professional between shops (D-011), customer data export, shop access to customer contact details. `PermissionCatalogue_HasNoTransferPermission` guards this.
- Shop permissions are always tenant-scoped to the user's own shop: the tenant comes from the session and the shop's current status, every shop-owned row is filtered by it, and a suspended shop has no tenant (D-059, D-061).

## Platform admin roles

| Permission | SuperAdmin (مدير عام) | OperationsManager (مدير عمليات) | Support (دعم) |
|---|---|---|---|
| `Admin.Dashboard.View` | ✓ | ✓ | ✓ |
| `Admin.Shops.View` | ✓ | ✓ | ✓ |
| `Admin.Shops.Create` | ✓ | ✓ | — |
| `Admin.Shops.Edit` | ✓ | ✓ | — |
| `Admin.Shops.Suspend` | ✓ | ✓ | — |
| `Admin.Shops.ManageAccount` | ✓ | ✓ | — |
| `Admin.Professionals.View` | ✓ | ✓ | ✓ |
| `Admin.Professionals.Create` | ✓ | ✓ | — |
| `Admin.Professionals.Edit` | ✓ | ✓ | — |
| `Admin.Professionals.Disable` | ✓ | ✓ | — |
| `Admin.Professionals.AssignServices` | ✓ | ✓ | — |
| `Admin.Professionals.ManageWhatsApp` | ✓ | ✓ | — |
| `Admin.Professionals.RevealWhatsApp` | ✓ | ✓ | — |
| `Admin.ServiceCategories.Manage` | ✓ | ✓ | — |
| `Admin.ShopServices.View` | ✓ | ✓ | ✓ |
| `Admin.ShopServices.Moderate` | ✓ | ✓ | — |
| `Admin.ShopServices.SupportOverride` | ✓ | ✓ | — |
| `Admin.Packages.Manage` | ✓ | ✓ | — |
| `Admin.Bookings.View` | ✓ | ✓ | ✓ |
| `Admin.Bookings.Intervene` | ✓ | ✓ | ✓ |
| `Admin.Customers.View` | ✓ | ✓ | ✓ |
| `Admin.Customers.ViewContact` | ✓ | ✓ | ✓ |
| `Admin.Reviews.View` | ✓ | ✓ | ✓ |
| `Admin.Reviews.Moderate` | ✓ | ✓ | — |
| `Admin.Reviews.Flag` | ✓ | ✓ | ✓ |
| `Admin.Subscriptions.View` | ✓ | ✓ | ✓ |
| `Admin.Subscriptions.Assign` | ✓ | ✓ | — |
| `Admin.Subscriptions.Renew` | ✓ | ✓ | — |
| `Admin.Subscriptions.Suspend` | ✓ | ✓ | — |
| `Admin.Qr.View` | ✓ | ✓ | ✓ |
| `Admin.Qr.Manage` | ✓ | ✓ | — |
| `Admin.WhatsApp.View` | ✓ | ✓ | ✓ |
| `Admin.WhatsApp.Templates.Edit` | ✓ | ✓ | — |
| `Admin.WhatsApp.Templates.Activate` | ✓ | ✓ | — |
| `Admin.WhatsApp.TestSend` | ✓ | ✓ | — |
| `Admin.WhatsApp.Dispatches.Retry` | ✓ | ✓ | — |
| `Admin.Roles.View` | ✓ | ✓ | — |
| `Admin.Roles.Manage` | ✓ | — | — |
| `Admin.Staff.Manage` | ✓ | — | — |
| `Admin.Audit.View` | ✓ | ✓ | — |
| `Admin.Settings.View` | ✓ | ✓ | — |
| `Admin.Settings.Edit` | ✓ | — | — |
| `SuperAdmin.SubscriptionPlans.Manage` | ✓ | — | — |
| `SuperAdmin.Subscriptions.Override` | ✓ | — | — |

## Shop roles

| Permission | ShopOwner | ShopStaff (reception) |
|---|---|---|
| `Shop.Bookings.Read` | ✓ | ✓ |
| `Shop.Bookings.UpdateStatus` | ✓ | ✓ |
| `Shop.Bookings.CreateWalkIn` | ✓ | ✓ |
| `Shop.Bookings.ResendNotification` | ✓ | ✓ |
| `Shop.Schedule.Read` | ✓ | ✓ |
| `Shop.Schedule.Manage` | ✓ | — |
| `Shop.OnlineBooking.Pause` | ✓ | — |
| `Shop.Services.Manage` | ✓ | — |
| `Shop.Profile.Edit` | ✓ | — |
| `Shop.Location.Edit` | ✓ | — |
| `Shop.Subscription.Read` | ✓ | — |
| `Shop.Notifications.Manage` | ✓ | — |

## Customer

The `Customer` role holds no catalogue permission. Customers use self-service endpoints that act only on their own account (`/me`, sessions, profile completion); booking and review ownership rules arrive with those features (Phases 10–12).

## Endpoints (Phases 04–05)

| Endpoint | Access |
|---|---|
| `GET /api/v1/meta` | Anonymous |
| `GET /api/v1/auth/csrf` | Anonymous |
| `POST /api/v1/auth/otp/request` | Anonymous · rate limit `otp` (per IP) + 5 codes/hour/number |
| `POST /api/v1/auth/otp/verify` | Anonymous · rate limit `auth` |
| `POST /api/v1/auth/staff/sign-in` | Anonymous · rate limit `auth` · lockout 5 failures / 15 min |
| `POST /api/v1/auth/password/forgot` | Anonymous · rate limit `auth` · always 202 |
| `POST /api/v1/auth/password/reset` | Anonymous · rate limit `auth` |
| `POST /api/v1/auth/invitations/accept` | Anonymous · rate limit `auth` |
| `POST /api/v1/auth/refresh` | Anonymous (refresh cookie) · rate limit `auth` |
| `POST /api/v1/auth/sign-out` | Anonymous (ends the caller's own session) |
| `GET /api/v1/me` | Any signed-in user (self) |
| `GET /api/v1/auth/sessions` | Any signed-in user (own sessions) |
| `DELETE /api/v1/auth/sessions/{id}` | Any signed-in user (own sessions; others → 404) |
| `POST /api/v1/auth/sessions/revoke-all` | Any signed-in user (own sessions) |
| `POST /api/v1/auth/profile/complete` | User type Customer |
| `GET /api/v1/admin/permissions` | `Admin.Roles.View` |
| `GET /api/v1/admin/roles` | `Admin.Roles.View` |
| `POST /api/v1/admin/staff/invitations` | `Admin.Staff.Manage` |
| `POST /api/v1/admin/shops` | `Admin.Shops.Create` · audited |
| `GET /api/v1/admin/shops` | `Admin.Shops.View` · paged (max 100) |
| `GET /api/v1/admin/shops/{id}` | `Admin.Shops.View` · unknown id → 404 |
| `POST /api/v1/admin/shops/{id}/activate` | `Admin.Shops.Suspend` · audited |
| `POST /api/v1/admin/shops/{id}/suspend` | `Admin.Shops.Suspend` · audited · shop users lose tenant access on their next request |
| `POST /api/v1/admin/shops/{id}/users/invitations` | `Admin.Shops.ManageAccount` · ShopOwner/ShopStaff only · audited without the email |
| `GET /api/v1/shop/me` | User type ShopUser · the shop comes from the session, never the request |
| `GET /api/v1/dev/otp-inbox/latest` | Development/Testing only; not mapped otherwise; excluded from OpenAPI |

All unsafe methods additionally require the CSRF header (`X-CSRF-Token` = `trimme-csrf` cookie), anonymous ones included.
