# TRIMME Session Handoff

- **Updated:** 2026-10-04 (Session 15: simpler customer booking, after Phase 17)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Pushed: everything, including Session 15's booking changes, at the user's request.
- **Working tree:** clean after the commit.
- **Local stack: running, all in compose.** Ports: web 3300, API 8080, DB 5434, Mailpit 8325.
  - The `api` and `web` services were rebuilt from this session's tree. The database was not touched.
  - Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
- **Demo deployment:** trimme.net (VPS, demo mode) was redeployed from `main`: `git pull` in `/opt/trimme`, then build, migrate (no new migration) and `up -d` in `/opt/trimme-deploy`.
- **Phases:** 17 is the last phase (D-116) and is complete at 98/100; 2 points wait on the NVDA pass. Dark mode (D-124) and this session's booking changes (D-125) were requested after the phases and are recorded as decisions, not phases.

## Session 15, second request: short home page (D-126) and admin services (D-127)
- **Home page:** only the hero (search, «استخدم موقعي», the figures, browse link), the photo (16:10 on phones) and the top-rated salons.
  - The trust strip, how it works, outcomes, shop-partner band, FAQ and final call to action were removed, with their catalogue entries.
  - The header anchors became Shops and Nearby.
  - Page height at 390 px went from 5,485 to 1,746 px.
  - Shop sign-up has no entry point on the home page now (`TRIMME_PARTNER_CONTACT_URL` is unused).
- **Admin services:**
  - New permission `Admin.ShopServices.Manage`.
  - New endpoints: `POST /admin/shops/{id}/services` and `PUT /admin/services/{id}`, both with `professionalIds` and both audited.
  - New Services tab on the admin shop page, the `/admin/services/new?shopId=` page, and an edit form on `/admin/services/{id}`.
  - Creating a shop lands on its Services tab.
  - An existing OperationsManager role needs the permission granted by hand.
- **Gap to raise:** admins cannot set a shop's opening hours.
- **Fifth request (D-130):**
  - The wizard shows every 5 minutes, with unavailable ones greyed; demo `SlotStepMinutes` is back to 5.
  - «كل الخدمات» on the services step.
  - The shop's catalogue pages are removed; the admin manages packages through the new endpoints and pages.
  - The shop dashboard has a bottom tab bar on phones, and the schedule label is «الدوام والبريكات».
  - `/discover` is the logo with the location, then the salons; sign-in lands there.
  - QR was checked and works on the demo.
- **Fourth request (D-129):**
  - Customer wording changed from «حلاق / محل» to «صالون / مختص» (dashboards unchanged).
  - The wizard has four steps: the day's times show under the days.
  - The month is shown on the date chips.
  - The salon phone is removed from the public API and all customer pages.
  - `SlotStepMinutes` = 15 on the demo database.
  - Open: there is no TRIMME support contact for customers yet.
- **Third request (D-128):**
  - No "from" price on the shop page bar (now a centred «احجز الآن») or on the shop cards.
  - The search button reads «احجز الآن».
  - The figures row is removed, with its `public/stats` request.
  - The FAQ and closing call to action are restored.
  - Home page height at 390 px: 2,753 px.
- **Also fixed:** a race in the `FilterSheet` unit test. It now waits for the count request that carries the new filter.

