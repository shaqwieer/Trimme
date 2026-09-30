# TRIMME Session Handoff

- **Updated:** 2026-09-30 (Session 12: CI fix, then Phase 16)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Pushed: everything up to `786d8c8` (the Phase 14 and 15 commits and the 390 px CI fix). The user confirmed CI is green.
  - Local only: the Phase 16 commit `c7f266c`, its review follow-up and the `docs:` commit (run `git log --oneline -4`).
- **HEAD commit:** the `docs:` commit recording the Phase 16 hashes and the gitleaks `git` result, on top of the review follow-up and `c7f266c` (feat: phase 16 qr codes and attribution).
- **Working tree:** clean after the commit.
- **Local stack: running, all in compose,** on a fresh volume from this session's last `down -v` + `up --build`, which built this session's final product code.
  - Ports: web 3300, API 8080, DB 5434, Mailpit 8325. Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
  - This volume has had two full E2E runs and the Linux Chromium run of four specs.
- **Current phase:** 16 is complete. Phase 17 has not started.
- **After Phase 16 (same session):**
  - `160a423` fixed the booking countdown (calendar days in the shop time zone, so a visit two dates away no longer reads «غداً»). It is pushed, with everything before it.
  - **Plan change (D-116):** Phase 18 was removed at the user's request. Phase 17 is the last phase; the platform total is 1,800 points.
- **Phase score:** 100 / 100 (Phase 16).
- **Last fully completed phase:** 16, QR codes and attribution analytics.

