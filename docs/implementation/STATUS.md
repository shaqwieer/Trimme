# TRIMME — Status

- **Updated:** 2026-10-04 (Session 15: simpler customer booking, after Phase 17)
- **Current phase:** 17, the last phase (D-116), is complete and verified at 98/100. It delivers:
  - **Security:** a nonce CSP on every page, plus COOP, CORP and HSTS; uploads re-encoded from their pixels; the Data Protection key ring wrapped with a certificate; a test for every rate limit; ZAP, dependency audits, gitleaks and a phone-leak sweep all clean (`docs/security.md`).
  - **Observability:** OpenTelemetry traces and metrics (OTLP only when configured), health checks for Hangfire and the outbox, and log conventions (`docs/observability.md`).
  - **Localization, SEO and accessibility:** a route audit of all 81 pages in both locales as each role (axe 0 serious), a keyboard pass, a sitemap index, Open Graph images and validated JSON-LD (`docs/accessibility.md`).
  - **Performance:** N+1 and query budgets in tests, a batched availability probe, three indexes, bundle budgets, a k6 smoke and recorded Lighthouse runs (`docs/performance.md`).
  - **Visual review** of all 25 design screens.

  2 points are held back for the NVDA screen-reader pass, a manual checklist the user can run. The phase is committed locally and not pushed.
- **After Phase 17 (Session 14, at the user's request): dark mode (D-124).** Light / Dark / System (System by default) on every page, from the shell headers and the account page. The choice is saved in a cookie the server renders from, so there is no flash and no script; open tabs follow at once; printing stays light. The dark palette is checked by `tokens.test.ts`, `flows/theme.spec.ts`, and the `a11y-dark` route audit (81 routes, both locales, every role, axe 0 serious). See `docs/theming.md`.
- **After Phase 17 (Session 15, at the client's request): simpler customer booking (D-125).**
  - New copy: «ابحث عن صالون» and «استخدم موقعي».
  - Several services in one booking: one barber, back to back, durations and prices added. The API takes `serviceIds`; no migration.
  - Compact service tiles, and dates that start at today.
  - The time is picked as the hour first, then that hour's minutes.
  - The nearest free time is offered first, with one tap to book it.
  - Step circles (الصالون — الخدمة — الوقت — التأكيد) at every width.
  - Going back, with the app or the browser, keeps every choice.
  - Deployed to trimme.net.
- **Session 15, second request (D-126, D-127).**
  - The home page is cut to the search (with the location and the figures), the photo and the top-rated salons: 1,746 px tall on a phone instead of 5,485.
  - The admin shop page has a Services tab to add and edit a shop's services and pick its barbers for each.
  - Gap: admins still cannot set a shop's opening hours.
- **Platform progress:** 1798 / 1800 points (Phases 00–17). Phase 18 was removed at the user's request (D-116).

| Phase | Status | Points |
|---|---|---|
| 00 Discovery & plan | [x] | 100/100 |
| 01 Backend & infra foundation | [x] | 100/100 |
| 02 Web foundation | [x] | 100/100 |
| 03 Design-system components | [x] | 100/100 |
| 04 Identity & sessions | [x] | 100/100 |
| 05 Tenancy, privacy & audit | [x] | 100/100 |
| 06 Shops, locations & professionals | [x] | 100/100 |
| 07 Services & packages | [x] | 100/100 |
| 08 Subscriptions & settings | [x] | 100/100 |
| 09 Schedules & availability | [x] | 100/100 |
| 10 Booking core | [x] | 100/100 |
| 11 Public discovery | [x] | 100/100 |
| 12 Customer booking & account | [x] | 100/100 |
| 13 Shop dashboard | [x] | 100/100 |
| 14 Admin dashboard | [x] | 100/100 |
| 15 WhatsApp & notifications | [x] | 100/100 |
| 16 QR & attribution | [x] | 100/100 |
| 17 Hardening | [x] | 98/100 |

## Decisions resolved with the user (D-037)

| ID | Decision |
|---|---|
| D-004 | In-house command/query dispatcher; MediatR is not used |
| D-005 | Customers sign in passwordless with mobile + 6-digit OTP over WhatsApp (fake in dev). Staff sign in with email + password, with reset over email (Mailpit in dev) |
| D-006 | Per-shop `RequireManualConfirmation`, default off, so online bookings are auto-confirmed |
| D-007 | OpenStreetMap: MapLibre with OSM tiles and a Nominatim-compatible geocoder. Production needs a self-hosted or OSM-based host, because the public OSM services' usage policies forbid heavy traffic |

## Decisions made at the user's request
| ID | Decision |
|---|---|
| D-064 | Photos and other media are stored in PostgreSQL (`media.media_files`), not on disk (Session 4) |
| D-078 | Keep the default: shops without a subscription in force are hidden and take no online bookings; the warning-only setting stays available for a temporary rollout (Session 4) |
| D-081 | Custom durations and back-dated starts need a SuperAdmin override with an explicit total price and a reason, fully audited (Session 4) |

## Open decisions
None are blocking. Phase 09 recorded D-082…D-084, Phase 10 D-085…D-089, Phase 11 D-090…D-095, Phase 12 D-096…D-098, Phase 13 D-099…D-100 and Phase 14 D-101…D-107, Phase 15 D-108…D-113, Phase 16 D-114…D-115 and Phase 17 D-117…D-123 as their own design decisions (D-096 supersedes the inline part of D-033; D-100 refines D-034); D-012 and D-015 are now Accepted; D-017 (reviews) is completed by D-102. The product assumptions D-012…D-035 remain overridable defaults. Phase 08 accepted D-013/D-014 as settings (D-076, D-078); the user confirmed D-078 and decided D-081.

## Blockers
None.
