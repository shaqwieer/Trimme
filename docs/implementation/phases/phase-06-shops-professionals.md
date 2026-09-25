# Phase 06 — Shops, locations & professionals

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
- An admin creates and edits a shop, including its full public profile, and sets its exact location with the TRIMME map-pin picker (search, current location, drag pin, confirm the resolved address and coordinates).
- An admin creates professionals in exactly one shop, with a masked WhatsApp number and a notification toggle. There is **no transfer action**.
- A shop edits only the profile fields the admin policy permits, plus its location if permitted.

## Prerequisites
- Phase 05 complete.
- D-007: the dev adapter is acceptable, and the production provider may still be open.

## In scope
- **Shop profile:**
  - Localized name and description, category/type, public phone (shop's own), cover and gallery images (upload validation; local storage adapter in dev), verification flag, amenities (optional), slug.
  - `EditablePolicy` (admin-controlled list of fields editable by the shop) (DV-S16).
- **`ShopLocation`:**
  - PostGIS `geography(Point,4326)` with a GiST index, plus address lines, district and city.
  - Resolved address, source (manual/geocoded/device), last confirmed at/by.
- **Map adapter:**
  - Web: a `MapView` and `LocationPicker` client component behind a provider interface; dev uses MapLibre GL.
  - API: an `IGeocoder` for reverse/forward geocoding via a dev adapter with caching and rate limiting. Keys only from environment variables.
  - The picker UI follows DV-A02 in TRIMME styling: search field, "استخدم موقعي الحالي", map with a draggable pin, resolved address card with coordinates, and confirm/save.
- **`Professional`** (Professionals module, `IShopOwned`):
  - Localized display name, slug, bio, avatar, specialties, active/disabled, and `ShopId` **set once at creation** with no setter afterwards (D-011).
  - `ProfessionalContact`: WhatsApp E.164 (encrypted per D-026), `NotificationsEnabled`, and verification state.
  - Optimistic concurrency.
- **Admin API:**
  - Shops CRUD, `PUT /admin/shops/{id}/location`, `PUT /admin/shops/{id}/editable-policy`.
  - Professionals `POST/GET/PUT /admin/professionals`, `POST .../disable|enable`.
  - `PUT /admin/professionals/{id}/whatsapp` (audited).
  - `POST /admin/professionals/{id}/whatsapp/reveal` (permission + reason + audit).
- **Shop API:**
  - `GET/PUT /shop/profile`, which enforces the editable policy server-side.
  - `PUT /shop/location`, if permitted.
  - `GET /shop/professionals`: read-only, with no contact data.
- **Public API (read):** `GET /public/shops/{slug}` basic profile and `GET /public/shops/{slug}/professionals`, with no phone numbers.
- **Web:**
  - Admin shops list, shop detail tabs (profile, location, users, professionals) (DV-A06).
  - Admin professionals list and create/edit form with masked number and reveal (DV-A07), nav label "الحلاقون وخدماتهم", and no transfer UI (DV-S01).
  - Shop settings (profile/location) with lock icons driven by the policy.
- **Seed:** separate professionals per shop with fake valid E.164 numbers (+9665xxxxxxxx test ranges), and locations in Riyadh districts.
- **Tests:** R-NEG-01, R-NEG-05, R-NEG-06 (professional part), R-PRO-01/02, R-SHP-02/03, and the E3 portion (create shop, pin, professional).

## Explicitly out of scope
Services and assignment (Phase 7), subscriptions (Phase 8), schedules (Phase 9).

## Checklist (100 points)
- [ ] 6.1 (5) Re-validate; refine checklist; confirm D-007 status.
- [ ] 6.2 (10) Shop profile, images upload validation, editable policy + tests.
- [ ] 6.3 (12) ShopLocation geography + GiST index + geocoder adapter + tests (R-SHP-02).
- [ ] 6.4 (14) LocationPicker UI (search, current location, drag pin, resolved address + coords, confirm) in RTL/LTR, keyboard-operable fallback (manual coordinates/address).
- [ ] 6.5 (14) Professional aggregate with immutable ShopId, contact encryption/masking, enable/disable + tests (R-NEG-01/05, R-PRO-01).
- [ ] 6.6 (8) Admin reveal (permission + reason + audit) + public DTO phone-absence tests (R-PRO-02).
- [ ] 6.7 (12) Admin web: shops list/detail tabs, professionals list/form, no transfer (grep gate + E2E).
- [ ] 6.8 (8) Shop web: settings profile/location with policy locks (R-SD-09).
- [ ] 6.9 (7) Seed extension; E3 partial E2E; `docs/domain-model.md` initial.
- [ ] 6.10 (10) Phase gates, isolation suite extended to professionals, control files, commit.

## Files/modules expected to change
`src/Modules/Shops/**`, `src/Modules/Professionals/**`, `src/Modules/Administration/**`, `apps/web/src/app/[locale]/admin/{shops,professionals}/**`, `apps/web/src/app/[locale]/shop/settings/**`, `apps/web/src/components/map/**`.

## Data model and migration impact
- Adds `shops.shop_locations` (geography + GiST), `shops` profile columns, `professionals.professionals` and `professionals.professional_contacts`.
- Migration `0004_ShopProfileLocationProfessionals`.

## API contracts and UI routes
As listed. Routes: `/admin/shops/[id]`, `/admin/professionals(/new|/[id])`, `/shop/settings(/location)`.

## Security, tenancy, privacy, RTL, a11y, responsive
- Professional numbers never appear in the public/shop DTOs.
- Numbers are masked in lists, and every reveal is audited.
- Geolocation is requested only on user action.
- The map has a keyboard/text alternative.

## Tests and verification commands
```
dotnet test
pnpm -C apps/web test
pnpm exec playwright test admin-shops admin-professionals
rg -i "transfer(Professional|Barber|Shop)|(professional|barber)\s*transfer|نقل حلاق|تنفيذ النقل" apps src   # must return no hits (the pattern is kept narrow so it does not match HTTP or other legitimate uses of "transfer")
```

## Acceptance criteria
- An admin can place and drag the pin and save.
- The stored point is correct and is used by a spatial query smoke test.
- Transfer is absent: the tests pass and the grep gate is clean.

## Rollback / recovery
Revert the commit, then reset the DB. Uploaded dev images are in `.data/uploads` and can be deleted.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- Production map provider (D-007).
- Next: Phase 07 — Services, categories & packages.