## Completed this session (Session 12)
- **CI fix (before Phase 16).** Runs #20 and #21 failed only in `admin pages fit phone, tablet and desktop widths`. The fix `1e457e1` put `sr-only` on a wrapper instead of the chart `<table>`. It was confirmed in Linux Chromium: the old markup gives 9 px of overflow, the fix 0. It was pushed and CI is green.
- **Phase 16** (`phases/phase-16-qr-analytics.md`, D-114, D-115):
  - **Module `QrAnalytics`** (schema `qr`):
    - `QrCodeLink` is shop-owned: an 8-character unique code, the shop or a same-shop barber as target (composite key), a label, active, never deleted.
    - `QrCodeRoute` maps code → shop for anonymous scans.
    - `QrVisit` holds the time, device class, language and a per-day keyed visitor hash; no IP address.
  - **Public:**
    - `GET /public/qr/{code}` resolves a code; a barber no longer active falls back to the shop.
    - `POST /public/qr/{code}/visits` is called from the landing page. It sets the HttpOnly `trimme-qr` cookie (`Path=/api/v1`, 7 days), and a reload within 30 minutes reuses the visit.
  - **Attribution.**
    - Only the customer `POST /bookings` reads the cookie. `IQrAttributionResolver` checks the same shop and the 7-day window.
    - The booking stores `qr_link_id` (composite FK to its shop's code) and `qr_visit_id`. The cookie is excluded from the idempotency hash.
    - `ShopBookingResponse.ViaQr` makes shop and admin views show «رمز QR».
  - **Admin:**
    - `/admin/qr` has period tabs, KPIs, scans per shop, the print notes and privacy line, and the codes table: filter, PNG/SVG/PDF, A5 poster, switch off/on.
    - The create dialog takes a shop or barber, and a label.
    - Poster page `/admin/qr/[id]/poster`.
    - Analytics: `/admin/qr/analytics?days|from&to`.
  - **Shop:** `/shop/qr` and its poster page (`Shop.Qr.View`, owner, read-only).
  - **Files:** QRCoder 1.6.0 (MIT) supplies the matrix. PNG uses its managed writer. SVG and a 70 mm vector PDF are drawn in-house. The A5 poster is a browser print page, so Arabic shaping is correct.
  - **D-115.** The web proxy's matcher was broken, and unprefixed paths answered 404. `/q/{code}` and any old unprefixed link now redirect to a locale.
  - **Seed:** `DemoQr` holds 6 codes (1 switched off) and 27 days of deterministic scans. Three demo bookings are credited (`a01`, `a24`, `a11`).
  - **Tests:** U `QrDomainTests` 25; I `QrTests` 3; W `qr.test.tsx` 9; E `qr.spec.ts` 3. The QR pages were added to the admin and shop viewport tests.
  - **Review follow-up (after `c7f266c`):**
    - the create dialog loads the chosen shop's active barbers (a platform-wide page of 100 would miss shops);
    - the privacy page gains a cookies section, and the QR privacy line no longer claims "no tracking";
    - the scan-to-book E2E now scans as a guest, then signs up, then books in the wizard without scanning again, and checks the cookie survives.
  - **Also:**
    - the integration fixture's connect timeout is 60 s (rare connection-open timeouts under parallel load);
    - the Phase 14 reschedule E2E race is fixed;
    - `/admin/qr` is in the 390 px overflow test.

## Verification evidence
| Command | Result |
|---|---|
| Phase 15 re-validation | `NotificationsTests` 12/12 |
| `dotnet build Trimme.slnx -c Release` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 420 / 65 / 175 (integration ×3 consecutive runs after the timeout change) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| OpenAPI regenerated, `pnpm openapi:check` | PASS (12 operations added, none removed) |
| Web `format:check`, `lint`, `typecheck`, `test`, `build` | PASS: 366 web tests |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2 --retries=0` | **72/72 ×2** on `c7f266c`; after the review follow-up, `qr.spec.ts` 3/3 ×2 and a fresh-stack full run 72/72; API log 0 × 5xx, 0 × 429 |
| Linux Chromium container (`qr`, `admin-operations`, `shop-dashboard`, `public-discovery`) | 19/19, before and after the review follow-up |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` and `git` | PASS (`git`: 62 commits after `c7f266c`, no leaks) |

## Database and migrations
- Created and applied locally: `20260930111559_Qr`, on the compose volume and from empty in the integration tests.
  - It adds the `qr` schema: `qr_code_links`, `qr_code_routes`, `qr_visits`.
  - It adds `bookings.qr_link_id` (composite FK) and `qr_visit_id`, with a filtered index.
- No production migration was run.

## Decisions added
- D-114: QR codes, scans and booking attribution (the model, route table, visits without IP, the cookie, attribution rules, analytics definitions, files and poster, shop read-only access, local compose caveat).
- D-115: locale negotiation on every page path (the proxy matcher fix).
- Design deviations: DV-A14 applied.

## Known issues or blockers
- **Local visitor hashes collide.** In local compose, every browser shares the web container's address at the API. Production needs Nginx forwarding and `KnownNetworks` (Phase 17, D-094).
- **EF warnings in the API log, not from Phase 16:**
  - "Take without OrderBy" from Phase 15's notification sweep job;
  - "multiple collection includes" from Phase 08's admin subscription read.
  - Phase 17 can tidy both.
- **Carried over:**
  - production Nginx for `/hubs` and client IP forwarding;
  - a backplane for the hubs;
  - a dead-letter replay screen;
  - at most five E2E runs per stack per hour for each demo number (OTP);
  - "any professional" does not retry;
  - packages across professionals (D-020);
  - grace days and limits (D-077);
  - oversized uploads through the rewrite.

## Exact next action
1. Push when the user asks: `git push origin main`. CI runs the 72-test E2E suite.
2. Start Phase 17 (`phases/phase-17-hardening.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*QrTests"`
   - `cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test flows/qr.spec.ts`
3. Phase 17 should include:
   - the Nginx example forwarding the client address (QR visitor hashes, rate limits);
   - cache headers for QR images;
   - the two EF warnings above;
   - D-115's side effect: page responses now carry `Set-Cookie: NEXT_LOCALE`, which blocks shared caching of public HTML.

## Files intentionally left modified
- None.
