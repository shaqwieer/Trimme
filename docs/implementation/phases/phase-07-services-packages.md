# Phase 07 — Services, categories & packages

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
- Each shop creates, edits, orders, activates/deactivates and archives its **own** services and packages, setting its own prices and durations.
- Admins manage categories, moderate or override shop services with an audit trail, and assign professionals to their own shop's services.

## Prerequisites
Phase 06 complete.

## In scope
- **`ServiceCategory`** (platform-owned):
  - Localized name, icon, order, active.
  - Admin CRUD.
- **`ShopService`** (`IShopOwned`):
  - Localized name/description, category, price (decimal, SAR), duration (5-minute multiples), active, archived, display order, booking rules (e.g. online bookable).
  - Moderation state (Visible/HiddenByAdmin with reason).
  - Concurrency version.
  - Physical delete is forbidden once referenced; archive only (R-SVC-02). The check is done via a reference count, and bookings arrive in Phase 10. Deletion is allowed only for never-used drafts.
- **`ServicePackage` + `ServicePackageItem`** (D-020):
  - Composite FKs to the same shop's services.
  - Explicit price and total duration.
  - Active/archived.
- **`ProfessionalService`:**
  - Composite FK `(ShopId, ProfessionalId)` + `(ShopId, ServiceId)`, so assignment is guaranteed same-shop.
  - Admin-only (R-NEG-06).
- **Shop API:**
  - `GET/POST /shop/services`, `GET/PUT /shop/services/{id}`.
  - `POST /shop/services/{id}/activate|deactivate|archive`.
  - `PUT /shop/services/order`.
  - The same operations for `/shop/packages`.
- **Admin API:**
  - `/admin/service-categories` CRUD.
  - `GET /admin/services`: platform-wide, filter by shop/category/status.
  - `POST /admin/services/{id}/moderate` (hide/unhide + reason, audited).
  - `PUT /admin/services/{id}/override`: explicit permission + reason, audited, with a before/after summary.
  - `PUT /admin/professionals/{id}/services`: assign from the same shop only.
- **Public read:** `GET /public/shops/{slug}/services` and `/packages`. Returns only active, visible, non-archived items.
- **Web:**
  - Shop `/shop/services` list with a toggle, drag/keyboard reorder, edit and archive; `/new` and `/[id]` form (DV-A01, DV-S03). Prices and durations are formatted per locale.
  - Admin `/admin/services`, `/admin/services/categories`, `/admin/packages`, and a professional's services tab showing that shop's prices (DV-S02, DV-S04).
- **Seed:** different services, prices and durations per shop, packages, and assignments.
- **Tests:**
  - R-SVC-01..05 and R-NEG-06.
  - Cross-shop isolation on every service/package mutation.
  - The service part of E2 (create/edit price and duration).

## Explicitly out of scope
Booking snapshots (Phase 10) and public shop page UI (Phase 11).

## Checklist (100 points)
- [ ] 7.1 (5) Re-validate; refine checklist.
- [ ] 7.2 (8) Categories entity/API/admin UI.
- [ ] 7.3 (15) ShopService aggregate, rules, concurrency, archive semantics + unit/integration tests.
- [ ] 7.4 (10) Packages + items (same-shop composite FKs) + tests.
- [ ] 7.5 (10) ProfessionalService assignment (admin only, same shop enforced by DB) + tests.
- [ ] 7.6 (10) Admin moderation + audited override + tests (R-SVC-04, R-TEN-08).
- [ ] 7.7 (14) Shop services & packages UI (list, reorder, form, archive guard).
- [ ] 7.8 (10) Admin services/categories/packages UIs + professional services tab.
- [ ] 7.9 (8) Cross-shop isolation tests for all service/package endpoints (R-SVC-03).
- [ ] 7.10 (10) Seed, E2E E2 (service part), public read endpoints, control files, commit.

## Files/modules expected to change
`src/Modules/Services/**`, `src/Modules/Professionals/**` (assignment), `src/Modules/Administration/**`, `apps/web/src/app/[locale]/shop/services/**`, `apps/web/src/app/[locale]/admin/{services,packages}/**`.

## Data model and migration impact
Adds `services.service_categories`, `services.shop_services`, `services.service_packages`, `services.service_package_items` and `professionals.professional_services`. Migration `0005_ServicesPackages`.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- Tenant filters and composite FKs apply to all tables.
- Admin override is gated by a permission and audited.
- Reordering works by keyboard.
- Localized fields are validated in both languages; Arabic is required and English is optional with a fallback (confirm in this phase).

## Tests and verification commands
```
dotnet test
pnpm exec playwright test shop-services admin-services
```

## Acceptance criteria
- A shop changes its price or duration, and only its own record changes.
- A foreign service ID returns 404 for every verb.
- An admin override leaves an audit entry.

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- The English-required policy for localized fields.
- Next: Phase 08 — Subscriptions & platform settings.
