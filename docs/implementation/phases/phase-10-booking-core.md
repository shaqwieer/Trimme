# Phase 10 — Booking core & integrity

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
The API can create, reschedule, cancel and transition bookings safely:
- Each booking snapshots the service it was made for.
- An explicit state machine records full history.
- A transactional availability recheck and a PostgreSQL exclusion constraint prevent double booking.
- Commands are idempotent.
- Every change writes an outbox message in the same transaction.
- Concurrency tests prove exactly one booking wins a contested slot.

## Prerequisites
- Phase 09 complete.
- **D-006 decided.**

## In scope
- **`Booking` aggregate** (`IShopOwned`):
  - customer id (nullable for walk-ins, with a walk-in name), professional, service or package
  - **snapshot**: localized name, price, currency, duration, and package items
  - start/end (UTC) and a `tstzrange` column
  - status (D-016), channel (Online/WalkIn/QR attribution placeholder), notes
  - concurrency version
  - neutral payment fields: `PaymentStatus = NotApplicable`, `AmountDue` snapshot (payment seam, R-BKG-10)
- **`BookingStatusHistory`**: from, to, actor id, actor type, reason, timestamp.
- **`BookingNote`**.
- **State machine** (D-016), with unit tests for every allowed and forbidden transition (R-BKG-02).
- **DB guarantee:**
  - `EXCLUDE USING gist (professional_id WITH =, during WITH &&) WHERE (status IN ('Pending','Confirmed','Arrived'))` (btree_gist).
  - A composite FK to the same-shop professional and service.
- **Commands:**
  - `CreateOnlineBooking` (customer; resolves "any" per D-012)
  - `CreateWalkIn` (shop)
  - `Reschedule`, `CancelByCustomer` (cutoff per D-015), `CancelByShop`
  - `Confirm`, `MarkArrived`, `Complete`, `MarkNoShow`
  - admin intervention variants

  Each command runs in a single transaction: recheck availability → insert/update → handle an exclusion violation as a typed 409 `booking.slot_unavailable` (R-BKG-06).
- **Idempotency:**
  - An `IdempotencyRecord` table (key, user, request hash, response, expiry).
  - An `Idempotency-Key` header is required for create and reschedule (R-BKG-05).
- **Outbox:**
  - An `OutboxMessage` table (type, payload without phone numbers, occurred at, processed at, attempts).
  - Rows are written through the same DbContext transaction: BookingCreated, BookingRescheduled, BookingCancelled and BookingStatusChanged (R-BKG-08).
  - The dispatcher/processor arrives in Phase 15. Until then a test verifies that rows exist and that a rollback leaves none.
- **Integration with other modules:** availability's booking port is now wired to real bookings, and `IShopBookability` is enforced on online create only. Walk-ins stay allowed (D-014).
- **API:**
  - Customer: `POST /bookings`, `GET /me/bookings?tab=upcoming|past`, `GET /me/bookings/{id}` (with `allowedActions`), `POST /me/bookings/{id}/cancel`, `POST /me/bookings/{id}/reschedule`, `GET /me/bookings/{id}/calendar.ics`.
  - Shop: `GET /shop/bookings` (date range/status/professional/search by name or reference only, DV-S18), `GET /shop/bookings/{id}` (**no customer phone**), `POST /shop/bookings/walk-in`, `POST /shop/bookings/{id}/transitions`.
  - Admin: read/search (the UI is in Phase 14).
- **Concurrency tests (R-BKG-04):**
  - N parallel requests for the same professional and slot → exactly one 201 and N−1 409.
  - The same for reschedule.
  - Overlapping partial ranges.
- **Tests:**
  - Snapshot retained after the service is edited (R-BKG-01).
  - Expiry does not alter future bookings (R-SUB-05).
  - Phone absence on the shop booking DTOs (R-NEG-04).
  - Cross-shop booking IDOR.
- **Seed:** sample appointments (completed, upcoming, cancelled, no-show) generated from the availability engine so they are valid.
- **Docs:** `docs/availability-and-booking.md` sections on the booking lifecycle, the concurrency guarantee and the payment seam.

