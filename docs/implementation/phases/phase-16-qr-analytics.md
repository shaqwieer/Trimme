# Phase 16 — QR codes & attribution analytics

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
Admins create unique QR codes for shops and professionals. Shop owners can download their own. Scanning one opens the designed QR landing page. Visits are counted without invasive tracking, and bookings made from them are credited to the code. Admins see the resulting QR analytics.

## Prerequisites
Phase 15 complete.

## In scope
- **`QrCodeLink`:** a unique short code, the target (shop or professional), a label, an active flag, and the creator.
- **`QrVisit`:** the code, the time, a coarse device class, the language, and a keyed hash of the day and the IP address. Raw IP addresses are not stored, and there is no third-party tracking.
- **Attribution:** a first-party, HttpOnly attribution cookie holding the visit id, with a configurable 7-day window. It credits a booking on create (R-QR-02).
- **API:**
  - Public: `GET /public/qr/{code}` and `POST /public/qr/{code}/visits`, both rate-limited under `qr`.
  - Admin: `/admin/qr/codes`, create, deactivate/activate, image, and `/admin/qr/analytics`.
  - Shop: `/shop/qr/codes` (read-only, `Shop.Qr.View`, open question 8 → D-114).
- **Generation:** server-side PNG, SVG and PDF, plus an A5 poster as a browser print page (c-qr print card).
- **Pages:** `/q/[code]` (c-qr, shop and barber variants), `/admin/qr`, `/admin/qr/[id]/poster`, `/shop/qr`, `/shop/qr/[id]/poster`.
- **Seed:** QR codes, 27 days of scans, and three credited demo bookings.
- **Tests:** unit, integration, web and E2E (scan → book → credited).

## Explicitly out of scope
- Marketing campaigns beyond labels.
- A per-shop QR tab on the admin shop page. The QR page lists every shop's codes.
- A QR action on the public shop cover. The design's icon has no defined behaviour.

## Checklist (100 points)
- [x] 16.1 (5) Re-validate; refine the checklist.
  - Phase 15 still holds: CI green (69/69 E2E on GitHub Actions after the pushed 390 px fix) and `NotificationsTests` 12/12 locally.
  - Refinements, all in D-114:
    - a code → shop route table, so an anonymous scan finds its shop without weakening the public scope;
    - visits recorded from the browser, so the API sets the HttpOnly cookie;
    - the booking keeps `qr_link_id` and `qr_visit_id`, not a new channel;
    - the A5 poster is a print page;
    - the migration keeps the repository's timestamped name.
- [x] 16.2 (12) QR entities + unique codes + migration + tests (R-QR-01).
  - `QrCodeLink` (shop-owned, composite key to a same-shop professional), `QrCodeRoute`, `QrVisit`; migration `20260930111559_Qr`.
  - `QrCodeLinkId` joins `ProfessionalId` in the shared domain, so the booking's composite FK has matching key types.
  - I `Codes_AreUniqueAndResolveToTheirTarget_…`: 22 distinct codes; the database refuses a duplicate; another shop's barber is refused.
  - U `NewCodes_…` (2 000 distinct codes) and `Codes_AreMatchedLowercase_…`.
- [x] 16.3 (14) Visit tracking (privacy-preserving) + attribution cookie + booking attribution + tests (R-QR-02).
  - I `Scans_KeepNoIpAddress_ReloadsCountOnce_AndCreditOnlySameShopBookingsWithinTheWindow` checks:
    - `qr_visits` has no IP column, and the hash is 32 characters;
    - the cookie is `HttpOnly; Secure; SameSite=Lax; Path=/api/v1`;
    - a reload reuses the visit;
    - a same-shop booking is credited, another shop's is not, and a walk-in by a staff browser carrying the cookie is not;
    - the replay with a changed cookie returns the same booking;
    - nothing is credited after 8 days on a fake clock.
  - U `TheDeviceClass_…`.
- [x] 16.4 (12) QR image/PDF/A5 generation.
  - QRCoder 1.6.0 (MIT) provides the matrix. PNG uses its managed writer. SVG and a 70 mm vector PDF are drawn here.
  - U `TheMatrix_HasAQuietZoneAndThreeFinderPatterns`, `TheSvgAndPdf_DrawExactlyTheDarkModules` (xref offsets checked).
  - I PNG signature, SVG root, `%PDF-1.4 … %%EOF`, and 400 for an unknown format.
  - The A5 poster (`@page A5`, RTL) is checked visually and in E2E.
