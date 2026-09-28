# TRIMME Session Handoff

- **Updated:** 2026-09-28 (end of Session 5: Phase 09)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). Phase 08 is pushed and CI-green (run 36333734614). Phase 09 is committed locally and **not pushed**; push only when the user asks.
- **HEAD commit:** `feat: phase 09 schedules and availability engine`. Run `git log --oneline -3`.
- **Working tree:** clean after the commit.
- **Local Docker stack: running.** It was recreated from an empty volume with the Phase 09 images: web 3300, API 8080, DB 5434, Mailpit UI 8325.
- **Current phase:** 09 is complete. Phase 10 has not started.
- **Phase score:** 100 / 100 (Phase 09).
- **Last fully completed phase:** 09, schedules and the availability engine.

## Completed this session
- **Phase 09** (`phases/phase-09-availability.md`):
  - **Schedules** (schema `availability`): shop opening hours and professional working hours (the week as JSON, one row each), closures, breaks (weekly or once; one professional or everyone), time off (UTC instants).
  - **Pause.** Its own row, `shops.online_booking_pauses`; `POST /shop/online-booking/pause|resume` (audited). `IShopBookability` now returns `shop.paused` (D-083).
  - **Engine.** Pure `AvailabilityEngine` (D-082): UTC instants with DST handled, closures of the business day, windows past midnight, exact lead/horizon edges, the 5-minute grid, and any-professional candidate sets. `IsBookable` is ready for the Phase 10 recheck.
  - **API** (D-084):
    - `GET /shop/schedule`, hours and working-hours PUTs, CRUD and preview for closures, breaks and time off;
    - public `availability/dates` and `availability/slots` (anonymous, rate-limited, gated).
  - **Ports:** `IBookableOfferCatalog` (Services) and `IBookedTimeReader` (an empty stand-in until Phase 10).
  - **Web:** `/shop/schedule` (s-hours, DV-A03/A04/A12, DV-S22): hours grid, professionals' hours, break, time-off and closure dialogs with a conflict preview, the pause card; read-only for staff.
  - **Seed:** demo hours, breaks, a National Day closure and time off.
  - **Docs:** `docs/availability-and-booking.md` (availability).

## Verification evidence (Phase 09)
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS, 243 / 63 / 119. Integration was 119/119 on 5 of 6 full runs; the first had 9 failures whose details were lost (output truncated). Watch item |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web gates (`lint`, `typecheck`, `format:check`, `openapi:check`, `test`, `build`) | PASS, 229 web tests |
| Fresh `down -v` + `up --build` + `pnpm e2e` ×3 | PASS — final stack (fresh, the last code): **47/47, 47/47, 47/47** (cold first run included). Earlier fresh stacks failed on real defects that are now fixed (see the Phase 09 file, "Found and fixed while verifying"): 46/47 ×2 + 47 (form remount), 47, 46/47, 47 (edit reset on refresh), 45/47 (load timing + a Phase 06 sign-in under the same load), 47, 47, 46/47 (pause made the profile stale) |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` + `git` | PASS, no hits, no leaks |

## Database and migrations
- Created this session: `Schedules` (`20260928101655`): five `availability` tables and `shops.online_booking_pauses`.
- Applied locally only: Testcontainers databases and the compose volume (upgrade from Phase 08, then fresh).

## Decisions added
- D-082: schedules and engine semantics.
- D-083: the pause as its own row (so the profile version is untouched); one bookability gate.
- D-084: ports and API shape; the paths differ from the phase plan.
- D-012 and D-013 are annotated. New deviations: DV-A12 (pause confirmation) and DV-T11 (closed-day contrast). DV-A03, DV-A04 and DV-S22 are applied.

## Known issues or blockers
- **Phase 09 is not pushed.** When the user asks, push and confirm CI is green, then record the run in the Phase 09 file and MASTER_PLAN.
- **Watch item: integration.** One full integration run had 9 failures with no details kept. Keep full logs: `dotnet test … > it.log`. Earlier timing watch items continue.
- **Phase 10 carry-overs:**
  - replace `NoBookedTime` with the Bookings reader (`services.Replace`);
  - recheck with `AvailabilityEngine.IsBookable` inside the transaction, through a new building-block contract;
  - the D-012 tie-break;
  - persist the "flagged" state for bookings hit by time off, breaks or closures;
  - assert that pause and expiry never touch future bookings.
- **Earlier carry-overs:**
  - Grace/trial days and plan limits are stored but not enforced (D-077).
  - Serilog logs handled 400/409 as 500 (Phase 17).
  - An upload over the size limit gets a bare 500 through the Next.js rewrite (Phase 17 Nginx).
  - Drag-and-drop reordering is deferred.
- **Ports:** 8025 is taken on this machine. Use `TRIMME_MAILPIT_PORT=8325` and `E2E_MAILPIT_URL=http://localhost:8325`.

## Exact next action
1. If the user asks, push Phase 09 and confirm CI.
2. Start Phase 10 (`phases/phase-10-booking-core.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Availability"`
   - `pnpm -C tests/E2E exec playwright test flows/shop-schedule.spec.ts` (with the E2E env vars)
3. Phase 10 should:
   - implement `IBookedTimeReader` in Bookings;
   - expose a building-block contract from Availability (for example `IAvailabilityChecker`, wrapping `ScheduleLoader.CalendarsAsync` + `AvailabilityEngine.IsBookable`), because Bookings may not reference Availability internals, and call it in the booking transaction;
   - gate new online bookings with `IShopBookability` (walk-ins skip the gate).

## Files intentionally left modified
- None.
