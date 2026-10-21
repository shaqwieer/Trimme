# TRIMME Session Handoff

- **Updated:** 2026-10-21 (Session 18: the desk's pages — breaks by the hour, the walk-in's mobile, a simple appointments list (D-134); «انتهت الخدمة» and new or returning customers (D-135); after Phase 17)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Sessions 17 and 18 are pushed (`60bb98e`) and deployed on groomhere.sa (2026-10-21).
- **Working tree:** clean after the commit, except the five user guides in `docs/guides/` (PDF/DOCX), which were untracked before and are left alone.
- **Local stack: running, all in compose.** Ports: web 3300, API 8080, DB 5434, Mailpit 8325.
  - The `trimme-api:dev` and `trimme-web:dev` images were rebuilt from Session 18's tree, but the `trimme` stack on 3300 was **not** restarted or migrated. It still runs Session 17's containers against a volume without `WalkInContacts`. Run `up -d` (which runs `migrate` and the seed) when memory allows.
  - The local volume is several days old: Barber House's seeded subscription has lapsed there, so the public E2E flows fail on it. Run E2E on a fresh stack (see Session 17 below) or after `down -v`.
  - Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
- **Demo deployment:** https://groomhere.sa (VPS, demo mode; www redirects to the apex; trimme.net is no longer served). Redeploy from `main`: `git pull` in `/opt/trimme`, then build, migrate and `up -d` in `/opt/trimme-deploy`. A changed `Caddyfile` needs `docker compose up -d --force-recreate caddy`.
- **Phases:** 17 is the last phase (D-116) and is complete at 98/100; 2 points wait on the NVDA pass. Dark mode (D-124) and this session's booking changes (D-125) were requested after the phases and are recorded as decisions, not phases.

## Session 18: the desk's pages (D-134); «انتهت الخدمة» and new or returning customers (D-135); the client's answers and the deploy (D-136)
- **The client's points** (screenshots of the desk account on groomhere.sa):
  1. **The desk saw the shop's and barbers' hours, time off and closures** on «الدوام والبريكات» (read-only since Phase 09, but it looked editable). The client asked why: there was no strong reason (the desk could see who works when). The desk now sees only «البريكات», and `GET /shop/schedule` answers 403 to it.
  2. **Breaks with no fixed times or reasons:** each barber's day as 24 hour cells; one tap makes an hour a break and another frees it; «استراحة طوال اليوم» and «إلغاء كل الاستراحات». Booked hours, the owner's breaks and time off cannot be changed there. New `Shop.Breaks.Manage` and `GET/PUT /shop/schedule/barber-day`. Break names are optional, and the owner's form has no name field. The demo's prayer and lunch breaks are no longer seeded, and the seeder deletes them, so groomhere.sa loses them on the next deploy. The owner's page keeps everything and gets the same day view.
  3. **Walk-in:** the summary no longer hangs mid-screen on a phone (it follows the form; it stays beside it from `lg`). The desk asks for the customer's mobile, which is optional (the client changed it from required); the name is required. A given mobile is stored encrypted (`bookings.walk_in_contacts`, purpose `trimme.walk-in-phone`), never returned to the shop, and shown to admins masked on the booking page, with an audited reveal (`Admin.Customers.ViewContact`).
  4. **Appointments for the desk:** «حجوزات اليوم» and «المواعيد القادمة» by day, a week at a time, Pending, Confirmed and Arrived only. Tapping opens the drawer. The owner keeps the full board.
