# Phase 13 — Shop operational dashboard & live updates

**Status:** [ ] · **Score:** 0/100

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
- **Overview KPIs** (s-overview), via `GET /shop/dashboard/overview?date`:
  - today's appointments, next up, completed, no-shows, cancellations, free capacity;
  - professional load;
  - expiry warning.
- **Calendar** `/shop/calendar?view=day|week`:
  - Day view: minute-accurate per-professional columns (DV-S19), with breaks and time off shaded.
  - Week view: grid plus a density heatmap strip (D-034, DV-A19).
  - Click a slot to open a prefilled walk-in.
- **Appointments** `/shop/appointments`:
  - Filters: every status chip (DV-S08), professional, date.
  - Search by name or reference only (DV-S18).
  - Detail drawer: snapshot, history timeline, notes, and "رقم العميل غير متاح" copy.
  - Actions come from `allowedTransitions`, with a cancel-reason dialog.
  - Optimistic update with rollback on conflict.
- **Walk-in** `/shop/walk-in` (s-walkin): steps for service → professional → time (bookable slots, or "start now" per D-035) → name → confirm. No phone field.
- **SignalR:**
  - `/hubs/operations` with cookie auth.
  - Group per `shop:{shopId}`, taken from claims only.
  - Events: BookingCreated/Updated/StatusChanged, carrying DTOs without phone numbers.
  - An admin group for admin views.
  - Tests: R-SD-10, and R-TEN-06 (the SignalR part: another shop's events are never received). The phone-absence test covers hub messages.
- **Subscription and pause banners** across the dashboard.
- **Mobile:** compact navigation and a drawer; tables become cards.
- **E2E:** E2 complete (services + walk-in + statuses + no foreign access + no phone in network payloads), and R-NEG-03 (no export).

## Explicitly out of scope
The shop notifications inbox (Phase 15).

## Checklist (100 points)
- [ ] 13.1 (5) Re-validate; refine checklist.
- [ ] 13.2 (10) Overview KPI queries + tests + page.
- [ ] 13.3 (16) Calendar day and week (minute-accurate, per professional, heatmap strip).
- [ ] 13.4 (14) Appointments list + drawer + allowed transitions + optimistic rollback.
- [ ] 13.5 (10) Walk-in flow.
- [ ] 13.6 (14) SignalR hub, tenant-scoped groups, client integration + isolation/phone tests.
- [ ] 13.7 (5) Banners (subscription/pause) + responsive mobile nav.
- [ ] 13.8 (14) E2E E2 complete incl. network-payload phone scan + no-export assertion.
- [ ] 13.9 (12) Axe, 3 viewports vs reference, gates, control files, commit.

## Files/modules expected to change
- Bookings query side
- `apps/api` hubs
- web `/shop/**`

## Data model and migration impact
Indexes for the dashboard queries (shop_id, start). No new tables expected.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- Hub group membership is taken from claims only; a client-requested group join is rejected.
- The calendar is keyboard-navigable and mirrors in RTL.

## Tests and verification commands
```
dotnet test --filter "Category=ShopDashboard|Category=Realtime"
pnpm exec playwright test e2
```

## Acceptance criteria
- E2 passes.
- A second shop's session never receives events from the first shop.

## Rollback / recovery
Revert the commit.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- SignalR scale-out (backplane) is not needed for a single instance; it is documented in Phase 17.
- Next: Phase 14 — Admin operations.
