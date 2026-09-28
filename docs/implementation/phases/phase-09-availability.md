# Phase 09 — Schedules & availability engine

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
Shops manage their opening hours, closures, each professional's working hours, breaks, time off and vacations, and can pause or resume online booking. The API computes genuinely bookable slots for a service, a professional (or any professional) and a date.

## Prerequisites
Phase 08 complete (settings and bookability).

## In scope
- **Entities** (all `IShopOwned`, with concurrency tokens):
  - `ShopOpeningHour`: weekday, one or more intervals, may cross midnight.
  - `ShopClosure`: date range with a reason.
  - `ProfessionalWorkingHour`: weekday intervals.
  - `ProfessionalBreak`: recurring weekly or a one-off date, with a time range.
  - `ProfessionalTimeOff`: date/time range, type (vacation/sick/other).
  - Shop `OnlineBookingPaused` flag, with reason and timestamps.
- **Availability domain service (pure, clock-injected).** Inputs:
  - shop time zone
  - opening hours and closures
  - paused flag and bookability
  - professional hours, breaks and time off
  - service or package duration
  - existing non-cancelled appointments (via a port; the Booking module supplies them in Phase 10, and a fake is used here)
  - lead time, horizon and slot step (5 min default)

  Output: bookable slot start instants (UTC) with local labels, grouped by period.
- **Any professional (D-012):** the union of eligible professionals, each slot carrying its candidate set.
- **Unit tests** (R-AVL-01/02): opening hours ∩ working hours; breaks; time off spanning days; closures; intervals crossing midnight; horizon and lead time edges; slot step 5/10/15; service longer than the remaining window; past times excluded; `Asia/Riyadh` (no DST) plus one DST zone to prove correctness. Property-based tests are optional.
- **API:**
  - Shop: `/shop/schedule/opening-hours`, `/shop/schedule/closures`, `/shop/professionals/{id}/working-hours|breaks|time-off`, and `POST /shop/online-booking/pause|resume`. The shop may manage schedules for its own professionals (spec §7).
  - Public: `GET /public/shops/{slug}/availability/dates?serviceId&professionalId&from&to` (day-level availability for the date strip) and `GET /public/shops/{slug}/availability/slots?serviceId|packageId&professionalId|any&date`. Rate-limited under `availability`.
- **Conflict preview:** when time off or a break overlaps existing bookings, the API returns the affected bookings (no phone numbers) and flags them. No automatic customer messages (DV-S22). The booking port is stubbed until Phase 10 wires it.
- **Web:**
  - `/shop/schedule` (s-hours): weekly hours editor, professional working-hours editor (DV-A03), break/time-off dialogs with conflict preview (DV-A04), closures, and a pause/resume toggle with confirm.
  - Professional schedule visibility.
- **Seed:** hours, breaks and time off for the demo shops and professionals.
- **Docs:** `docs/availability-and-booking.md`, availability section.

## Explicitly out of scope
Booking creation (Phase 10) and the customer slot UI (Phase 12).

## Checklist (100 points)
- [x] 9.1 (5) Re-validate and refine the checklist. Subscriptions integration 8/8 and E2E `flows/subscriptions.spec.ts` 3/3 on the committed Phase 08 code. The design review settled the building-block ports, the pause (first on the shop row, then moved to its own row, D-083), the engine semantics (instants, midnight, edges) and the gate order before any code (D-082…D-084).
- [x] 9.2 (12) Schedule entities, migration `Schedules`, tenant filters and composite FKs. `shop_opening_hours` and `professional_working_hours` (the week as JSON), `shop_closures`, `breaks` (professional optional), `professional_time_off`, all shop-owned with `xmin`. CHECKs cover ranges, minutes and weekly-xor-once. The pause is its own row, `shops.online_booking_pauses` (D-083).
- [x] 9.3 (22) `AvailabilityEngine` (pure, clock injected), with 22 engine tests and 15 domain tests. They cover:
  - hours ∩ working hours, breaks (weekly and one-off), time off across days, bookings (back-to-back allowed), closures of the business day, windows past midnight (both closure cases), an item longer than the window;
  - lead time and horizon at their exact edges, steps 5/10/15, a grid from local midnight (08:05);
  - DST: New York spring gap and autumn repeat on real instants; Riyadh has 24-hour days;
  - `IsBookable` for the Phase 10 recheck; week validation (overlap past midnight, Saturday into Sunday); instant-set algebra.
- [x] 9.4 (8) Any-professional candidates. Each slot lists every active, assigned professional free for the whole item; for a package, only professionals assigned to every item. Tested in the unit suite (candidate sets, a slot nobody can take) and in integration (package, unassigned or disabled professional). The tie-break stays in Phase 10 (D-012).
- [x] 9.5 (10) Shop schedule API, pause/resume and tests (R-AVL-03):
  - staff read but cannot write or pause (403);
  - a stale version is 409;
  - shop B gets 404 on every verb for shop A's closure, break and time-off ids and for shop A's professional (hours, time off, break, preview), and nothing changes;
  - pausing twice is 409; pause and resume are audited.