- [x] 16.5 (16) `/q/[code]` landing for the shop and professional variants.
  - The c-qr frame: badge, identity with live opening, intro callout, the earliest times across barbers linking into the wizard, services and packages, and the sticky «احجز الآن» with the caption.
  - The barber variant uses the avatar and the barber's offers.
  - The page is noindex, with the canonical set to the shop or barber page.
  - The locale-less printed URL redirects by `Accept-Language`. This needed D-115, the proxy matcher fix.
- [x] 16.6 (16) Admin QR management + analytics (R-AD-09, DV-A14).
  - `/admin/qr`: period tabs (7/30/90 days), KPIs (scans, credited bookings, conversion with the window), scans per shop, the print notes and the privacy line.
  - The codes table has a status filter, files (PNG/SVG/PDF), the A5 poster and switch off/on (confirmed, version-checked, audited).
  - The create dialog takes a shop or one of its active barbers, and a label.
  - Admin booking views label the source «رمز QR».
- [x] 16.7 (5) Shop QR download.
  - `Shop.Qr.View` (owner): `/shop/qr` with the shop's codes, its 30-day figures, the files and the poster.
  - Another shop's code is 404. Staff have no access.
- [x] 16.8 (8) Seed + E2E scan → book → attribution.
  - Seed: 6 codes (1 switched off), deterministic daily scans, three credited demo bookings.
  - I `DemoSeed_CodesResolve_AndTheAnalyticsMatchTheSeededScansAndBookings`: the analytics match the formula exactly.
  - E `qr.spec.ts` (3 tests): the admin creates a code; a visitor signs up, scans the locale-less URL (HttpOnly cookie), books through the wizard; the admin booking shows «رمز QR»; the code has 1 scan and 1 booking; the shop sees it; cancel; switch off → 404.
- [x] 16.9 (12) Axe, viewports, gates, control files, commit. Evidence below.

## Files/modules changed
- **Backend:**
  - `src/Modules/QrAnalytics/**`: domain, persistence, public, admin and shop use cases, images, endpoints, seeder;
  - `src/BuildingBlocks` (QR contracts, `QrCodeLinkId`, the `trimme.qr-visitor` purpose, `DemoQr`);
  - `src/Modules/Bookings` (attribution fields, cookie read, `IQrBookingReader`, `ViaQr`, credited seed bookings);
  - `Permissions` (`Shop.Qr.View`);
  - `ModuleCatalog` (QR before Bookings).
- **Web:**
  - pages `q/[code]`, `admin/qr/**` and `shop/qr/**`;
  - `components/qr/**`, `lib/qr.ts`, `bookingSource`;
  - the shop navigation entry;
  - messages (ar/en);
  - `proxy.ts` (D-115).
- **Tests:**
  - U `QrDomainTests`;
  - I `QrTests`, plus the endpoint-matrix allow-list and the fixture's connect timeout;
  - W `qr.test.tsx`;
  - E `qr.spec.ts`, plus the QR pages added to the admin and shop viewport tests;
  - the admin reschedule race fix in `admin-operations.spec.ts`.
- **Docs:** D-114, D-115, DV-A14, traceability, domain model, permissions matrix.
- **Infra:** compose `RateLimiting__qr__PermitLimit`.

## Data model and migration impact
- Schema `qr`: `qr_code_links`, `qr_code_routes`, `qr_visits`.
- `bookings.bookings` gains `qr_link_id` (composite FK `(shop_id, qr_link_id)` → `qr.qr_code_links`) and `qr_visit_id`, with a filtered index.
- Migration `20260930111559_Qr`.

## API contracts and UI routes
As listed. OpenAPI regenerated: 12 operations added, none removed.

## Security, tenancy, privacy, RTL, a11y, responsive
- No IP address or user agent is stored: a per-day keyed hash and a device class only.
- The attribution cookie is first-party and HttpOnly, sent only to `/api/v1`, and read only by the customer's online booking.
- Codes are shop-owned. The anonymous path goes through the route table and the one shop's public scope. Scans reach shops only through their own codes.
- A booking can only be credited to its own shop's code (composite FK).
- The poster's print styles are RTL. The URL is left-to-right in Arabic text.
- Axe finds no serious issues on the landing, admin, poster and shop pages. There is no overflow at 390/768/1440.

