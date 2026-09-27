# TRIMME — Domain model

The model as built so far (Phases 04–08), with the invariants each part enforces. Later phases extend this file:
schedules (09), bookings (10), reviews, notifications and
QR (11–16). The spec's target model is in spec §8. Decisions are referenced as D-xxx (`docs/implementation/DECISIONS.md`).

One PostgreSQL database with one schema per module. Identifiers are UUIDv7 wrapped in typed ids. Instants are UTC
`timestamptz`, and optimistic concurrency uses PostgreSQL `xmin`.

```mermaid
erDiagram
    SHOP ||--o{ USER : "shop users (users.shop_id, fixed)"
    SHOP ||--o{ INVITATION : "shop invitations"
    SHOP ||--o{ PROFESSIONAL : "exactly one shop, fixed"
    PROFESSIONAL ||--|| PROFESSIONAL_CONTACT : "(shop_id, professional_id)"
    SHOP }o--o| MEDIA_FILE : "logo, cover"
    SHOP ||..o{ MEDIA_FILE : "gallery (uuid[])"
    PROFESSIONAL }o--o| MEDIA_FILE : "avatar"
    USER ||--o{ USER_SESSION : "sessions"
    USER_SESSION ||--o{ REFRESH_TOKEN : "rotating family"
    SHOP ||--o{ SHOP_SERVICE : "own prices and durations"
    SERVICE_CATEGORY |o--o{ SHOP_SERVICE : "platform category"
    SHOP ||--o{ SERVICE_PACKAGE : ""
    SERVICE_PACKAGE ||--|{ SERVICE_PACKAGE_ITEM : "2-10, (shop_id, package_id)"
    SHOP_SERVICE ||--o{ SERVICE_PACKAGE_ITEM : "(shop_id, service_id)"
    PROFESSIONAL ||--o{ PROFESSIONAL_SERVICE : "(shop_id, professional_id)"
    SHOP_SERVICE ||--o{ PROFESSIONAL_SERVICE : "(shop_id, service_id)"
    SUBSCRIPTION_PLAN ||--o{ PLAN_PRICE : "append-only versions"
    SHOP ||--o| SHOP_SUBSCRIPTION : "one per shop"
    SHOP_SUBSCRIPTION ||--|{ SUBSCRIPTION_PERIOD : "(shop_id, subscription_id)"
    SHOP_SUBSCRIPTION ||--o{ SUBSCRIPTION_OVERRIDE : "(shop_id, subscription_id)"
    PLAN_PRICE ||--o{ SUBSCRIPTION_PERIOD : "price snapshot"
    SHOP ||--o| SUBSCRIPTION_COVERAGE : "read model"
    ROLE ||--o{ ROLE_PERMISSION : grants
    PERMISSION ||--o{ ROLE_PERMISSION : ""

    SHOP {
        uuid id PK
        string slug UK
        string name_ar
        string name_en
        string status "Draft|Active|Suspended"
        string category "Barbershop|Salon|Unisex"
        string public_phone "shop's own number"
        text_array amenities
        bool is_verified "admin only"
        uuid logo_media_id FK
        uuid cover_media_id FK
        uuid_array gallery_media_ids "ordered, max 12"
        geography location "Point 4326, GiST"
        text_array editable_fields "admin policy"
        xid xmin "concurrency"
    }
    PROFESSIONAL {
        uuid id PK
        uuid shop_id FK "set once"
        string slug "unique per shop"
        string name_ar
        string name_en
        string status "Active|Disabled"
        uuid avatar_media_id FK
    }
    PROFESSIONAL_CONTACT {
        uuid professional_id PK
        uuid shop_id FK
        string protected_whatsapp "encrypted E.164"
        string whatsapp_lookup_hash UK "HMAC"
        string whatsapp_masked
        bool notifications_enabled
        string whatsapp_verification
    }
    MEDIA_FILE {
        uuid id PK
        string purpose
        string content_type "jpeg|png|webp"
        int width
        int height
        bytea content "metadata stripped"
        char64 sha256 "ETag"
    }
```

## Shops (`shops`) — the tenant root

**Shop** (D-059, D-061, D-065) is the tenant of every shop-owned row.
- **Lifecycle.** Created by an admin as Draft, then Active; an admin can Suspend it and activate it again. A suspended shop's users have no tenant, so its data is unreachable at once and nothing is deleted.
- **Profile.** Localized name and description, category, the shop's own public phone (E.164), amenities, logo, cover and a gallery of up to 12 images.
  - `IsVerified` is admin-only.
  - `EditableFields` is the admin policy that opens profile areas to the shop. The server rejects a changed locked field with 403.
- **Location.** An owned value stored on the shop row: `geography(Point,4326)` (X = longitude, Y = latitude, 6 decimal places), address line, district, city, formatted address, source (Manual, Geocoded, Device), and confirmed at/by. There is a GiST index for nearby search (Phase 11).
- **Visibility.** The shops table is a public directory, but a shop user reads only their own shop row. Public pages publish active shops only (D-066).

## Professionals (`professionals`)

**Professional** (D-011, D-067) is a barber or stylist, created by a platform admin in exactly one shop.
- `ShopId` is set by the factory and never changes. No update contract carries it, EF refuses to modify it (it is part of the `(shop_id, id)` key), and the tenant rules reject any `ShopId` change.
- Its slug is unique within the shop. Status is Active or Disabled; disabled professionals leave public pages and are not offered for new bookings (Phase 10).

