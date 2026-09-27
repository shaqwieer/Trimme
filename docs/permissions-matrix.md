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

## Endpoints (Phases 04–08)

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
| `GET /api/v1/media/{id}` | Anonymous · a stored image (JPEG/PNG/WebP), public and immutable (D-064) |
| `GET /api/v1/public/shops/{slug}` | Anonymous · active shops only · read inside the public data scope (D-066) |
| `GET /api/v1/public/shops/{slug}/professionals` | Anonymous · active professionals of an active shop · no contact data |
| `PUT /api/v1/admin/shops/{id}` | `Admin.Shops.Edit` · profile + verification · optimistic concurrency (409) · audited |
| `PUT /api/v1/admin/shops/{id}/location` | `Admin.Shops.Edit` · audited |
| `PUT /api/v1/admin/shops/{id}/editable-policy` | `Admin.Shops.Edit` · which fields the shop edits · audited |
| `PUT/DELETE /api/v1/admin/shops/{id}/logo`, `.../cover`; `POST .../gallery`; `DELETE .../gallery/{mediaId}` | `Admin.Shops.Edit` · multipart, ≤ 5 MB · audited |
| `GET /api/v1/admin/geo/search`, `GET /api/v1/admin/geo/reverse` | `Admin.Shops.Edit` · rate limit `geocode` |
| `GET /api/v1/shop/profile` | User type ShopUser · own shop from `ICurrentTenant`; 404 while suspended |
| `PUT /api/v1/shop/profile` | `Shop.Profile.Edit` · a changed locked field → 403 `shop.profile_field_locked` |
| `PUT/DELETE /api/v1/shop/profile/logo`, `.../cover`; `POST .../gallery`; `DELETE .../gallery/{mediaId}` | `Shop.Profile.Edit` · only if the policy opens the field · another shop's image → 404 |
| `PUT /api/v1/shop/location` | `Shop.Location.Edit` · only if the policy opens Location |
| `GET /api/v1/shop/geo/search`, `GET /api/v1/shop/geo/reverse` | `Shop.Location.Edit` · rate limit `geocode` |
| `GET /api/v1/shop/professionals` | User type ShopUser · own professionals, read-only, no contact data |
| `POST /api/v1/admin/professionals` | `Admin.Professionals.Create` · the one shop is chosen here · audited |
| `GET /api/v1/admin/professionals`, `GET /api/v1/admin/professionals/{id}` | `Admin.Professionals.View` · WhatsApp masked |
| `PUT /api/v1/admin/professionals/{id}` | `Admin.Professionals.Edit` · no shop field · optimistic concurrency · audited |
| `PUT/DELETE /api/v1/admin/professionals/{id}/avatar` | `Admin.Professionals.Edit` · audited |
| `POST /api/v1/admin/professionals/{id}/disable`, `.../enable` | `Admin.Professionals.Disable` · audited with optional reason |
| `PUT /api/v1/admin/professionals/{id}/whatsapp` | `Admin.Professionals.ManageWhatsApp` · number never logged or audited |
| `POST /api/v1/admin/professionals/{id}/whatsapp/reveal` | `Admin.Professionals.RevealWhatsApp` · reason required · audited · `no-store` |

| `GET /api/v1/public/service-categories` | Anonymous · active categories |
| `GET /api/v1/public/shops/{slug}/services`, `.../packages` | Anonymous · active shop; active, visible, non-archived items; packages only when every item is available |
| `GET /api/v1/shop/services`, `GET /api/v1/shop/services/{id}`, `GET /api/v1/shop/packages`, `GET /api/v1/shop/packages/{id}` | User type ShopUser (owner and staff) · own shop only; 404 while suspended |
| `POST/PUT /api/v1/shop/services(/{id})`, `POST .../{id}/activate|deactivate|archive`, `DELETE .../{id}`, `PUT .../order` | `Shop.Services.Manage` · same for `/shop/packages` (no delete) · another shop's id → 404 |
| `GET /api/v1/admin/service-categories` | `Admin.ShopServices.View` |
| `POST /api/v1/admin/service-categories`, `PUT .../{id}`, `POST .../{id}/activate|deactivate` | `Admin.ServiceCategories.Manage` · audited |
| `GET /api/v1/admin/services`, `GET /api/v1/admin/services/{id}`, `GET /api/v1/admin/packages` | `Admin.ShopServices.View` · paged |
| `POST /api/v1/admin/services/{id}/moderation`, `POST /api/v1/admin/packages/{id}/moderation` | `Admin.ShopServices.Moderate` · hide needs a reason · audited |
| `PUT /api/v1/admin/services/{id}/override` | `Admin.ShopServices.SupportOverride` · reason required · audited before → after |
| `GET /api/v1/admin/professionals/{id}/services` | `Admin.Professionals.View` |
| `PUT /api/v1/admin/professionals/{id}/services` | `Admin.Professionals.AssignServices` · the professional's own shop's services only · audited |
| `GET /api/v1/admin/subscription-plans`, `GET .../{id}`, `GET .../{id}/prices` | `Admin.Subscriptions.View` |
| `POST /api/v1/admin/subscription-plans`, `PUT .../{id}`, `POST .../{id}/publish|deactivate|archive`, `PUT .../order`, `POST .../{id}/prices` | `SuperAdmin.SubscriptionPlans.Manage` · prices are append-only versions · audited (D-079) |
| `GET /api/v1/admin/subscriptions`, `GET /api/v1/admin/shops/{id}/subscription` | `Admin.Subscriptions.View` · counts over every shop |
| `POST /api/v1/admin/shops/{id}/subscription/assign` | `Admin.Subscriptions.Assign` · published plan open to new shops · audited |
| `POST /api/v1/admin/shops/{id}/subscription/renew` | `Admin.Subscriptions.Renew` · version-checked · audited |
| `POST /api/v1/admin/shops/{id}/subscription/suspend|reinstate` | `Admin.Subscriptions.Suspend` · suspend needs a reason · audited |
| `POST /api/v1/admin/shops/{id}/subscription/override` | `SuperAdmin.Subscriptions.Override` · reason required · previous values kept · audited |
| `GET /api/v1/shop/subscription` | `Shop.Subscription.Read` · own shop only (tenant from claims) · no override reasons |
| `GET /api/v1/admin/settings` / `PUT /api/v1/admin/settings` | `Admin.Settings.View` / `Admin.Settings.Edit` · version-checked · audited with the changed fields (D-076) |

There is no endpoint that changes a professional's shop. Shops never assign services to professionals.

All unsafe methods additionally require the CSRF header (`X-CSRF-Token` = `trimme-csrf` cookie), anonymous ones included.
