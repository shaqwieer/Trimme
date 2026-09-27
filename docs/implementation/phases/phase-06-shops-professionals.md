# Phase 06 — Shops, locations & professionals

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
- An admin creates and edits a shop, including its full public profile, and sets its exact location with the TRIMME map-pin picker (search, current location, drag pin, confirm the resolved address and coordinates).
- An admin creates professionals in exactly one shop, with a masked WhatsApp number and a notification toggle. There is **no transfer action**.
- A shop edits only the profile fields the admin policy permits, plus its location if permitted.

## Prerequisites
- Phase 05 complete.
- D-007: the dev adapter is acceptable, and the production provider may still be open.

## In scope
- **Shop profile:**
  - Localized name and description, category/type, public phone (shop's own), logo, cover and gallery images (upload validation; **stored in PostgreSQL**, not on disk, at the user's request in Session 4 — D-064), verification flag, amenities (optional), slug.
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
Refined at 6.1: images are stored in PostgreSQL at the user's request (D-064); the location is an owned value on the shop row rather than a `shop_locations` table (D-065); public reads need a new read-only scope (D-066). D-007 is confirmed: OpenStreetMap behind adapters, with the production host still a configuration item.
- [x] 6.1 (5) Re-validate; refine checklist; confirm D-007 status. — Phase 05 decisive checks green at the start: architecture 61/61, tenancy integration 15/15.
- [x] 6.2 (10) Shop profile, image upload validation, editable policy + tests. — Profile (localized description, category, own phone, amenities, verification), logo/cover/gallery in the database with content sniffing and EXIF stripping, `EditableFields` enforced server-side (403 on a changed locked field), optimistic concurrency.
- [x] 6.3 (12) ShopLocation geography + GiST index + geocoder adapter + tests (R-SHP-02). — `geography(Point,4326)` + GiST on `shops.shops`; `IGeocoder` with Nominatim (cached, ≤ 1 request/s) and Fake adapters behind `/admin/geo/*` and `/shop/geo/*`; spatial `ST_Distance` smoke test.
- [x] 6.4 (14) LocationPicker UI (search, current location, drag pin, resolved address + coords, confirm) in RTL/LTR, keyboard-operable fallback (manual coordinates/address). — MapLibre 6.11.2 adapter behind `MapView`; typed-coordinate fallback when WebGL is missing; Vitest with a fake map; E2E search → drag → confirm; visual check at 1440 (ar) and 390 (en).
- [x] 6.5 (14) Professional aggregate with immutable ShopId, contact encryption/masking, enable/disable + tests (R-NEG-01/05, R-PRO-01). — Factory-only `ShopId` (EF also refuses: key member), encrypted number + unique lookup hash + mask, toggle rules, disable/enable, avatar, concurrency.
- [x] 6.6 (8) Admin reveal (permission + reason + audit) + public DTO phone-absence tests (R-PRO-02). — Support 403, reason required, audited with reason, `no-store`; public and shop lists scanned for the number; toggle-only change without reveal (`keepCurrentNumber`).
- [x] 6.7 (12) Admin web: shops list/detail tabs, professionals list/form, no transfer (grep gate + E2E). — `/admin/shops/[id]?tab=profile|location|users|professionals`, `/admin/professionals(/new|/[id])`, nav "الحلاقون وخدماتهم"; no transfer UI (E2E, Vitest copy scan, grep gate).
- [x] 6.8 (8) Shop web: settings profile/location with policy locks (R-SD-09). — `/shop/settings` (locked fields disabled with a shield note), `/shop/settings/location` (picker, or read-only when locked).
- [x] 6.9 (7) Seed extension; E3 partial E2E; `docs/domain-model.md` initial. — Demo shop profiles and Riyadh locations, five professionals with fake numbers; `shops-professionals.spec.ts`; domain model with ER diagram.
- [x] 6.10 (10) Phase gates, isolation suite extended to professionals, control files, commit. — See the evidence below.

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
dotnet build Trimme.slnx -c Release --no-incremental
dotnet test --project tests/Trimme.UnitTests -c Release
dotnet test --project tests/Trimme.ArchitectureTests -c Release
dotnet test --project tests/Trimme.IntegrationTests -c Release          # Shops/*, Professionals/*, Tenancy/*, Identity/*
pnpm lint && pnpm typecheck && pnpm format:check && pnpm test && pnpm openapi:check && pnpm build
E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 pnpm e2e     # flows/shops-professionals.spec.ts
# No-transfer gate (D-069: test files assert absence and are excluded):
rg -i "transfer(Professional|Barber|Shop)|(professional|barber)\s*transfer|نقل حلاق|تنفيذ النقل" apps src -g '!**/*.test.*' -g '!**/node_modules/**'   # must return no hits
```

## Acceptance criteria
- An admin can place and drag the pin and save.
- The stored point is correct and is used by a spatial query smoke test.
- Transfer is absent: the tests pass and the grep gate is clean.

## Rollback / recovery
Revert the commit, then reset the DB. Uploaded images live in the database (`media.media_files`, D-064), so a DB reset removes them; there is no upload directory.

## Completion evidence
Session 4, 2026-09-27.

**What was built**
- **Building blocks.**
  - Database media store (`media.media_files`, `IMediaStore`, `ImageSanitizer`: JPEG/PNG/WebP recognised from their bytes; EXIF/XMP/IPTC/text metadata stripped).
  - Anonymous immutable `GET /media/{id}`; `.AcceptsImageUpload()` for upload endpoints.
  - `IPublicDataScope` (read-only; architecture rule with probe).
  - `DbUpdateConcurrencyException` → 409 `resource.concurrency_conflict`; `geocode` rate limit; demo professionals in `DemoData`.
- **Shops module.**
  - Profile (description, category, own phone, amenities, verification), logo/cover/gallery, `EditableFields` policy.
  - `ShopLocation` owned value (`geography(Point,4326)` + GiST).
  - Admin endpoints (profile, location, policy, images), shop self-service (`/shop/profile`, images, `/shop/location`) through `ICurrentTenant`, and public `/public/shops/{slug}`.
  - `IGeocoder`: Nominatim and Fake adapters with `/admin/geo/*` and `/shop/geo/*`. Demo profiles and locations.
- **Professionals module.**
  - `Professional` (`IShopOwned`, `(shop_id, id)` key, factory-only `ShopId`) and `ProfessionalContact` (encrypted number, unique lookup hash, mask, toggle, verification).
  - Admin CRUD, disable/enable, WhatsApp set/keep/remove, audited reveal and avatar.
  - `/shop/professionals` (read-only), `/public/shops/{slug}/professionals`. Demo seeder.
- **Migration** `20260927101016_ShopProfileLocationProfessionals`. Existing shops get category `Barbershop` and the default edit policy.
- **Web.**
  - Map layer: `MapView` contract, MapLibre 6.11.2 adapter, `LocationPicker`.
  - Shared shop editors: profile with locks, images, location.
  - Admin: shop detail tabs (`?tab=`), edit-policy card, and `/admin/professionals` list, new and detail (WhatsApp card, reveal dialog, disable, photo).
  - Shop: `/shop/settings` and `/shop/settings/location` (`ShopFrame`). Messages ar/en.
- **Config and docs.**
  - Compose `TRIMME_GEOCODER` (default Fake) and `TRIMME_MAP_TILE_URL`; `.env.example` files; README.
  - `docs/domain-model.md` (new), architecture (media, public scope, maps), permissions matrix (Phase 06 endpoints), design deviations (DV-S01/S16/A02/A06/A07 applied).
  - D-064…D-069, TRACEABILITY.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration tests | PASS — 153 / 62 / 97 (Phase 05: 124 / 61 / 81) |
| New integration suites | `Shops/ShopProfileTests` (8) and `Professionals/ProfessionalTests` (8): profile + concurrency + audit; geography type, GiST index, axis order, `ST_Distance` ordering; locked fields (profile, location, staff, policy change); suspended shop guard; images (content sniffing, HTML/SVG rejected, too small, 413, EXIF stripped, ETag/304, immutable cache, replaced image deleted); cross-shop gallery 404; public page scope; fake geocoder; masked number stored encrypted; reveal (Support 403, reason, audit, `no-store`, keep-number toggle); public/shop DTOs without the number; immutable `ShopId`; one shop per professional (NOT NULL, composite FK, unique number); shop cannot create or change professionals; disable/avatar audit; no transfer operation in OpenAPI |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| `pnpm lint` / `typecheck` (web + E2E) / `format:check` / `openapi:check` / `test` / `build` | PASS — 179 web tests (9 new: `LocationPicker` with a fake map — search, drag, rounding, reverse geocoding, no-WebGL fallback, invalid coordinates; `ShopProfileEditor` locks; no transfer copy in either catalog) |
| Compose on the Phase 05 volume (upgrade path) | PASS — migration applied; seed gave the demo shops profiles and locations and added 5 professionals |
| `docker compose down -v`, `up --build`, `pnpm e2e` ×2 | PASS — 40/40 both runs, including `flows/shops-professionals.spec.ts`: admin profile + cover upload (`naturalWidth` 1600), search → drag pin → confirm (saved point moved, source Manual, inside Riyadh), professional with a masked number, audited reveal, no transfer/move control; owner sees locks, location read-only, API 403. Updated `tenancy.spec.ts` (invite on the Accounts tab) |
| No-transfer grep gate (D-069 form) | PASS — no hits in product code; three comments reworded to avoid the term (D-011) |
| gitleaks `dir` + `git` | PASS — no leaks (24 commits scanned) |
| Visual: `/ar/admin/shops/{id}?tab=location` and `/ar/admin/professionals/{id}` at 1440; `/ar/admin/professionals`, `/en/admin/shops/{id}?tab=location`, `/ar/shop/settings` at 390 | RTL mirrored (sidebar on the inline start, map controls and hint chip mirrored, coordinates LTR); 0 px horizontal overflow at 390 on all three phone captures |

**Findings fixed during the phase**
- The endpoint matrix sent JSON to multipart endpoints and got 415 from routing, so it now sends multipart (D-069).
- The test PostgreSQL ran out of connections; it now allows `max_connections=400` (D-069).
- The coordinates line failed axe contrast (`text-secondary` on `bg-subtle`); it now uses primary text.
- Forms keyed on `version` remounted after `router.refresh()` and lost their success message; the keys were removed.
- The WhatsApp toggle-only change needed no reveal, so `keepCurrentNumber` was added (D-067).

**Database and migrations:** `ShopProfileLocationProfessionals` (schemas `media`, `professionals`; shop profile, policy and location columns). Applied locally only: the Testcontainers databases and the local compose volume, both upgraded and recreated from empty.

**Smallest decisive re-verification for the next session:** `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Professionals" --filter-namespace "*Shops"`, then `pnpm e2e` against the stack (`flows/shops-professionals.spec.ts`).

## Remaining risks → next phase
- Production map tile and geocoder hosts (D-007/D-068): configuration, chosen in Phase 17/18.
- Images in PostgreSQL (D-064): watch database and backup size; add Nginx/CDN caching of `/api/v1/media/*` in Phase 17.
- The admin "new professional" shop list shows the first 100 shops; replace it with a searchable picker when shops outgrow it.
- Phase 07 adds professional–service assignment (R-NEG-06 remainder) on the `(shop_id, id)` professional key.
- Next: Phase 07 — Services, categories & packages.