- [x] 9.6 (8) Public `dates` and `slots` (anonymous, rate limit `availability`, allow-listed in the authorization matrix):
  - the gates: 404 unknown or suspended shop, 200 `bookable:false` for `subscription.none` and `shop.paused`, 404 unpublished or foreign item, 422 not online-bookable or not eligible, 400 bad query or range over 31 days;
  - only bookable slots, including existing bookings through a fake `IBookedTimeReader`.
- [x] 9.7 (15) `/shop/schedule` (s-hours, DV-A03/A04):
  - the weekly hours grid (split shifts, past-midnight "next day" hint, LTR time inputs);
  - professionals' hours ("follows the shop's hours");
  - break, time-off and closure dialogs with a conflict preview and "save anyway" (DV-S22);
  - the pause card with a confirmation and a note;
  - read-only for staff; the nav entry needs `Shop.Schedule.Read`.
- [x] 9.8 (5) Seed:
  - Al Asala hours as designed; Barber House open past midnight on Thursday; Sultan's own hours;
  - Asr, lunch and Maghrib breaks;
  - a National Day closure; Majed's current and Rakan's upcoming time off.
  - Integration `DemoSeed_GivesTheDemoShopsHoursBreaksAndBookableSlots`.
- [x] 9.9 (5) `docs/availability-and-booking.md`, availability section; README seed note.
- [x] 9.10 (10) Phase gates, the E2 schedule-part E2E (`flows/shop-schedule.spec.ts`), control files and commit.

## Files/modules expected to change
`src/Modules/Availability/**`, `src/Modules/Shops/**` (pause), `src/Modules/Services/**` (catalogue port), `src/Modules/Professionals/**` (directory), `src/Modules/Subscriptions/**` (bookability), `src/BuildingBlocks/**` (contracts), web `/shop/schedule` and `components/schedule/*`.

## Data model and migration impact
- Schema `availability`.
- Migration `Schedules` (`20260928101655`): the five tables above, and `shops.online_booking_pauses`. The phase plan named it `0007_Schedules`; the repo uses timestamped names.

## API contracts and UI routes
See D-084 and `docs/availability-and-booking.md`. Route: `/shop/schedule`.

## Security, tenancy, privacy, RTL, a11y, responsive
- Public availability exposes no professional contact data and no booking details.
- The conflict preview's appointment shape has no phone field. The integration test checks that the response contains neither "phone" nor "+966", and `ShopFacingContractTests` covers every new shop endpoint automatically.
- Time inputs are LTR-isolated. Ranges follow the page direction.
- axe shows 0 serious violations on `/en/shop/schedule` (fixing one found on the closed-day rows, DV-T11). There is no horizontal overflow at 390 px in Arabic.

## Tests and verification commands
Smallest decisive re-validation (for the next session):
```
dotnet test --project tests/Trimme.UnitTests -c Release --filter-namespace "*Availability"
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Availability"
E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 pnpm -C tests/E2E exec playwright test flows/shop-schedule.spec.ts
```

## Acceptance criteria
- All availability unit tests pass.
- Paused or unbookable shops return no slots.
- The shop UI edits persist and show up in the slots response (E2E through the UI, then the public API).

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
Session 5, 2026-09-28.

**What was built**
- **Availability module:**
  - domain: schedule entities, `ScheduleRules`, and the engine (`ShopClock`, `InstantSet`, `AvailabilityEngine`);
  - application: the shop schedule use cases with the conflict finder, and public dates/slots;
  - persistence, the empty `IBookedTimeReader`, and `DemoSchedulesSeeder`.