## Explicitly out of scope
Customer and shop UIs (Phases 12/13), and message dispatch (Phase 15).

## Checklist (100 points)
- [x] 10.1 (5) Re-validate and refine the checklist. Phase 09: Availability unit 37/37, integration 5/5, E2E `flows/shop-schedule.spec.ts` 2/2 on the committed code. D-006 was already decided by the user (D-037), so nothing to ask. The design review settled the following before code:
  - data-layer customer isolation instead of a bypass scope (D-085);
  - the recheck in the public scope;
  - the building-block ports;
  - the idempotency claim order;
  - the service-usage guard.
- [x] 10.2 (12) Booking aggregate with snapshot, owned history, shop-owned notes, the payment seam and the reference (D-086). Migration `Bookings` adds:
  - the generated `during tstzrange`;
  - the exclusion constraint (SQL);
  - same-shop service and package keys (SQL);
  - the professional composite FK;
  - `infra.outbox_messages` and `infra.idempotency_records`.
- [x] 10.3 (12) State machine (D-016) with time rules and the customer cutoff (D-087). Unit tests:
  - all 49 status pairs through `CanTransition` and through the shop transition with history;
  - arrival window, no-show after the start, reason required, cutoff boundary, reschedule, walk-in statuses, references;
  - the walk-in engine rule.
- [x] 10.4 (12) Create, reschedule, cancel, transition and admin-cancel commands, each in one transaction. The recheck goes through `IAvailabilityChecker`. A lost race is typed as 409 `booking.slot_unavailable`: an exclusion violation, or a deadlock between overlapping inserts (found by the race test, D-089).
- [x] 10.5 (8) Idempotency records with header enforcement (400 missing, 422 reused), replay with `Idempotent-Replayed`, a same-key race giving one booking, and rollback on failure.
- [x] 10.6 (8) Outbox rows written in the transaction: created, rescheduled, cancelled and status_changed, ids only. None are written for refused or losing commands (tested).
- [x] 10.7 (12) Concurrency suite: an 8-way create race, partial overlaps, a reschedule race, a same-key race and the raw constraint. 20/20 consecutive runs pass locally.
- [x] 10.8 (10) Customer, shop and admin booking APIs:
  - customers can neither read nor change another customer's booking (404; raw data-layer attempt throws);
  - shop B gets 404 on every verb for shop A's booking;
  - user types are separated (403);
  - no phone in shop or admin payloads, outbox or `.ics`;
  - admin cancel is audited;
  - the authorization matrix and shop-facing contract suites cover the new endpoints automatically.
- [x] 10.9 (6) Walk-ins share the collision checks: overlap 409, outside hours 409, any minute allowed, "now" gives Arrived (D-035, D-088).
- [x] 10.10 (5) Seed:
  - two demo customers (Identity, encrypted mobile);
  - completed, no-show and cancelled history, and two upcoming bookings on real free slots (none for Faisal);
  - integration `DemoSeed_AddsValidSampleBookings_Idempotently`.
- [x] 10.11 (10) Docs: the Bookings sections of `docs/availability-and-booking.md` (Mermaid lifecycle, no-double-booking, idempotency, outbox, payment seam), domain model, README. Gates, control files, commit.

## Files/modules expected to change
- `src/Modules/Bookings/**`;
- `src/Modules/Availability/**` (checker, walk-in rule, loader options);
- `src/Modules/Identity/**` (customer directory, demo customers);
- `src/Modules/Services/**` (offer snapshot);
- `src/Modules/Shops/**` (manual confirmation in the summary);
- `src/BuildingBlocks/**` (customer-owned rule, outbox, idempotency, database errors, contracts);
- `docs/availability-and-booking.md`.

## Data model and migration impact
- Schema `bookings` (`bookings`, `booking_history`, `booking_notes`), plus `infra.outbox_messages` and `infra.idempotency_records`.
- Migration `Bookings` (`20260928111635`). The phase plan named it `0008_Bookings`; the repo uses timestamped names.

## API contracts and UI routes
See D-085 … D-089 and `docs/availability-and-booking.md`. No UI in this phase.

