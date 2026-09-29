# Phase 13 — Shop operational dashboard & live updates

**Status:** [x] · **Score:** 100/100 (Session 8)

## Goal and user-visible outcome
Shop owners and staff run their day from TRIMME:
- operational overview;
- today's appointments;
- day and week calendar;
- appointment list and detail drawer, showing only valid status actions;
- walk-in creation;
- subscription warnings;
- live updates scoped strictly to their own shop.

## Prerequisites
Phase 12 complete.

## In scope
- **Overview KPIs** (s-overview), via `GET /shop/dashboard/overview?date`: the day's appointments (and the previous day's), pending from now on, completed, no-shows, cancellations, free capacity; each professional's minutes-based load; the last seven days by hour (D-100).
- **Calendar** `/shop/calendar?view=day|week&date&professional`:
  - Day view: minute-accurate per-professional columns (DV-S19), with off-shift, breaks and time off shaded; free half-hours open a prefilled walk-in.
  - Week view: seven day columns with the bookings (lanes when they overlap) plus the density strip (D-034 as refined by D-100, DV-A19).
  - Business days: the hours after midnight belong to the window's day (D-100).
- **Appointments** `/shop/appointments`: every status chip with counts (DV-S08), date range, professional, search by name or reference only (DV-S18), table on desktop and cards on phones, paging; the detail drawer (`?booking=`) with snapshot, history, internal notes and "رقم العميل غير متاح"; actions from `allowedTransitions` only, cancel with a reason, optimistic with rollback.
- **Walk-in** `/shop/walk-in` (s-walkin): service or package → professional (free now, next free time) → time ("start now" per D-035, or the day's free starts) → name and optional note → record. There is no phone field. `GET /shop/availability/walk-in` gives the choices.
- **SignalR** `/hubs/operations` (D-099):
  - cookie auth; the group is chosen by the server from the session (`shop:{id}` or `admins`), and there are no client methods;
  - allowed origins only; the connection closes with the session and the client reconnects after a refresh;
  - events come after commit from the outbox via an EF interceptor, and carry ids, times and status only.
- **Subscription and pause banners** on every shop page (the subscription page keeps its own warning).
- **Mobile:** the existing drawer navigation; tables become cards; the calendar scrolls one professional column at a time on phones.
- **E2E:** E2 (services in the Phase 07 flow; walk-in, live update on a second screen, calendar, drawer note and cancel, no foreign access, network payload phone scan, no export) and a layout check at three widths.

## Explicitly out of scope
The shop notifications inbox (Phase 15). "Resend confirmation" arrives with WhatsApp (Phase 15). "طلب تواصل" is deferred (D-023).

## Checklist (100 points)
- [x] 13.1 (5) Re-validate; refine checklist.
  - Phase 12: `CustomerAccountTests` 4/4, `CustomerAccountDomainTests` 4/4, booking web tests 28/28, and the customer-booking E2E 5/5 on the running stack.
  - Design settled with the advisor:
    - after-commit outbox interceptor;
    - hub groups from the session;
    - a transport spike first;
    - business-day rules; minutes-based load; a real week view (D-099, D-100).
- [x] 13.2 (10) Overview KPI queries, tests and page. Integration `Overview_CountsTheBusinessDay…`; the page shows the identity header, KPIs, the next bookings, load meters and the hourly chart, and refreshes live.
- [x] 13.3 (16) Calendar day and week:
  - the day plan contract and engine `DayOf` (unit test);
  - `GET /shop/calendar` (integration `Calendar_…AfterMidnight…`);
  - the minute-accurate day view, week view and density strip;
  - web tests for positioning, lanes and axis.
- [x] 13.4 (14) Appointments list, drawer, allowed transitions and optimistic rollback. Multi-status filter and chip counts (integration). Web tests: allowed actions only, optimistic, rollback on 409, cancel reason, read-only staff.
- [x] 13.5 (10) Walk-in flow. Options endpoint (integration). Web test: no phone field, "start now" payload and idempotency key, conflict refresh. E2E walk-in.
- [x] 13.6 (14) SignalR hub, tenant-scoped groups, client, and the isolation and phone tests.
  - Spike through the compose rewrite: WebSockets, SSE and long polling all deliver.
  - Integration `RealtimeTests` 4/4: non-vacuous isolation, admins, no group join, customer and anonymous refused, origins over HTTP and WebSocket.
  - Live indicator and reconnect loop.
- [x] 13.7 (5) Pause and subscription banners on every page (web test), responsive cards and the calendar column scroll.
- [x] 13.8 (14) E2E E2, including a phone scan of network payloads and live frames, a second-screen live update, and the no-export assertion. OpenAPI `OpenApi_has_no_export_surface`.
- [x] 13.9 (12) Axe (overview, walk-in, appointments, drawer, calendar day and week), three widths without horizontal scroll, gates, control files, commit.

## Files/modules changed
- **Building blocks:** `Application/Realtime` (event, projector, publisher contracts); `Application/Scheduling` (`IShopDayPlanReader`, day plans); `Infrastructure/Persistence/OperationsEventsInterceptor`; `Web/Realtime/OperationsHub` (hub, publisher, origin check, setup).
- **Availability:** engine `OpenWindows`/`DayOf`, `ShopDayPlanReader`, walk-in options (`GET /shop/availability/walk-in`).
- **Bookings:** `ShopDashboard` (overview, calendar, business days), multi-status list with counts, `BookingOperationsProjector`.
- **API host:** SignalR registration and mapping, origin middleware. **Compose:** the web origin follows `TRIMME_WEB_PORT`.
- **Web:**
  - `components/shop/live/OperationsLive` (provider, hook, refresh, indicator), `components/shop/board/*` (drawer, list, calendar, walk-in, booking row), `ShopBanners`, `ShopFrame` (providers, banners, parallel reads);
  - `lib/shop/board.ts`; `lib/api/client` (`refreshSessionOutcome`, follow-up); pages `/shop`, `/shop/calendar`, `/shop/appointments`, `/shop/walk-in`; `@microsoft/signalr`.
- **Tests:** integration `ShopDashboardTests`, `RealtimeTests`, `OpenApi_has_no_export_surface`; unit `DayOf_…`; web `board.test.ts`, `board.test.tsx`, `OperationsLive.test.tsx`, `client.test.ts` (follow-ups); E2E `flows/shop-dashboard.spec.ts`.

## Data model and migration impact
None. The `(shop_id, starts_at)` booking index already serves the dashboard queries (Phase 10).

## API contracts and UI routes
- **API (new):** `GET /shop/dashboard/overview?date`, `GET /shop/calendar?from&to&professionalId`, `GET /shop/availability/walk-in?serviceId|packageId&date` (all shop permissions), and the hub `/hubs/operations` (event `bookingChanged`).
- **API (changed):** `GET /shop/bookings` takes repeated `status` values and returns `counts`.
- **UI:** `/shop`, `/shop/calendar`, `/shop/appointments` (with `?booking=`), `/shop/walk-in` (with `?professionalId&startsAt&date`).

## Security, tenancy, privacy, RTL, a11y, responsive
- **Hub.** Group membership comes from the session only. There are no client-callable methods, so a join request is rejected. A foreign origin gets 403. Customers and anonymous callers are refused.
- **Payloads.** Hub messages and every dashboard payload carry no customer phone or customer id (integration and E2E scans).
- **Tenancy.** Tenant isolation on every new query (the tenant filter plus the session shop). Another shop's booking is 404 (E2E).
- **Accessibility and layout.**
  - The calendar is keyboard-operable (booking blocks and free slots are buttons and links with names).
  - Directional icons mirror in RTL.
  - The drawer is a focus-trapped sheet.
  - Axe is clean on every new page.
  - No horizontal page scroll at 390/768/1440; the calendar scrolls inside its card.

## Tests and verification commands
Smallest decisive re-validation (for the next session):
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*RealtimeTests"
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*ShopDashboardTests"
pnpm -C apps/web vitest run src/lib/shop src/components/shop
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 npx playwright test flows/shop-dashboard.spec.ts
```

## Acceptance criteria
- E2 passes. ✔
- A second shop's session never receives events from the first shop. ✔ (the first message shop B receives is its own)

## Rollback / recovery
Revert the commit. No migration.

## Completion evidence
Session 8, 2026-09-29.

**Re-validation of Phase 12 (before any change).** `CustomerAccountTests` 4/4; `CustomerAccountDomainTests` 4/4; booking web tests 28/28; `customer-booking.spec.ts` 5/5 on the running stack.

**Transport spike (before the UI, advisor item 1).** A Node SignalR client went through the web port (the Next.js `/hubs` rewrite) with the Barber House owner's cookies, and created a walk-in per transport:
- WebSockets: the event arrived in 58–720 ms;
- SSE: 34–80 ms;
- long polling: 52 ms.

Long polling first failed at connect: the hub read the request's tenant flags, and the endpoint permission handler read the HTTP context, which long polling no longer has when the hub runs. The hub now reads the shop status and the permissions from its own user.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build -c Release` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration | PASS — **378 / 63 / 152** (Phase 12 end: 377 / 63 / 143). New: unit `DayOf_…`; integration `ShopDashboardTests` ×4, `RealtimeTests` ×4, `OpenApi_has_no_export_surface`. The full integration suite ran alone (2 min 11 s) |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes (no migration this phase) |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — **299** web tests (Phase 12 end: 285) |
| Fresh `down -v` + `up --build`, then `pnpm e2e` (first attempt) | Runs 1–3: 60/62, 59/62, 61/62. Two specs failed in 2 or 3 runs: <ul><li>Phase 08 E3 now matched two elements, because the new subscription banner duplicated the subscription page's own warning (a real regression);</li><li>the Phase 06 shop-owner sign-in did not reach `/shop` within 5 s.</li></ul> The Phase 06 test and the Phase 05 tenancy test (failed once) passed alone. API p95 stayed under 150 ms for every shop endpoint, so the pressure was on the web server. <br>Fixes: <ul><li>no subscription banner on the subscription page;</li><li>`ShopFrame` reads the shop, pause and subscription in parallel;</li><li>the new layout test loads each page once per width.</li></ul> |
| Fresh `down -v` + `up --build`, then `pnpm e2e` (after the fixes) | Run 1: 61/62 — the Phase 04 invitation acceptance did not reach `/admin` within 5 s on the cold stack (admin area, untouched). **Runs 2–4: 62/62, 62/62, 62/62.** 0 HTTP 429, 0 HTTP 5xx |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` | PASS — no hits, no leaks |

**Found and fixed while verifying**
- **Long-polling hub connections were closed at once:** the tenant flags and permission handler were request-bound (see the spike).
- **WebSocket origin check:** it was not enforced under the test server (its own WebSocket feature bypasses the framework allow-list). It is replaced by an explicit origin check on every hub request, tested over HTTP and WebSocket.
- **Anonymous hub start:** it surfaced as a client parsing error, so the test now asserts the negotiate endpoint's 401 directly.
- **Local compose:** the CORS and public origin followed a fixed port 3000; they now follow `TRIMME_WEB_PORT`, otherwise WebSockets from `:3300` would be refused.
- **React 19 lint:** `Date.now()` in render in the calendar; it is taken once in a lazy state initializer.
- **Drawer date line:** it failed axe colour contrast (secondary text on the subtle background); the stronger text colour is used.
- **E2E specifics:** Majed is on seeded time off, so E2 uses Omar two days ahead. Hidden radio inputs are clicked through their labels. Each run uses a unique customer name, because failed attempts could leave walk-ins behind.

**Review follow-up (after commit `6448b1c`, fixed in `77dcc1c` and `031aebc`)**
- **Live reconnect spun (bug).** Every close restarted the backoff at attempt 0. A connection the server drops right after the handshake (a suspended shop, a removed permission) therefore reconnected about once a second, and each attempt refreshed the session first, rotating the refresh token (the auth rate limit is 10 a minute in production).
  - Fix: failures count across closes and failed starts (1 s, 2 s … 30 s). The count resets only after a connection stayed up 30 s. The provider stops (the indicator stays on «إعادة الاتصال…») only when the server refuses the refresh (401/403); the page's own requests then lead to sign-in.
  - Web test `OperationsLive.test.tsx` (mocked SignalR client, fake timers), 5 cases: immediate closes back off 1→2→4→8→16→30→30 s with one refresh per attempt; a stable connection resets to 1 s; failed starts back off; a failed refresh stops; unmount stops. With the old behaviour restored (probe), 3 of the 5 fail.
- **E2 ran as the owner only.** The owner has every shop permission, so the staff role's permissions for the walk-in, the drawer note and the cancel were not exercised in the browser. E2 now performs them as `staff@barber-house`; the owner is the live watcher.
- **Verification.**
  - Web gates PASS: `lint`, `typecheck`, `format:check`, `openapi:check`, `test` (**304**), `build`.
  - Web image rebuilt; `pnpm e2e` on the running stack (previously used this hour): run 1 61/62 (the Phase 03 sign-up did not reach its URL within 5 s just after the restart; `auth.spec.ts` then 6/6 alone), run 2 59/62 (three customer sign-ins answered 429: the demo customers' hourly OTP limit, see Phase 12).
  - Fresh `down -v` + `up --build`, then `pnpm e2e`: **62/62, 62/62**. API log: 0 `responded 429`; the only 5xx lines are the two settings validation lines described in the correction below.
  - gitleaks v8.30.1 (Docker): `git` 45 commits, no leaks; `dir` on the repository, no leaks.
- **A refused refresh and an outage looked the same (`031aebc`).** `refreshSession()` answers false for a network error, 429 or 5xx too, so the first version of the stop rule would have ended live updates for good after a brief API restart. `refreshSessionOutcome()` now separates `expired` (401/403) from `unavailable`; the provider stops only on `expired` and backs off otherwise. `refreshSession()` keeps its boolean result for the other callers.
  - Tests: `lib/api/client.test.ts` (outcome per status and on a network failure, 8 cases); provider "keeps retrying with backoff while the API cannot be reached" (fails when `unavailable` stops the provider, probe).
  - Web gates PASS: `lint`, `typecheck`, `format:check`, `openapi:check`, `test` (**313**), `build`. E2E not rerun: the change is client-only and limited to the live provider's failure branch, which the browser runs do not reach; `refreshSession()` answers exactly as before.

**Correction (Phase 13 follow-up).** The "0 HTTP 5xx" counts in this file were taken with a pattern for JSON logs (`"StatusCode":5xx`), which the plain-text API log never contains, so they were always 0; the 429 counts used the right pattern. Re-counted with `responded 5xx` on the Phase 13 stack, the only 5xx lines are `PUT /api/v1/admin/settings responded 500`, one per full run: the known carry-over where Serilog's request log records a validation failure as 500 while the client receives the 400 (the settings E2E sees "This value is out of range"). Fix planned for Phase 17.

**CI run #17 follow-up (the first CI run with the Phase 12 and 13 E2E).** Web, backend and gitleaks passed. The compose + Playwright job failed 3 tests, each on both attempts, and the local suite still passed 62/62 (fresh `down -v`, `CI=1`, 2 workers). These are CI-only failures; the causes were proven locally:
- **E1 (axe colour contrast at "wizard time", then "reschedule").** axe ran right after a slot was clicked, while `transition-colors` was fading it to navy. With the transition slowed down, the clicked slot measured **2.37:1** (`#b6bec5` on `#6a7986`). The E2E browser now emulates `prefers-reduced-motion: reduce` (`playwright.config.ts`), and the app's reduced-motion rule makes transitions instant.
- **E2 (axe at the walk-in, `.bg-bg-muted`).** The "no phone" note is `text-secondary` `#647484` on `bg-muted` `#f1f4f7`: **4.35:1**, a real AA failure. Locally the note sits below the 900 px viewport (y = 1041), so axe skipped it; with the runner's fonts the page is shorter. The appointment drawer's note and the review form's note (on `bg-subtle`, 4.38:1) had the same pair. `text-secondary` → `#5F6F80` (D-039, DV-T12); `tokens.test.ts` now checks the grey text tokens on `bg-muted`, `bg-subtle` and `bg-app`.
- **Favorites (the heart missing after sign-up).** The hearts reused a settled `GET /me/favorites` answer for 2 s, and the module survives client-side navigation. On a fast runner, guest → sign-up → back to the shop page took under 2 s, so the heart kept the signed-out link. Now only the request in flight is shared (D-098). The unit test "asks again … however fast the return" fails on the old component (probe) and passes now; "hearts that mount together share one request" covers the sharing.
- **Landing redesign (`41ac0a0`, committed during this work).** Its new headline broke the headline assertions in `locale.spec.ts` and `public-discovery.spec.ts`, and its step numerals `01–03` failed axe at 1.18:1 on `/ar` and `/en`, with or without reduced motion. The numerals now use `brand-600` (DV-T13), and the two specs expect the new headlines. The Phase 11 landing checks hold on the new page: real figures, the top-rated shop, canonical, hreflang, Organization and WebSite JSON-LD, and no AggregateRating.
- **Verification.** Web `lint`, `typecheck`, `format:check` and `test` (**318**) PASS; E2E `typecheck` and `format:check` PASS. Fresh `down -v` + `up --build`, then the full suite with `CI=1 --workers=2`: **62/62**, no retries. The CI job itself is confirmed only after a push.

## Remaining risks → next phase
- **Production Nginx** must forward `/hubs` with the WebSocket upgrade headers (and allow SSE without buffering) (Phase 17).
- **Scale-out** needs a SignalR backplane (Phase 17).
- **Revocation lag.** A revoked session or a suspended shop keeps receiving events until the access cookie expires (minutes).
- **Load-sensitive E2E.** The shop pages are heavier; see the evidence for the load-related flakes and their mitigations.
- Next: Phase 14 — Admin operations.