**ProfessionalContact** holds the WhatsApp number.
- The number is stored only encrypted (Data Protection purpose `trimme.professional-whatsapp`), with an HMAC lookup hash that is unique platform-wide (one person, one professional) and a mask for lists (`+966 5•• ••• •67`).
- Notifications can be on only while a number is set; `CanReceiveNotifications` means both are true.
- Changing the number resets the verification state.
- Only admins with `RevealWhatsApp` read the full number (with a reason, audited), plus the notification worker in Phase 15. Shops and customers never receive it.

## Services (`services`)

**ServiceCategory** (D-074) is platform-owned: Arabic and English names, icon, order, active flag. It carries no price
or duration. It is deactivated, never deleted.

**ShopService** (D-070, D-071) is a service of one shop (`IShopOwned`, `(shop_id, id)` key).
- The shop's own names (Arabic required, English optional), description, category, price (`numeric(10,2)`, 0–100,000
  SAR, currency stored) and duration (5-minute steps, 5–480; database CHECK constraints).
- Online-bookable flag, on/off, display order, optimistic concurrency.
- Archive is final. Delete is allowed only when no `IShopServiceUsage` reports a use (packages now, bookings from
  Phase 10).
- Platform moderation (Visible/Hidden with a reason) and an audited support override never change who owns it.
- Publicly available = active, not archived, visible.

**ServicePackage** (D-020, D-072) is shop-owned: 2–10 distinct services of the same shop (items keyed
`(package_id, service_id)`, composite FKs to the package and the service), with its own price and total duration.
Items keep their rows when reordered, and an items-only edit still bumps the package version. A package is published
only while it and every item are available.

**ProfessionalServiceAssignment** (D-073) links a professional to a service of the same shop, with both composite FKs
including `shop_id`. Only admins assign. The FK to the Professionals module names the entity type as a string, so
there is no project reference between the modules.

## Subscriptions (`subscriptions`)

**SubscriptionPlan** (D-079) is managed by SuperAdmin only.
- Localized name and description, up to 12 bilingual feature lines (JSON), billing unit Month (1–36) or Day (1–1095), optional limits and trial/grace days (stored only in v1, D-077), available-to-new-shops, display order.
- Status: Draft → Published ⇄ Inactive → Archived (final). Publishing needs a price.

**PlanPrice** versions are append-only.
- Amount (`numeric(12,2)`, 0–1,000,000), the platform currency, and an effective date that is today or later.
- Unique `(plan_id, effective_from)` and `(plan_id, version_number)`. The version in force on a date is the latest that started on or before it.

**ShopSubscription** (`IShopOwned`, unique `shop_id`, `xmin`) is recorded manually; there is no payment in v1.
- It stores `StartDate`/`EndDate` (platform calendar, D-077), the latest plan, and the suspension flag and reason.
- **SubscriptionPeriod** (assignment or renewal) keeps the plan name, the price version id, the amount and the currency as recorded, plus notes and who recorded it. Periods never overlap, and never leave a future gap.
- **SubscriptionOverride** (SuperAdmin) keeps the previous and new amount/end with the reason.
- Status (computed): None, Suspended, Expired, ExpiringSoon (≤ threshold days left, the end day counts), Active.

**SubscriptionCoverage** (D-078) is a platform read model (shop id, end date, suspended) with no prices. It is written with every subscription change and read by `IShopBookability` without a tenant scope.

## Administration settings

**PlatformSettings** (D-076) is one typed row: booking policy, reminder offset, expiring-soon threshold, enforcement (D-014), hide paused shops (D-013), region (fixed in v1) and map defaults. It is version-checked and audited with the changed fields; `migrate` inserts the defaults only when the row is missing.

## Media (`media`, building blocks)

**StoredMedia** (D-064) is an image kept in PostgreSQL.
- Only JPEG, PNG or WebP, recognised from its bytes. Size limits apply, and EXIF/GPS, XMP, IPTC and PNG text are removed.
- It has no shop id: its only owner is the aggregate that references it, and uploads create the image and the reference together. Images never change in place (new upload, new id).

## Identity (`identity`)

Users of three types — Customer, ShopUser, PlatformAdmin (D-050) — sit on one table.
- **Customers** are passwordless. Their mobile is encrypted, with a lookup hash.
- **Staff** use email and password.
- **Shop users** carry `shop_id`, which is set once (`ITenantMember`).
- **Roles and permissions.** Roles map to data-driven permissions (`role_permissions`). The permission catalogue is code, synchronised by `migrate`.
- **Supporting records:** sessions with rotating refresh-token families (D-052), OTP challenges (D-054) and invitations (D-055).

## Administration (`administration`)

**AuditEntry** (D-063) is append-only.
- It records the actor, action, entity, shop, a PII-free summary, the reason and the correlation id.
- It is written in the same unit of work as the change.
- Phase 06 actions:
  - `shop.profile_updated`, `shop.location_set`, `shop.editable_policy_set`, `shop.image_changed`;
  - `professional.created`, `.updated`, `.enabled`, `.disabled`, `.avatar_changed`;
  - `professional.whatsapp_changed`, `.whatsapp_revealed`.
- Phase 07 and 08 actions add `service.*`/`package.*` moderation and override, then `plan.created|updated|published|deactivated|archived|reordered|price_added`, `subscription.assigned|renewed|overridden|suspended|reinstated` and `platform_settings.updated`.