## Security, tenancy, privacy, RTL, a11y, responsive
- Customer isolation is enforced in the data layer (D-085), and shop isolation by the tenant filter.
- No phone on bookings, in shop or admin DTOs, in outbox payloads or in `.ics` files.
- Outbox payloads carry ids only.

## Tests and verification commands
Smallest decisive re-validation (for the next session):
```
dotnet test --project tests/Trimme.UnitTests -c Release --filter-namespace "*Bookings"
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Bookings"
for i in $(seq 1 20); do dotnet test --project tests/Trimme.IntegrationTests -c Release --no-build --filter-class "*BookingConcurrencyTests" || break; done
```

## Acceptance criteria
- The concurrency suite is deterministic: 20/20 runs pass.
- The exclusion constraint exists in the migration.
- Every R-BKG item for this phase passes.

## Rollback / recovery
Revert the commit and reset the DB. The exclusion constraint needs a fresh DB if the migration is edited.

## Completion evidence
Session 5, 2026-09-28.

**What was built**
- **Bookings module:**
  - domain: `Booking`, history, notes, the state machine and rules;
  - persistence and readers: the real `IBookedTimeReader`, the service-usage guard;
  - customer, shop and admin use cases, endpoints, and the demo seed.
- **Building blocks:**
  - `ICustomerOwned` / `ICurrentCustomer` with the data-layer rule;
  - `OutboxMessage`, `IdempotencyRecord` and `DatabaseErrors`;
  - the `IAvailabilityChecker` and `ICustomerDirectory` contracts;
  - snapshot fields on `BookableOffer`, the booking id on `BusyTime`, manual confirmation on `ShopSummary`;
  - 409 mappings for exclusion violations and deadlocks.
- **Availability:** `AvailabilityChecker`, `AvailabilityEngine.FreeTime`/`IsFree` (walk-ins, flags), loader options (ignore a booking, leave bookings out).
- **Identity:** `CustomerDirectory`, `DemoCustomersSeeder`.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration | PASS — 349 / 63 / 132 (Phase 09 end: 243 / 63 / 119). New: 106 unit (`Bookings/BookingDomainTests`, incl. 49-pair theories ×2) and 13 integration (`Bookings/*`) |
| Concurrency suite ×20 (`--filter-class "*BookingConcurrencyTests"`) | 20/20 runs pass (4 tests each, 80/80) |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — 229 web tests (no web code changes; the OpenAPI schema was regenerated) |
| Fresh `down -v` + `up --build`, `pnpm e2e` ×3 (regression; no new UI) | PASS — **47/47, 47/47, 47/47** (the first compose build hit a transient Docker BuildKit snapshot error, "parent snapshot does not exist"; the rebuild succeeded) |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` + `git` | PASS — the grep gates hit only excluded tests, Seeding and Geocoding; gitleaks no leaks on the committed code |

**Found and fixed while verifying**
- **Offsets.** Npgsql writes only UTC `DateTimeOffset` values to `timestamptz`, and clients send `+03:00`. Every booking instant is now normalized to UTC in the domain and in the query filters.
- **Deadlocks under concurrency.** Concurrent inserts checked by one GiST exclusion constraint can deadlock. PostgreSQL aborts one with `40P01`, which EF wraps as a "transient failure", so the partial-overlap race returned 500s. A deadlock is now a lost race (409 `booking.slot_unavailable`), and the global handler has a fallback.
- **Npgsql in Application.** The architecture rules caught `Npgsql` in the Application layer. The checks moved to `DatabaseErrors` in building-block infrastructure.

## Remaining risks → next phase
- **Packages across professionals** are not supported in v1 (D-020): one professional does the whole package.
- **"Any professional" does not retry** another free candidate after losing a race (a plain 409, D-088).
- **The outbox only accumulates** until the Phase 15 processor. Idempotency records expire after 24 h but are not purged yet (Phase 15 jobs).
- **Next:**
  - Phase 11 — Public discovery. It hides paused shops through `IShopBookability.VisibleInDiscovery`.
  - Phase 12 — the customer booking UI on these APIs (E flow 1).