## Tests and verification commands
```
dotnet test --project tests/Trimme.UnitTests -c Release --filter-class "*QrDomainTests"
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*QrTests"
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test flows/qr.spec.ts
```

## Acceptance criteria
- A scanned QR produces a visit.
- A booking within the window is credited.
- The analytics numbers match the seeded data.

## Rollback / recovery
- Revert the commit. The migration's `Down` drops the `qr` schema and the two booking columns.
- Switching a code off stops it resolving without losing data.

## Completion evidence
Session 12 (2026-09-30).

| Check | Result |
|---|---|
| Re-validation of Phase 15 | `NotificationsTests` 12/12; CI green on the pushed `786d8c8` (the user confirmed) |
| `dotnet build Trimme.slnx -c Release` | PASS, 0 warnings, 0 errors |
| Unit tests | PASS 420/420 (395 → 420): `QrDomainTests` 25 |
| Architecture tests | PASS 65/65; the tenancy allow-list gains `QrCodeRoute` and `QrVisit` with reasons |
| Integration tests (Testcontainers PostGIS) | PASS 175/175 on three consecutive runs (172 → 175: `QrTests` 3). Two changes came first. The shop-facing contract needed `Produces<byte[]>` on the image endpoints, and the anonymous allow-list needed the two public QR endpoints. Then rare connection-open timeouts under parallel load: 1–3 random tests per run before the fix, one run of 7 while a web build ran alongside. The fixture's connect timeout went from 15 s to 60 s, and three quiet runs followed with no failure. |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| OpenAPI (regenerated) and `pnpm openapi:check` | PASS; 12 operations added, none removed |
| Web `format:check`, `lint`, `typecheck`, `test`, `build` | PASS; 366 tests (357 → 366); `next build` includes the five new routes |
| E2E `tsc`, `prettier --check` | PASS |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2 --retries=0` | **72/72 ×2** on the final code. API log: 0 × 5xx, 0 × 429. The only ERR is E6's expected exclusion-constraint loss (the database decides the race). Two EF warnings come from Phase 15's notification sweep job and Phase 08's admin subscription read, not from Phase 16 code. |
| Linux Chromium (`mcr.microsoft.com/playwright:v1.63.0-noble`) against the stack | `qr`, `admin-operations`, `shop-dashboard`, `public-discovery`: **19/19**, including the 390 px overflow tests with `/ar/admin/qr` and `/ar/shop/qr` |
| No-transfer and R-NEG-08 grep gates | PASS: hits only in the test files that assert absence, seeders and the excluded geocoder |
| gitleaks v8.30.1 `dir` (and `git` after the commit) | PASS: no leaks (see the commit note below) |
| Visual check (screenshots at 390 and 1440) | Landing (ar, barber in en), admin QR (desktop and phone cards), poster (screen and print emulation), shop QR. This led to three fixes: the poster's squeezed code box (children no longer shrink; the code takes 72 % of the card), start-aligned LTR URLs, and no mid-code URL wrap in the admin table |

**Found and fixed during the phase:**
- **Unprefixed paths answered 404 (D-115).** The web proxy's matcher `'.*\..*'` reached the regex as `.*..*`, so locale negotiation ran only for `/`. The printed `/q/{code}` needs it. The dot is now escaped.
- **Composite key types.** The booking → code key needed the same id type on both sides, so `QrCodeLinkId` moved to the shared domain.
- **E2E run 2 (first fresh stack): 71/72.** The QR test found the shop card and admin row by label, and a second run on one stack created a second code with that label. It now matches by the generated code.
- **E2E run 2 (second fresh stack): 71/72.** A Phase 14 test failed: it force-checked the first reschedule slot while today's slots were being replaced by the new day's. It now waits for the new day's options and clicks the visible label (3/3 alone, and 72/72 ×2 after).

**Database.**
- Migration `20260930111559_Qr` was applied on fresh compose volumes and from empty in the integration tests.
- The seed adds 6 codes, the daily scans and three credited bookings.
- No production migration was run.

## Remaining risks → next phase
- **Local visitor hashes collide.** Local compose proxies `/api` through the web container, so every visitor shares one address. Production needs Nginx to forward the client address (D-094) and the web network in `KnownNetworks`. This is Phase 17.
- **QR images are generated per request.** They are small, but a CDN or cache header policy is a Phase 17 item.
- **The PDF is the code only.** The A5 poster relies on the browser's print dialog ("Save as PDF").
- **Next:** Phase 17, hardening.
