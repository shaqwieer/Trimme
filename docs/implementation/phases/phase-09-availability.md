# Phase 09 — Schedules & availability engine

**Status:** [ ] · **Score:** 0/100

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
- [ ] 9.1 (5) Re-validate; refine checklist.
- [ ] 9.2 (12) Schedule entities, migration, tenant filters, composite FKs.
- [ ] 9.3 (22) Availability engine + exhaustive unit tests.
- [ ] 9.4 (8) Any-professional candidate resolution + tests.
- [ ] 9.5 (10) Shop schedule API + pause/resume + tests (R-AVL-03) + cross-shop tests.
- [ ] 9.6 (8) Public availability API (dates + slots), rate limits, only bookable slots (R-AVL-02, D-009).
- [ ] 9.7 (15) Shop schedule UI incl. professional hours, dialogs, conflict preview, pause.
- [ ] 9.8 (5) Seed extension.
- [ ] 9.9 (5) Docs: availability section.
- [ ] 9.10 (10) Phase gates, E2 schedule part E2E, control files, commit.

## Files/modules expected to change
`src/Modules/Availability/**`, `src/Modules/Shops/**` (pause), web `/shop/schedule`.

## Data model and migration impact
- Schema `availability`.
- Migration `0007_Schedules`.

## API contracts and UI routes
As listed. Route: `/shop/schedule`.

## Security, tenancy, privacy, RTL, a11y, responsive
- Public availability exposes no professional contact data and no booking details.
- Time pickers are RTL-safe and times are LTR-isolated.

## Tests and verification commands
```
dotnet test --filter Category=Availability
pnpm exec playwright test shop-schedule
```

## Acceptance criteria
- All availability unit tests pass.
- Paused or unbookable shops return no slots.
- The shop UI edits persist and show up in the slots response.

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- Performance of slot computation over the horizon; the cache is scoped per request only.
- Next: Phase 10 — Booking core.
