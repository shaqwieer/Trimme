# TRIMME Session Handoff

- **Updated:** 2026-09-28 (end of Session 5: Phases 09 and 10)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). Phase 09 is pushed and CI-green (run 36411900841). Phase 10 is committed locally and **not pushed**; push only when the user asks.
- **HEAD commit:** a `fix:` follow-up on top of `41007e6` (`feat: phase 10 booking core and integrity`). Run `git log --oneline -3`.
- **Working tree:** clean after the commit.
- **Local Docker stack: running.** It was recreated from an empty volume with the Phase 10 images (9 migrations, 9 seeders): web 3300, API 8080, DB 5434, Mailpit UI 8325.
- **Current phase:** 10 is complete. Phase 11 has not started.
- **Phase score:** 100 / 100 (Phase 10). Phase 09 was also 100 / 100 this session.
- **Last fully completed phase:** 10, booking core and integrity.

## Completed this session
- **Phase 09:** schedules and the availability engine (see its phase file).
- **Phase 10** (`phases/phase-10-booking-core.md`):
  - **Bookings.**
    - Online bookings (`POST /bookings`, `Idempotency-Key` required), reschedule, cancel (cutoff), walk-ins, shop transitions with time rules, notes, admin read and cancel (audited).
    - Customer views, including `.ics`. Shop views have `allowedTransitions` and an `outsideSchedule` flag.
  - **Integrity (D-089).** One transaction per command: idempotency claim, recheck through `IAvailabilityChecker`, write, outbox row.
    - The exclusion constraint `ex_bookings_professional_overlap` guarantees no double booking.
    - A lost race (exclusion violation or deadlock) is 409 `booking.slot_unavailable`.
  - **Customer isolation in the data layer (D-085).** `ICustomerOwned`/`ICurrentCustomer`: a customer reads and changes only their own bookings. Customer use cases read the booked shop through the public scope.
  - **Snapshot and model (D-086).** Item, price, duration, package items, professional and customer names; the payment seam (`PaymentStatus = NotApplicable`, `AmountDue`); a reference; owned history; notes.
  - **Rules (D-087, D-088).** The D-016 machine. Arrived from 60 min before the start; NoShow after it; a cancel reason is required. The customer cutoff. Walk-ins check collisions only, at any minute; "now" gives Arrived. "Any professional" picks the fewest bookings that day, then the id.
  - **Seed:** demo customers (Noura `+966500100301`, Khalid `+966500100302`) and sample appointments (completed, no-show, cancelled, two upcoming on real free slots; none for Faisal).
  - **Docs:** the Bookings sections of `docs/availability-and-booking.md` (Mermaid lifecycle, no double booking, idempotency, outbox, payment seam), domain model, README.

## Verification evidence (Phase 10)
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS, 349 / 63 / 133 (106 new unit, 14 new integration) |
| Concurrency suite ×20 | PASS, 20/20 runs (4 tests each), then 5/5 after the partial-overlap assertion was tightened to exactly one winner. Local only; CI runs it once |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web gates (`lint`, `typecheck`, `format:check`, `openapi:check`, `test`, `build`) | PASS, 229 web tests (no web code changes; schema regenerated) |
| Fresh `down -v` + `up --build` + `pnpm e2e` ×3 | PASS, **47/47 ×3** (the first compose build hit a transient Docker BuildKit snapshot error; the rebuild succeeded) |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` + `git` | PASS, no hits outside the exclusions, no leaks |

## Database and migrations
- Created this session: `Schedules` (Phase 09) and `Bookings` (`20260928111635`, Phase 10). `Bookings` adds `bookings.bookings`, `booking_history` and `booking_notes`; `infra.outbox_messages` and `infra.idempotency_records`; the exclusion constraint and the same-shop service/package keys (SQL).
- Applied locally only: Testcontainers databases and the compose volume.

## Decisions added
- **Phase 09:** D-082…D-084.
- **Phase 10:**
  - D-085: customer-owned rows;
  - D-086: booking model;
  - D-087: state machine, time rules and policy;
  - D-088: recheck, walk-ins and any professional;
  - D-089: transaction, exclusion constraint, idempotency and outbox.
- D-012 and D-015 are now Accepted. DV-S08, DV-S10 and DV-S18 are applied on the API side.

## Known issues or blockers
- **Phase 10 is not pushed.** When the user asks, push and confirm CI is green, then record the run in the Phase 10 file and MASTER_PLAN.
- **Not in v1 / later phases:**
  - packages across professionals (D-020);
  - "any professional" does not retry another candidate after losing a race;
  - outbox processing and idempotency-record purging (Phase 15 jobs).
- **Shared demo data in E2E.** `flows/shop-schedule.spec.ts` briefly pauses Al Asala and rewrites Faisal's hours. Booking E2E flows (Phase 12) should use another professional or relax the exact slot assertions.
- **Watch item:** one earlier full integration run had 9 failures without kept details; every later run passed. Keep full logs.
- **Earlier carry-overs:**
  - Grace/trial days and plan limits are stored but not enforced (D-077).
  - Serilog logs handled 400/409 as 500 (Phase 17).
  - An upload over the size limit gets a bare 500 through the Next.js rewrite (Phase 17).
  - Drag-and-drop reordering is deferred.
- **Ports:** 8025 is taken on this machine. Use `TRIMME_MAILPIT_PORT=8325` and `E2E_MAILPIT_URL=http://localhost:8325`.

## Exact next action
1. If the user asks, push Phase 10 and confirm CI.
2. Start Phase 11 (`phases/phase-11-public-discovery.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Bookings"`
   - the concurrency loop in the Phase 10 file (a few runs)
3. Phase 11 should hide shops through `IShopBookability.VisibleInDiscovery` (subscription and pause) and read slot counts from `availability/dates`.

## Files intentionally left modified
- None.