- **A desk login is not a barber.** ShopStaff is not linked to a professional, so the break page asks which barber. Per-barber logins would be a separate feature.
- **Evidence (D-134 and D-135 together, final tree):**
  - .NET: unit 430/430, architecture 69/69.
  - Integration: **209/209**, run in six batches of classes (the single full run had been stopped by Claude Code for low memory at 19/0). New: `Staff_SetABarbersBreaksForOneDay_ByTheHour_WithoutAReason`, `WalkIns_TakeTheCustomersMobile_ForThePlatformTeamOnly`, `FinishingEarly_FreesTheRestAtOnce_AndTheOwnerSeesNewAndReturningCustomers`. `ShopFacingContractTests`, `MigrationTests` and `OpenApiContractTests` (regenerated) pass.
  - Web: `lint`, `typecheck`, `format:check`, `openapi:check`, `build`, `bundle:check` pass; `pnpm test` 51 files, 473 tests (new: `barberDay.test.ts`, `BarberDayCard.test.tsx`, `SimpleAppointments.test.tsx` with «انتهت الخدمة», the optional walk-in mobile, the admin walk-in reveal, the drawer's «زبون عائد»).
  - E2E on a **fresh** stack (`-p trimme-verify`, ports 3310/8090/5444/8335, removed afterwards), `CI=1 --workers=4 --retries=0`, images built from the final tree: **102/102** (smoke, flows, a11y, a11y-dark). An earlier run on the D-134 tree was also 102/102.
  - Screenshots in the session scratchpad (`shots-d134/`): as the desk at 390 px, the break grid, the walk-in, the appointments list; the owner's schedule at 1440 px; the owner's KPI tiles («زبائن جدد · آخر 30 يوماً» 2, «زيارات العائدين: 2») and the board with «زبون جديد» / «زبون عائد» on the demo's completed visits.
- **Walk-in autofill:** the mobile field has `autocomplete="off"`, so the desk's browser should not offer earlier customers' numbers (best effort).
- **Deployed 2026-10-21 (`60bb98e`):**
  - Backup first: `/root/backups/trimme-before-d133-d136-20261021-091514.sql.gz` on the VPS (complete dump).
  - `git pull`, `docker compose build api web`, `docker compose run --rm migrate` (applied `LoyaltyFirstVisitStamps` and `WalkInContacts`; reference data and the managed roles synchronized), `up -d`; api and web healthy. Permissions are read per request, so desk users follow at once.
  - The demo seed was **not** re-run: it would re-create any demo row the client deleted, by fixed id. Instead, in one transaction: the four seeded prayer and lunch breaks were deleted by id (they were the only breaks), and `administration.platform_settings.min_lead_time_minutes` was set to 15 (`updated_at` stamped; not in the audit log).
  - Checked from the VPS: `/ar`, `/api/v1/meta`, the Al Asala page, the staff sign-in and the public shop API answer 200. At 12:20 Riyadh the first open time at Al Asala was 12:40 (15-minute lead time on the 5-minute grid). Curl from this Windows machine to groomhere.sa fails (exit 43), so check from the VPS.
- **Then, D-135 (same session, a second message from the client):**
  - «انتهت الخدمة»: a confirmed visit can be finished once it has started (state machine Confirmed → Completed), and a visit finished early ends at that minute. Completed bookings already freed their time (only Pending, Confirmed and Arrived hold it), but completing took two steps in the drawer. The desk's list has the button on each visit under way; the drawer's action reads «انتهت الخدمة».
  - The online **minimum lead time** (`MinLeadTimeMinutes`, 60 by default, admin settings) still stops people at home from booking time freed in the next hour. The walk-in page shows the barber free at once. Lowering it (for example to 10–15 minutes) is the client's call.
  - New or returning customers: `Shop.Insights.View` (owner only). `ShopBookingResponse.Visit` on the owner's board and drawer; the owner's overview tile with the last 30 days' new customers and returning visits. The desk gets null.
  - The walk-in mobile became optional (D-134 updated). No migration.
- **Then, D-136 (the client's answers):** online minimum lead time 15 minutes on groomhere.sa (a setting, set by SQL after the deploy's backup; the code default stays 60); «انتهت الخدمة» asks for a confirmation on the list and in the drawer; the owner has the same simple appointments list (with the new or returning badges); the full board is removed. Web 474 tests; E2E on a fresh stack with the final web image 102/102.
- **To ask the client:** will each barber get their own login (a separate feature)?
- **CI follow-up (after the push):** CI failed on `60bb98e` and `fbc176f`, on two causes, both fixed (`40c890f`, `1bf427a`, not pushed yet):
  - Web: `pnpm audit` found vite 8.3.1 (GHSA-587h-7wmj-h5pm, high), the implicit peer of vitest. `apps/web` now declares vite 8.3.4 itself (`docs/security.md`). The "1 high" pnpm still prints is the muted `braces` CVE; the audit exits 0.
  - Backend: 4 integration tests failed with `53300: sorry, too many clients already`. This is the same failure earlier sessions reran as a parallel-load flake, and it is not one: about 80 tests each create a database, and their idle connections stayed for 300 s. The test connection strings now close idle connections after 10 s (D-069 updated). A full local run went from 10 failures to 209/209, peaking at 68 of 400 connections. **If `53300` comes back, treat it as a bug.**
  - Locally, every other CI step passes (unit 430, architecture 69, NuGet audit, no pending model changes, web lint, typecheck, format, 474 tests, OpenAPI drift, build, bundle budgets). The stack job has not run on CI since `8ac7d9a`; the local fresh-stack E2E above was 102/102.

## Session 17: service before time from a barber's page or code; first-visit points (D-133)
- **The client's three points:**
  1. **Two salons, two cards?** Yes, already: a card belongs to the salon and the customer, and `/me/loyalty` lists one per salon with no set limit. Now tested with two salons.
  2. **After scanning a barber's code, the service was skipped.** The «أقرب الأوقات» chips (barber page and QR page) opened the wizard on the review with a service the system had picked. Also, the barber's own «احجز» link lost the barber until a service was chosen.
     - The chips are removed (DV-T24). The QR page's services each have «احجز».
     - The wizard keeps `pro` without a service and lists only that barber's services, under «الحجز مع …» with «تغيير المختص».
     - With a barber chosen, the services lead straight to the day and time. Back still reaches the specialist step.
     - Order now: scan → service → time → confirm.
  3. **First-visit points:** «نقاط أول زيارة» on the admin's «الولاء» tab (1 to visits − 1). A customer's first completed visit at the salon fills that many circles; every later visit fills one. Read as a programme rule from the client's example, not a manual grant to one customer: confirm with the client.
- **Backend:** `LoyaltyProgram.FirstVisitStamps`, a stamp row per circle (`stamps.number`, unique (`booking_id`, `number`)), migration `20261020205012_LoyaltyFirstVisitStamps` (additive; defaults 1; check constraints). The request field is optional (null = 1).
- **Evidence:**
  - .NET: unit 429/429, architecture 69/69. Integration: a full run under a parallel Docker build was 195/206; the 11 failing tests (`BookingTests`, `NotificationsTests`, `QueryCountTests`, `SessionTests`, `NotificationsHubTests`) then passed 41/41 alone. `LoyaltyTests` 3/3 (new: first-visit circles, two salons → two cards, validation), `MigrationTests` (applies from empty, no pending model changes) and `OpenApiContractTests` 4/4 without regeneration.
  - Web: `lint`, `typecheck`, `format:check`, `openapi:check`, `build`, `bundle:check` pass; `pnpm test` 48 files, 462 tests.
  - E2E on a **fresh** separate stack (`-p trimme-verify`, ports 3310/8090/5444/8335, the same images; removed afterwards), `--retries=0`: `qr` + `public-discovery` 11/11 (including the new barber path), `customer-booking` 5/5, `a11y` route audit + keyboard 13/13.
  - The same specs on the long-lived local volume failed 8/11, on pages this change does not touch, because Barber House's seeded subscription has lapsed there.
  - Screenshots at 390 px (ar): the barber QR page (no chips, «احجز» per service), the wizard (banner, Sultan's two services only), the day-and-time step straight after; the admin form with «نقاط أول زيارة» = 3; the salon card «اجمع 6 دوائر…» / «أول زيارة لك تملأ 3 دوائر…».
- **To deploy (not done):** pull, build, run `migrate` (applies `LoyaltyFirstVisitStamps`), `up -d`. Take a database backup first, as for D-132.

## Session 16, second request: Turkish, the loyalty card, «موقع سعودي ١٠٠٪» (D-132)
- **Turkish (`tr`)**: `/tr` routes, `messages/tr.json` (translated; `messages.test.ts` checks parity with ar/en), a three-language menu, Turkish emails; WhatsApp and the sign-in code use the English templates for Turkish speakers (no Turkish templates are approved).
- **Loyalty**: new module `src/Modules/Loyalty` (schema `loyalty`, migration `20261019200939_LoyaltyProgramme`, additive). Admin tab «الولاء» on the shop page (`Admin.Loyalty.Manage`); salon page card; `/account/loyalty`; the coupon picker on the wizard's review step; «كوبون» chips on the salon's appointments. Tests: `LoyaltyTests` (integration), `loyalty.test.tsx`, the wizard coupon test.
- **To do on a real deployment**: grant `Admin.Loyalty.Manage` to an existing OperationsManager role by hand if needed; set each salon's programme from its «الولاء» tab (none is set by the seed).
- **Open**: `tests/E2E/audit/routes.spec.ts` still audits ar and en only (Turkish not yet in the a11y route audit).
- **Dependencies**: Next.js and eslint-config-next 16.3.8 (GHSA-cjq9-62q9-8jv4, high). `pnpm audit` locally still lists `braces@3.0.3` (no fixed release exists; it was in the lockfile before); CI's audit step passed.
- **Verified**: web gates (459 unit tests), production build and bundle budgets; .NET unit (429), architecture (69) and loyalty/auth integration tests locally (a full local run hit 9 Postgres connection timeouts that passed on re-run); CI run 39322851392 on `7f99b05` green on all four jobs, including the Docker stack + Playwright smoke, flows and route audit.
- **Deployed**: `7f99b05` on groomhere.sa; migration `LoyaltyProgramme` applied; database backup taken first at `/root/backups/trimme-before-loyalty-20261019-225202.sql.gz` on the VPS. No salon has a loyalty programme yet (set it on the shop's «الولاء» tab).

## Session 16: Groom Here (D-131)
- The product name is **Groom Here** in both locales, the page metadata and the emails. The logo, Open Graph image and favicon come from the client's `public/brand/new-logo.jpeg`.
- Internal names (cookies, `TRIMME_*` variables, namespaces, the API title) are unchanged on purpose.
- The demo moved to groomhere.sa. On the VPS, `compose.yml` (CORS, `Web__PublicBaseUrl`, `TRIMME_SITE_URL`, mail sender) and the `Caddyfile` were changed; backups sit next to them as `*.bak-trimme-net`.
- Pinned `System.Security.Cryptography.Pkcs` 10.0.13 and `Microsoft.AspNetCore.SignalR.Client` 10.0.13 after new advisories failed restore.
- Open: an Arabic spelling of the name, if the client wants one; the user guides in `docs/guides/` still say TRIMME.

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
- Session 18: `20261021064349_WalkInContacts` (additive), applied on groomhere.sa (2026-10-21), on fresh verification volumes and from empty in the integration tests. Not yet on the local compose volume.
- Session 17: `20261020205012_LoyaltyFirstVisitStamps`, applied on groomhere.sa (2026-10-21), the local compose volume, fresh verification volumes and from empty in the integration tests.
- groomhere.sa data changes by SQL (2026-10-21, after the backup): the four seeded breaks deleted; online minimum lead time 15 minutes.
- Created and applied locally: `20261001082134_PerformanceIndexes` (index-only), on the compose volume and from empty in the integration tests.
- There is no production (non-demo) database yet; groomhere.sa is the demo.

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
0. Done: Sessions 17 and 18 pushed and deployed. Confirm with the client:
   - that «نقاط أول زيارة» (a rule for every customer's first visit) is what they meant;
   - per-barber logins (see Session 18).
1. Done: dark mode pushed to `origin/main`. Watch its CI run: `pnpm e2e` now also runs the `a11y-dark` project (8 more audit tests), so keep an eye on the 45-minute stack job.
2. Run the NVDA checklist (`docs/accessibility.md` §3) and record it in the Phase 17 file. That restores the remaining 2 points.
3. All phases are complete. Any further work, such as production deployment or the items above, needs a new plan agreed with the user.

## Files intentionally left modified
- None.