## Session 15: simpler customer booking (D-125)
- **What (the client's nine points):**
  1. The search placeholder is «ابحث عن صالون».
  2. The location button says «استخدم موقعي».
  3. Several services can be booked in one booking.
  4. Services are compact tiles.
  5. Dates start at today.
  6. The time is picked as the hour, then the minutes.
  7. The nearest free time is offered first.
  8. Step circles show progress.
  9. Going back keeps every choice.
- **Backend:**
  - `serviceIds` on the public `availability/dates|slots` and on `POST /bookings`. `IBookableOfferCatalog.FindServicesAsync` sums durations and prices and keeps the barbers assigned to every service.
  - A booking keeps `service_id` = the first service and lists every service in the JSON items, so there is no migration.
  - Reschedule and the admin candidates resolve the booked services (`FindBookedAsync`). A service that appears only in booking items is in use.
- **Web:**
  - `wizard.ts`: repeated `service`, `combineOffers`, `withItems`, `restoreSelection`, `offerKey`.
  - New components: `ServiceTile`, `HourMinutePicker`, `BookingProgress`.
  - Rebook links carry every service.
  - The last choices per shop are kept in module memory (Web Storage is banned).
- **Evidence:**
  - Web: `pnpm lint`, `typecheck`, `format:check`, `openapi:check`, `build` and `bundle:check` all pass. `pnpm test` passes 44 files and 434 tests.
    - One earlier run under parallel Docker load timed out the five-step wizard test at 5.2 s. It now has a 15 s timeout.
  - .NET:
    - Unit tests 429/429 and architecture tests 65/65.
    - Integration tests 198/200 on a run concurrent with a Docker rebuild. Both failures were Postgres `53300 too many clients` in the shared test container. Their classes then passed alone: `BookingTests` 11/11 (including the new multi-service test) and `NotificationsTests` 12/12.
  - E2E on compose with the rebuilt API and web, `--retries=0`:
    - `customer-booking` and `qr` passed 6/8. E1, E6 and QR passed.
    - E7 is the known volume issue (Sara's seeded visits are past the review window).
    - "favorites and profile" timed out under parallel load and passed 2/2 alone.
  - Visual check at 390 px: tiles, step circles, nearest-time card, dates from today, hours then minutes, review. Browser back twice kept the services, date and time.
  - axe on the services (checked tiles), date (nearest card), hours and minutes steps, in light and dark: 0 serious, contrast included. The route audit itself only visits the wizard's first step.
  - A real two-service booking through the local UI: haircut + beard via "Book this time". Majed was picked automatically (the only barber who does both), 10:00–10:45, 85 SAR, and the detail reads «قص وتصفيف + تحديد لحية». The booking was cancelled, and the rebook link carries both services.
- **CI follow-up (after the push):** the web job failed `pnpm audit --audit-level high` on a new advisory. CVE-2026-93687 (`braces` ≤ 3.0.3, published 2026-09-18, no fix released) sits in the lint-only `eslint-config-next` chain. It is not caused by this change, and any push would have failed. It is ignored by CVE in root `package.json` and recorded in `docs/security.md` with a removal condition. That failure had also skipped the Playwright job, so the follow-up push is the first full CI run of D-125.
- **Known limits:**
  - Booking statistics count services booked together under the first service.
  - The per-shop memory lasts across in-app navigation, not a reload.
  - Walk-ins stay single-service.

## Session 14: dark mode (D-124)
- **What:** Light / Dark / System (System by default) across every page and portal.
  - The theme button is in every shell header (public and auth pages, customer, shop and admin dashboards), and the customer account page has an Appearance setting.
  - The choice is saved in the `trimme-theme` cookie (one year) and rendered by the server: no flash, no inline script, no hydration mismatch, correct even without JavaScript.
  - System follows the OS live through CSS. Open tabs follow a change through a `BroadcastChannel`. Printing stays light.
- **How:** one dark palette under `@custom-variant dark` in `tokens.css`, with the same token names, so utilities re-theme.
  - `navy-900` is ink and inverts.
  - New `chrome` tokens keep brand navy panels navy: the sidebar and drawer, toasts, hero, subscription card, poster, photo badges and map pins.
  - Shadow colours are variables. MapLibre controls are restyled; tiles, photos and QR codes are untouched.
  - Rules for new UI: `docs/theming.md`.
- **Fixes found on the way:** `bg-warning-100` (not a token) on the paused chip is now `warning-50` (DV-T15). The header selector first used Radix's dropdown, which pushed public pages to 228.7 KB (budget 220). It is now a native radio disclosure (216.6 KB). A focus bug in that disclosure, where a mouse choice was lost, was caught in the browser and is now covered by a unit test.
- **Evidence:**
  - Web: `pnpm lint`, `typecheck`, `format:check`, `test` (44 files, 423 tests), `build` and `bundle:check` all pass.
  - `flows/theme.spec.ts`: 7 tests, 5 consecutive green runs.
  - `a11y-dark` route audit: 8/8, axe 0 serious or critical, colour contrast included, and every page confirmed dark.
  - Full E2E on compose (all four projects): 99/100. The one failure was `qr.spec`: the switched-off code still shows active after 5 s under parallel load; it passes 3/3 alone.
  - Without `a11y-dark`: 89/92. The failures were `qr.spec`, `customer-booking` E7 (Sara's seeded visits are past the 7-day review window on this volume) and the retired-QR page reporting no `lang`/`dir` in the light audit.
  - **The pre-change baseline (`f864343`) on the same volume fails the same three tests** (82/85), so they are pre-existing volume and load issues, not regressions. A run on a bare `next start` failed only where the harness differs (dev routes off, no hub proxy).
- **Visual pass in dark** at 1280 and 390, with data loaded: the shop board, appointment drawer, cancel dialog, day and week calendars, the gallery's dropdown, toasts, tooltip, dialog, sheet and confirm, the search filter sheet, the booking wizard (selected slot, disabled Next), dashboards, tables, forms, charts, the QR pages and poster, account and auth.
- **CI follow-up (after the push):** CI failed the route audit on `/en/shop/*` (5 px overflow at 390, light and dark) and flaked on `/ar/q/bhxx8dfg`.
  - The overflow was the dashboard header plus the theme button; reproduced in `playwright:v1.63.0-noble` (6 px, title squeezed to 0 px). The dashboard theme button now lives in the drawer below 768px (D-124).
  - The flake: every 404 is served as Next's bare `__next_error__` document, and the localized not-found page renders on the client. The audit now waits up to 10 s for the root layout's `lang` before checking.
  - Evidence: Linux Chromium route audit (light and dark), keyboard pass and theme flows **28/28**, `--retries=0`; the hub-blocked worst case is 0 px overflow with the title back to 32 px. Web 424 tests. Compose full suite 98/100; the two failures are the known volume ones (E7, QR), which also fail on the baseline.
  - Worth a separate look: server-render the localized 404 (no-JS visitors and crawlers currently get an empty document).
- **Not inspected by hand:** the NVDA pass (still open from Phase 17), and Safari and Firefox. Chrome only.

## Completed in Session 13 (Phase 17)
Phase 17 (`phases/phase-17-hardening.md`, D-117 to D-123).

**Security** (`docs/security.md`):
- A per-request nonce CSP on every page (`proxy.ts`). Every page renders per request.
  - Files and 404s get a script-less policy; COOP/CORP `same-origin`; HSTS for an HTTPS site.
  - Every E2E test fails on a CSP violation. On its first run that guard found the API CSP blocking the Hangfire dashboard's inner pages, now fixed.
- Uploads are re-encoded from their pixels with SkiaSharp (D-119): no metadata or appended payload survives, the EXIF orientation is applied, and the longer side is at most 2,560 px. Checked on Linux through compose.
- The Data Protection key ring was stored unencrypted next to the numbers it protects. It is now wrapped with a mounted certificate, required outside Development and Testing, and certificate rotation is supported (D-120).
- Rate limits: a 429 test for each of the nine policies, and a matrix test that pins which endpoints carry which policy.
- Every JavaScript-handled form posts. ZAP had found credentials in the URL on a submit before hydration.
- Scans: `pnpm audit` and the NuGet audit clean (both now in CI); gitleaks clean; ZAP 0 FAIL.
- Phone-leak sweep: logs, spans, Hangfire, outbox and audit, all 0.

**Observability** (`docs/observability.md`, D-118):
- OpenTelemetry for ASP.NET Core, HttpClient, Npgsql/EF, runtime, Hangfire jobs (own filter) and outbox spans and metrics.
- OTLP export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. A sampler drops parentless polling spans.
- `/health/ready` reports `jobs` and `outbox`, Degraded at worst.

**Localization, SEO, accessibility** (D-122, `docs/accessibility.md`):
- `tests/E2E/audit` is the Playwright project `a11y`. The route audit covers 81 routes × 2 locales, each as the right role. Its first runs found:
  - raw permission and audit keys;
  - a hydration error on `/admin/qr`;
  - missing `lang` on parts in the other language;
  - MapLibre's English labels;
  - three inverted chevrons.

  All are fixed.
- The keyboard pass found dialogs dropping focus on close, now fixed in the shared overlays, and touch targets under 44 px, which now grow under `pointer: coarse`.
- SEO: sitemap index plus `/sitemaps/ar.xml` and `/sitemaps/en.xml`; a default Open Graph image; Open Graph on the legal pages; JSON-LD validated by the audit.
- Plural review counts.

**Performance** (`docs/performance.md`, D-123):
- `QueryCountTests` found an N+1 in discovery's availability probe. It is batched (`ProbeManyAsync`, six queries for any number of shops).
- Three indexes (migration `PerformanceIndexes`).
- EF query-shape warnings throw in Testing.
- Bundle budgets (`pnpm bundle:check`): form pages went from 412 to 320 KB gzip. The Zod import pulled every locale pack, and the validation module imported the whole catalogue.
- The k6 smoke passes.
- Lighthouse: a11y, best practices and SEO 100; LCP 3.5–3.8 s on simulated slow 4G (documented gap).
- Public cache headers (D-121).

**Visual review** of 25 screens against `design/reference`. Fixed:
- the landing search bar collapsed at 390 px (`sm` is 390 px, D-041);
- the booking back chevrons;
- the step bar on step 1;
- hidden map credits;
- two admin button styles;
- the suspended-shop badge colour;
- pagination overflowing a phone once a list reaches six pages (found by audit run 4).

The rest is recorded in DV-C11 and DV-T14.

**Also:**
- the SignalR hubs ignore a client abort during the handshake;
- the axe helpers wait for the page title (a soft-refresh race);
- CI runs the bundle budgets, the dependency audits and all Playwright projects.

## Verification evidence
| Command | Result |
|---|---|
| Phase 16 re-validation | `QrTests` 3/3, `qr.spec.ts` 3/3 |
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 429 / 65 / 199 |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web `format:check`, `lint`, `typecheck`, `test`, `build`, `openapi:check`, `bundle:check` | PASS: 385 tests; 79 routes within budget |
| Fresh `down -v` + `up --build`, `CI=1 --workers=2 --retries=0` | 85/85 ×2; 0 × 5xx, 0 × 429. After the hub fix: run 3 84/85 (axe `<title>` race, fixed); run 4 83/85 (pagination overflow at 390 px on a six-page list, fixed); run 5 **85/85** |
| Linux Chromium: route audit, keyboard pass, `public-discovery`, `customer-booking` | 26/26 (`customer-booking` rerun after its seeded numbers' hourly OTP limit, which runs 4 and 5 had used) |
| ZAP baseline; `pnpm audit`; NuGet audit; gitleaks `dir`/`git` | 0 FAIL; clean; clean; no leaks (`git`: 67 commits) |
| Phone-leak sweep | 0 everywhere (logs, 50,601 spans, Hangfire, outbox, audit) |
| k6 smoke; Lighthouse | Passed thresholds; recorded in `docs/performance.md` |

## Database and migrations
- Created and applied locally: `20261001082134_PerformanceIndexes` (index-only), on the compose volume and from empty in the integration tests.
- No production migration was run.

## Decisions added
- D-117: nonce CSP.
- D-118: OpenTelemetry, health and logs.
- D-119: upload re-encoding.
- D-120: key ring encrypted at rest.
- D-121: caching.
- D-122: localization audit rules.
- D-123: performance budgets and the batched probe.
- Design deviations: DV-C11 and DV-T14 added; DV-S09 corrected to applied.

## Known issues or blockers
- **The NVDA pass is not run** (manual, `docs/accessibility.md` §3); 2 points are held back.
- **E2E on the long-lived volume:**
  - E7 needs a fresh volume (`docker compose down -v`), as its own message says.
  - `qr.spec`'s switch-off assertion and the retired-QR `lang`/`dir` check fail under full parallel load, on the baseline too. They are worth a look: the 5 s wait, and the 404 path for retired codes.
- **LCP on simulated slow 4G is 3.5–3.8 s** (target 2.5 s; next steps in `docs/performance.md` §4).
- **A contended booking race costs about 1 s** (deadlock path, D-089); a per-barber advisory lock is recommended (D-123).
- **Native date and time inputs** follow the browser's language (DV-T14).
- **The OTP limit when re-running E2E.** `customer-booking` signs in with three fixed seeded numbers, and each number gets 5 OTP codes per hour. More than about five suite runs in an hour against one stack give 429 on `/auth/otp/request`. That is the limit working, not a regression. CI uses a fresh stack per run.
- **Production setup is not in this repository** (D-116):
  - Nginx with `X-Forwarded-For` overwrite, `KnownProxies`/`KnownNetworks` and `client_max_body_size 6m`;
  - the certificate and the lookup key;
  - SMTP, WhatsApp and map hosts;
  - a shared rate-limit store before scaling out;
  - staff MFA.

  See `docs/security.md` §3 and §6.
- **Carried over:** a backplane for the hubs; a dead-letter replay screen; "any professional" does not retry; packages across professionals (D-020); grace days and limits (D-077).

## Exact next action
1. Done: dark mode pushed to `origin/main`. Watch its CI run: `pnpm e2e` now also runs the `a11y-dark` project (8 more audit tests), so keep an eye on the 45-minute stack job.
2. Run the NVDA checklist (`docs/accessibility.md` §3) and record it in the Phase 17 file. That restores the remaining 2 points.
3. All phases are complete. Any further work, such as production deployment or the items above, needs a new plan agreed with the user.

## Files intentionally left modified
- None.