- **Contracts:** `IBookableOfferCatalog` (implemented by Services), `IBookedTimeReader`, `IProfessionalDirectory.ListByShopAsync`, and `ShopSummary` with the time zone and pause.
- **Shops:** pause/resume endpoints and audit. **Subscriptions:** `IShopBookability` returns `shop.paused`.
- **Web:** `/shop/schedule` with `components/schedule/*` (`time.ts`, `HoursEditors`, `ScheduleEntries`, `PauseCard`), ar/en messages, and new validation and API error keys.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration | PASS — 243 / 63 / 119 (Phase 08 end: 206 / 63 / 114). New: 37 unit (`Availability/*`) and 5 integration (`Availability/ScheduleTests`) |
| Integration repeat runs | 119/119 on 5 of 6 full runs (the final one after the pause change, with the full log kept). The first full run reported 9 failures, and its details were lost: only the last 60 lines of output were kept. Every later run passed 119/119. This fits the Phase 07/08 timing watch item, but is **not proven**; the next session should keep full logs |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Upgrade on the Phase 08 compose volume | PASS — migrate applied `Schedules` (8 migrations), 7 seeders ran, public dates returned slot counts (Saturday 0) |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — 229 web tests (12 new) |
| Fresh `down -v` + `up --build`, `pnpm e2e` ×3 | PASS — final stack (fresh, the last code): **47/47, 47/47, 47/47** (cold first run included). Earlier fresh stacks failed on real defects that are now fixed (see Found and fixed): 46/47 ×2 + 47 (form remount), 47, 46/47, 47 (edit reset on refresh), 45/47 (load timing + a Phase 06 sign-in under the same load), 47, 47, 46/47 (pause made the profile stale) |
| No-transfer grep gate (D-069); R-NEG-08 grep gate | PASS — hits only in excluded tests, Seeding ("إجازة سنوية") and Geocoding (rerun on the committed code) |
| gitleaks `dir` + `git` (v8.30.1, Docker) | PASS — no leaks; rerun on the committed code (33 commits) |
| Visual check `/ar/shop/schedule` at 1440 and 390 | RTL mirrored, two columns on desktop and one on a phone, no overflow. Fixed a mirrored `play` icon and LTR-wrapped time ranges |

**Commit:** `a9a5aca`. A `test:` follow-up added the assertion that a paused shop leaves discovery by default and stays listed (still unbookable) with `HidePausedShopsFromDiscovery` off (D-013/D-083; Availability integration 5/5).

**Found and fixed while verifying**
- **Missing toast provider.** The pause card used the toast, but the dashboard has no `ToastProvider`, so the page failed to render on the stack. The unit test had wrapped its own provider and hid this. The card now shows an inline status.
- **Colour contrast.** axe found "مغلق" at 55% opacity on closed days (DV-T11).
- **Forms remounted after a save (found by E2E).** The hours forms were keyed by their version, so the refresh after a save remounted them. That erased the "saved" message (runs 1 and 2 of the first fresh stack failed on it; run 3 passed on the race), and a second save before the refresh arrived would send a stale version. The first fix (stay mounted, follow the server's version) exposed a second race on the next fresh stack (run 2): an edit made before the refresh arrived was reset to the server's values and then saved. The forms now stay mounted, send the version their last save returned, and take the server's values only when the version differs from that one (someone else changed it). Two web regression tests fail on each earlier behaviour and pass now.
- **Pause made the profile form stale (found by E2E).** In the final fresh-stack runs, the Phase 06 profile flow failed once (409, logged as 500) while the schedule flow paused the same shop. The pause lived on the shop row, so it changed the row version. It now has its own row (D-083), the uncommitted `Schedules` migration was regenerated, and an integration assertion checks that pausing keeps the profile version.
- **Load timing.** Under the parallel E2E load one API call took up to 3.4 s. The schedule flow's dialog saves (preview, save and refresh) now wait up to 15 s. The other flows are unchanged (watch item).
- **Placeholder parity.** The English plural message tripped the ICU parity check, and was reworded.

## Remaining risks → next phase
- **Performance.** Slot computation over the horizon has no cache between requests (a 31-day request stays in memory, one load per request).
- **Phase 10:**
  - replace the empty `IBookedTimeReader` (`services.Replace`);
  - call `AvailabilityEngine.IsBookable` in the booking transaction, through a building-block contract exposed by Availability;
  - apply the D-012 tie-break;
  - **Walk-ins need a collision-only check.** `IsBookable` applies the whole online policy (lead time, horizon, the 5-minute grid). Online create and reschedule should use it. A walk-in (spec §11) needs the same collision checks (hours, breaks, time off, bookings) but not that policy: a walk-in "now" or at 10:02 must be allowed. Add a collision-only check next to it; do not reuse `IsBookable` for walk-ins.
  - **Shared demo data in E2E.** `flows/shop-schedule.spec.ts` briefly pauses Al Asala, rewrites Faisal's hours and asserts exact slot lists. Seeded sample appointments (spec §20) or booking flows on Al Asala/Faisal would change those counts or hit the pause mid-run. Give the booking flows their own professional or date, or relax those assertions.
  - persist the "flagged" state for bookings affected by time off, breaks or closures;
  - assert "future bookings untouched" for pause and expiry.
- **Phase 11:** discovery hides paused shops through `IShopBookability.VisibleInDiscovery` (D-013).
- **Phase 12:** the customer date strip and slot grid use `dates` / `slots` (grouped by `period`).
- **Watch item:** the unexplained 9-failure integration run (see above) and the earlier timing watch items.
- **Next:** Phase 10 — Booking core.
