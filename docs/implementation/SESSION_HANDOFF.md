# TRIMME Session Handoff

- **Updated:** 2026-09-29 (Session 9: CI run #17 follow-up after Phase 13)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Pushed: Phases 12 and 13 up to `32619ba` (CI run #17: web, backend and gitleaks green; compose + Playwright **failed** 3 tests, fixed below), then the user's landing redesign `41ac0a0` (CI run #18 **cancelled**).
  - **Local and not pushed:** `9a7450c` (CI #17 fixes) and `3d2d99e` (landing follow-up and evidence), plus this docs commit. Push only when the user asks, then record the CI run in the Phase 13 file.
- **HEAD commit:** this `docs:` commit on top of `3d2d99e` and `9a7450c`. Run `git log --oneline -6`.
- **Working tree:** clean after the commit. `next dev` re-creates untracked `apps/web/AGENTS.md` and `apps/web/CLAUDE.md` each time it starts (Next.js agent rules); they were deleted, and whether to disable, ignore or commit them is the user's call.
- **Local stack: running, with the web app in dev mode.** Compose `postgres`, `migrate`/`seed` (done), `api` and `mailpit` are up on a volume created this session. The compose `web` container is **stopped**, and `next dev --port 3300` (a background task of Session 9) serves http://localhost:3300 with `TRIMME_API_INTERNAL_URL=http://localhost:8080`. `/api` and `/hubs` go through its rewrites (anonymous negotiate answers 401). To go back: stop `next dev`, then `docker compose -f infra/docker-compose.yml start web`.
  - Three full E2E runs used this hour's demo OTP codes on earlier volumes; the current volume has had one full run.
  - Ports: web 3300, API 8080, DB 5434, Mailpit UI 8325; start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
- **Current phase:** 13 is complete. Phase 14 has not started.
- **Phase score:** 100 / 100 (Phase 13).
- **Last fully completed phase:** 13, shop operational dashboard and live updates.

## Completed this session (Session 9)
CI run #17 follow-up (details and proofs in `phases/phase-13-shop-dashboard.md`, "CI run #17 follow-up"):
- **Favorites heart after a fast sign-in:** only the request in flight is shared now (D-098); a regression unit test fails on the old component.
- **Grey text contrast:** `text-secondary` `#647484` → `#5F6F80` (4.35:1 on `bg-muted` before; D-039, DV-T12); `tokens.test.ts` covers the grey surfaces.
- **axe mid-transition:** E2E browsers emulate `prefers-reduced-motion: reduce` (a clicked slot measured 2.37:1 mid-fade).
- **Landing redesign (`41ac0a0`, committed by the user during the session):** step numerals `brand-150` → `brand-600` (DV-T13); the locale and discovery specs expect the new headline. The Phase 11 landing checks still hold.

## Completed in Session 8
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
| Session 9: web `lint`, `typecheck`, `format:check`, `test`; E2E `typecheck`, `format:check` | PASS, 318 web tests |
| Session 9: fresh `down -v` + `up --build`, full suite `CI=1 --workers=2` | **62/62**, no retries (before the landing fixes: 57/62, the 5 landing failures) |
| Session 9: CI | Not yet confirmed: the fixes are not pushed |

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
1. If the user asks, push `main` and record the CI run (expected: all four jobs green) in the Phase 13 file.
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
