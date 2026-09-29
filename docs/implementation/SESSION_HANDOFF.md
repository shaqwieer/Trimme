# TRIMME Session Handoff

- **Updated:** 2026-09-29 (end of Session 8: Phase 13)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Phase 11 is pushed and CI-green (run 36543760813).
  - Phases 12 (`f8fa5be`, `11d76ef`, `c8a2c00`) and 13 (`6448b1c`, `77dcc1c`, `031aebc`) are **committed locally and not pushed**. Push only when the user asks, then record the CI run in the phase files.
- **HEAD commit:** a `docs:` commit recording the Phase 13 hashes, on top of `031aebc` and `77dcc1c` (review follow-ups) and `6448b1c` (Phase 13). Run `git log --oneline -6`.
- **Working tree:** clean after the commit.
- **Local Docker stack: stopped** (containers kept; `docker compose -f infra/docker-compose.yml start` brings it back). It was recreated from an empty volume with the final Phase 13 images and E2E ran twice on it.
  - Each demo customer has used 2 of its 5 OTP codes this hour; Sara has two fewer reviewable visits than seeded.
  - Ports: web 3300, API 8080, DB 5434, Mailpit UI 8325; start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
- **Current phase:** 13 is complete. Phase 14 has not started.
- **Phase score:** 100 / 100 (Phase 13).
- **Last fully completed phase:** 13, shop operational dashboard and live updates.

## Completed this session
Phase 13 (`phases/phase-13-shop-dashboard.md`):
- **Live updates** (D-099):
  - The hub is `/hubs/operations`: cookie auth; the server picks the group (`shop:{id}` or `admins`); no client methods; closes with the session; allowed origins only.
  - Events come after commit from the booking outbox via an EF interceptor, and carry ids, times and status only.
  - The web provider reconnects after a session refresh, backing off across closes (reset after 30 s connected) and stopping only when the server refuses the refresh (401/403); there is a live indicator.
  - Verified end to end through the Next.js rewrite on all three transports.
- **Board API** (D-100):
  - the business-day plan (`IShopDayPlanReader`, engine `DayOf`);
  - `GET /shop/dashboard/overview` (KPIs, next bookings, minutes-based load, last seven days by hour);
  - `GET /shop/calendar` (one to seven days, lanes, bookings; the hours after midnight stay in their day);
  - `GET /shop/availability/walk-in` (free now, next free, the day's starts);
  - `GET /shop/bookings` with repeated `status` and chip counts.
- **Web:**
  - `/shop` overview; `/shop/calendar` (minute-accurate day view with a walk-in shortcut, week view with the density strip);
  - `/shop/appointments` (chips with counts, filters, search, table and cards, the drawer with allowed transitions, optimistic rollback, notes, history, no phone);
  - `/shop/walk-in` (no phone field); pause and subscription banners on every shop page.
- **Compose:** the API's web origin (CORS, emails, hub origins) follows `TRIMME_WEB_PORT`.
- **Tests:**
  - integration `ShopDashboardTests`, `RealtimeTests`, `OpenApi_has_no_export_surface`;
  - unit `DayOf_…`;
  - web `lib/shop/board.test.ts`, `components/shop/board/board.test.tsx`, `components/shop/live/OperationsLive.test.tsx`, `lib/api/client.test.ts`;
  - E2E `flows/shop-dashboard.spec.ts` (E2 run by staff with the owner watching, plus layouts).

## Verification evidence
| Command | Result |
|---|---|
| `dotnet build -c Release` | PASS, 0 warnings |
| Unit / architecture / integration | PASS, 378 / 63 / 152 (the full integration suite ran alone) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes (no migration this phase) |
| Web gates (`lint`, `typecheck`, `format:check`, `openapi:check`, `test`, `build`) | PASS, 299 web tests at `6448b1c`, 313 after the follow-ups |
| Fresh `down -v` + `up --build` + `pnpm e2e` | After fixing a duplicated subscription warning and lightening load: run 1 61/62 (a Phase 04 admin invitation navigation timed out on the cold stack), **runs 2–4 62/62 ×3**. After the follow-up, on a new empty volume: **62/62 ×2**. 0 HTTP 429. The only 5xx log lines are the settings validation 400s logged as 500 (the earlier "0 5xx" used a pattern that never matched; see the Phase 13 correction) |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` and `git` | PASS, no hits, no leaks |

## Database and migrations
- None this phase (the `(shop_id, starts_at)` booking index from Phase 10 serves the board).

## Decisions added
- D-099: live operations updates over SignalR (hub, groups, origin check, after-commit events, contract, transports, matrix scope).
- D-100: business days, overview KPIs and load, calendar day and week views (refines D-034), list chips, walk-in options.
- Design deviations: DV-S08, S18, S19 and A19 applied. New DV-C08: drawer side, deferred buttons, header CTA, banners, overview additions, calendar walk-in shortcut, walk-in time picker.

## Known issues or blockers
- **Click-then-navigate E2E timings under load.** Assertions like `toHaveURL` after a click can exceed 5 s on a cold stack while the whole suite runs in parallel. Seen once each in: Phase 04 admin invitation, Phase 05 tenancy, Phase 06 shop owner, Phase 07 services toggle, Phase 08 E5. Every one passes alone and in later runs; API latency stays low (p95 < 150 ms), so the web server is the pressure point. Consider `loading.tsx` or streaming for dashboard pages, or fewer workers, in Phase 17/18.
- **Production:**
  - Nginx must forward `/hubs` with the WebSocket upgrade headers and must not buffer SSE;
  - `Web:PublicBaseUrl` must be the site origin (the hub's origin allow-list);
  - SignalR scale-out needs a backplane (Phase 17).
- **Revocation lag.** A revoked session or a suspended shop keeps receiving live events until its access cookie expires.
- **E2E per hour.** At most five E2E runs per stack per hour (the demo customers' OTP limit). Sara's review visits expire seven days after seeding.
- **Earlier carry-overs:**
  - "any professional" does not retry;
  - packages across professionals (D-020);
  - outbox processing and resend confirmation (Phase 15);
  - grace days and limits (D-077);
  - Serilog 400/409-as-500 (e.g. `PUT /admin/settings` validation, once per E2E run) and oversized uploads (Phase 17).

## Exact next action
1. If the user asks, push `main` and record the CI run in the Phase 12 and 13 files.
2. Start Phase 14 (`phases/phase-14-admin-dashboard.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*RealtimeTests"`
   - `cd tests/E2E && E2E_BASE_URL=http://localhost:3300 npx playwright test flows/shop-dashboard.spec.ts`
3. Phase 14 can reuse:
   - the `admins` hub group (admins already receive every booking change);
   - `AppointmentDrawer`, `lib/shop/board.ts` and the calendar pieces for admin booking views;
   - `GET /admin/bookings` (Phase 10) and `AdminBookingResponse`.
   Keep Faisal free (schedule E2E); E2 uses Omar two days ahead.

## Files intentionally left modified
- None.
