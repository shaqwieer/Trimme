# Phase 10 — Booking core & integrity

**Status:** [ ] · **Score:** 0/100

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
- [ ] 10.1 (5) Re-validate; confirm D-006; refine checklist.
- [ ] 10.2 (12) Booking aggregate, snapshot, history, notes, migration with exclusion constraint.
- [ ] 10.3 (12) State machine + exhaustive unit tests.
- [ ] 10.4 (12) Create/reschedule/cancel/transition commands with transactional recheck + typed conflicts.
- [ ] 10.5 (8) Idempotency records + header enforcement + tests.
- [ ] 10.6 (8) Outbox writes in-transaction + rollback test.
- [ ] 10.7 (12) Concurrency test suite (create + reschedule), stable in CI (run 20×).
- [ ] 10.8 (10) Customer/shop/admin booking APIs + permission and cross-shop tests + phone-absence snapshot.
- [ ] 10.9 (6) Walk-in command sharing collision checks (R-BKG-07).
- [ ] 10.10 (5) Seed appointments.
- [ ] 10.11 (10) Docs (lifecycle Mermaid, payment seam), gates, control files, commit.

## Files/modules expected to change
- `src/Modules/Bookings/**`
- `src/Modules/Availability/**` (port wiring)
- `src/BuildingBlocks/**` (outbox and idempotency)
- `docs/availability-and-booking.md`

## Data model and migration impact
- Schema `bookings` (bookings, status_history, notes), plus `outbox` and `idempotency` tables.
- Migration `0008_Bookings`.

## API contracts and UI routes
As listed. No UI in this phase.

## Security, tenancy, privacy, RTL, a11y, responsive
- The shop DTO excludes the phone.
- Customers see only their own bookings; a foreign ID returns 404.
- Outbox payloads carry IDs, not phone numbers.

## Tests and verification commands
```
dotnet test --filter "Category=Bookings|Category=Concurrency"
for i in $(seq 1 20); do dotnet test --filter Category=Concurrency || break; done
```

## Acceptance criteria
- The concurrency suite is deterministic: 20/20 runs pass.
- The exclusion constraint exists in the migration.
- Every R-BKG item for this phase passes.

## Rollback / recovery
Revert the commit and reset the DB. The exclusion constraint needs a fresh DB if the migration is edited.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- Package bookings across professionals are not supported in v1 (D-020).
- Next: Phase 11 — Public discovery.
