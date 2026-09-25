# Phase 14 — Admin operations dashboard

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
Platform admins can operate the platform:
- useful KPIs;
- global bookings search with details, history and intervention;
- a customers directory with protected contact reveal;
- reviews moderation;
- roles and permissions management;
- an audit log;
- the full platform settings UI.

## Prerequisites
Phase 13 complete.

## In scope
- **Overview** `/admin` (a-overview). KPIs (R-AD-01), with time-range filters, computed in SQL:
  - appointments today, completion rate, cancellations, no-shows;
  - active shops, expiring subscriptions, active professionals, new customers;
  - popular services (by category), top shops.
- **Bookings** `/admin/bookings`, `/[id]` (DV-A09):
  - Search and filters: shop, status, date, reference, customer name.
  - Details with history and dispatches (the dispatches link becomes live in Phase 15).
  - Intervention: status change with reason, and reschedule with an availability check. Audited.
- **Customers** `/admin/customers`, `/[id]` (DV-A08, DV-S17):
  - booking count, upcoming/previous, last booking, registration date;
  - masked phone with "إظهار الرقم" (permission + reason + audit) (R-AD-06).
- **Reviews** `/admin/reviews`: list, filters, hide/unhide with reason, audited (R-AD-07).
- **Roles and permissions** `/admin/roles` (DV-A15):
  - role CRUD (system roles protected);
  - permission toggles from the catalogue;
  - user-role assignment;
  - SuperAdmin permissions assignable only by SuperAdmin.
- **Audit** `/admin/audit` (DV-A16): filters for actor, entity, action and date; entity links.
- **Settings** `/admin/settings` (DV-A17), all sections:
  - booking policy, locale, currency, time zone;
  - reminder offset;
  - expiring threshold and enforcement;
  - pause visibility;
  - map defaults.

  Changes are audited.
- **Lists** are paginated with server-side filtering and a max page size (R-FND-13). Large tables use cursor or keyset pagination where needed.
- **Tests:** the permission matrix for every admin endpoint (Ops vs Support vs SuperAdmin), and the audit assertions.

## Explicitly out of scope
WhatsApp admin (Phase 15) and QR admin (Phase 16).

## Checklist (100 points)
- [ ] 14.1 (5) Re-validate; refine checklist.
- [ ] 14.2 (12) Overview KPI queries + tests + page.
- [ ] 14.3 (14) Bookings search/detail/intervention + tests.
- [ ] 14.4 (12) Customers directory + protected reveal + tests.
- [ ] 14.5 (8) Reviews moderation.
- [ ] 14.6 (14) Roles/permissions management + escalation guards + tests.
- [ ] 14.7 (8) Audit log page.
- [ ] 14.8 (10) Platform settings full UI + validation + audit.
- [ ] 14.9 (7) Pagination/virtualisation + responsive table → cards.
- [ ] 14.10 (10) E2E admin flows, axe, 3 viewports, gates, control files, commit.

## Files/modules expected to change
`src/Modules/Administration/**`, `src/Modules/Customers/**`, `src/Modules/Reviews/**`, web `/admin/**`.

## Data model and migration impact
Possibly reporting indexes and views. Migration `0011_AdminReporting` if needed.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- The admin bypass scope is used only here, and is covered by architecture tests.
- Every sensitive read and write is audited.
- No privilege escalation is possible via role editing.

## Tests and verification commands
```
dotnet test --filter Category=Admin
pnpm exec playwright test admin
```

## Acceptance criteria
- Every R-AD item for this phase passes.
- A Support role cannot reveal a phone or change settings unless it is granted permission.

## Rollback / recovery
Revert the commit.

## Completion evidence
_(fill)_

## Remaining risks → next phase
Next: Phase 15 — WhatsApp, outbox, Hangfire & notifications.
