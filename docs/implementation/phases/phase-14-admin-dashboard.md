# Phase 14 — Admin operations dashboard

**Status:** [x] · **Score:** 100/100

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
Refined in Session 10 after re-validation; each item lists what "done" means.
- [x] 14.1 (5) Re-validate; refine checklist. Phase 13 `RealtimeTests` 4/4. `shop-dashboard.spec.ts` failed 2/2 first because the Session 9 `next dev` server had crashed ("Jest worker encountered 2 child process exceptions"); after stopping it and rebuilding the compose `web` container from HEAD: 2/2 passed.
- [x] 14.2 (12) Overview:
  - `GET /admin/dashboard/overview?days=1|7|30`, computed in SQL through read ports opened inside one admin scope;
  - the KPI definitions recorded in a D-entry;
  - an integration test with exact numbers on a fake clock, including a booking at the Riyadh midnight boundary;
  - the `/admin` page: KPIs, 14-day trend, popular categories, top shops, subscriptions to follow.
- [x] 14.3 (14) Bookings:
  - list filters: shop, statuses, dates, channel, customer, reference/name search; chip counts;
  - detail: history, notes read-only, customer link;
  - intervention: `POST /admin/bookings/{id}/transitions` (reason required) and `POST /admin/bookings/{id}/reschedule` (collision rules, no cutoff, not in the past, reason, idempotency key, the same transaction and 409 path), plus the free starts for a date;
  - audited;
  - tests.
- [x] 14.4 (12) Customers:
  - `GET /admin/customers` (name search, booking count, last booking, registration date; no phone in list rows);
  - `GET /admin/customers/{id}` (masked phone, stats, upcoming and previous bookings);
  - `POST /admin/customers/{id}/contact/reveal` (`Admin.Customers.ViewContact`, reason ≥ 5, audited, `no-store`);
  - test `PhoneReveal_RequiresPermission_AndAudits`.
- [x] 14.5 (8) Reviews moderation:
  - list with flags (reported, low rating, contains a phone number) and filters;
  - flag (`Admin.Reviews.Flag`);
  - hide and unhide with reason (`Admin.Reviews.Moderate`);
  - rating aggregates ±1 in the same transaction, with a version check (parallel hides subtract once);
  - public lists show published reviews only;
  - migration;
  - tests.
- [x] 14.6 (14) Roles and staff:
  - role create, rename and delete (platform-admin roles; system roles protected);
  - permission toggles;
  - staff list, role assignment, disable and enable.

  Guards:
  - grant only permissions you hold;
  - `SuperAdmin.*` never on an editable role;
  - the SuperAdmin role only by a SuperAdmin;
  - never the last SuperAdmin, never your own roles.

  Audited, with tests. Includes the end-to-end proof that Support cannot reveal once the grant is removed and can again once granted.
- [x] 14.7 (8) Audit log:
  - `GET /admin/audit`: actor, action, entity, shop and date filters, keyset cursor, actor names;
  - page with entity links;
  - tests.
- [x] 14.8 (10) Platform settings:
  - the full sectioned screen (booking policy, notifications, subscriptions, discovery, region read-only, map);
  - a save bar;
  - validation;
  - the recent changes from the audit log;
  - remaining DV-S14 values.
- [x] 14.9 (7) Every admin list server-paged with the max page size; audit keyset; tables become cards below 768; no overflow at 390/768/1440.
- [x] 14.10 (10) E2E admin spec (staff sign-in only), axe on every new page, three viewports, full gates, control files, commit.

## Files/modules expected to change
`src/Modules/Administration/**`, `src/Modules/Customers/**`, `src/Modules/Reviews/**`, web `/admin/**`.

As built:
- **Building blocks:** `Reporting/ReportingContracts.cs` (overview read ports) and `IUserNameLookup`.
- **Administration:** overview, audit trail, their endpoints, and the audit `sequence`.
- **Bookings:** `BookingStatistics` and the admin handlers (list, detail, transitions, reschedule, options).
- **Identity:** the customers directory and reveal, roles and staff, `IAdminAccounts`.
- **Reviews:** moderation and `RatingBook` subtraction.
- **Read ports:** Shops, Professionals and Services.
- **Web:** `/admin` and `/admin/bookings`, `/customers`, `/reviews`, `/roles`, `/roles/staff`, `/audit`, `/settings`; `components/admin/ops/*`; `lib/admin/admin.ts`; messages.
- **Tests:** integration `Administration/AdminOperationsTests.cs`, web `admin.test.ts` and `ops.test.tsx`, E2E `flows/admin-operations.spec.ts`.

## Data model and migration impact
Migration `20260929164439_AdminOperations`:
- **Reviews:** `flag_reason`, `flagged_at`, `moderation_reason` and `moderated_at`; the version is the `xmin` system column, so no column is added.
- **Audit entries:** a `sequence` bigint identity. Existing rows are numbered by `occurred_at` first, then the identity continues. New indexes: `(sequence)` unique, `(action, sequence)` and `(actor_user_id, sequence)`.
- **Bookings:** no new index. The existing `(customer_id, starts_at)` and `(shop_id, starts_at)` indexes serve the statistics.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- The admin bypass scope is used only here, and is covered by architecture tests.
- Every sensitive read and write is audited.
- No privilege escalation is possible via role editing.

## Tests and verification commands
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*AdminOperationsTests"
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test flows/admin-operations.spec.ts
```
The smallest decisive re-validation for the next session is these two commands.

## Acceptance criteria
- Every R-AD item for this phase passes.
- A Support role cannot reveal a phone or change settings unless it is granted permission.

## Rollback / recovery
Revert the commit.

## Completion evidence
Session 10, 2026-09-29. Actual results:

| Check | Result |
|---|---|
| Re-validation (14.1) | `RealtimeTests` 4/4. `shop-dashboard.spec.ts` 2/2 after replacing the crashed `next dev` with the compose web built from HEAD (the first run failed 2/2 on a Next.js "Jest worker" runtime error page, not on code) |
| `dotnet build Trimme.slnx -c Release` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: **378 / 63 / 158** (152 + 6 new `AdminOperationsTests`; the full integration suite ran twice, before and after the admin reason change) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes. `AdminOperations` applied on the compose stack (12 migrations) and from empty in `Migrations_ApplyToEmptyDatabase` |
| OpenAPI | Regenerated (`TRIMME_UPDATE_OPENAPI=1`); `OpenApi_document_matches_committed_contract` PASS; `pnpm openapi:check` PASS |
| Web `format:check`, `lint`, `typecheck`, `test`, `build` | PASS; **337** web tests (318 + 19); every admin route builds as a dynamic server page |
| E2E `typecheck`, `format:check` | PASS |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2` | **68/68 ×2** (2.4 and 2.3 min), no retries. API log over both runs: no 5xx, no 429 |
| No-transfer and R-NEG-08 grep gates | PASS: no hits outside the seeders and the excluded geocoder |
| gitleaks v8.30.1 `dir` and `git` | PASS, no leaks (`git`: 54 commits, after commit `e9231a5`) |
| Visual check (ar 1440/390, en 1440): overview, bookings, customer, reviews, roles, audit, settings | RTL mirrored. Fixed during the check: the permission matrix overflowed at 390 (its `sr-only` cells escaped the scroller, the D-049 pattern, so the scroller is now `relative w-0 min-w-full`); the bookings filter grid overflowed at 768; actor names needed `<bdi>` in mixed text; "no earlier figure" deltas are neutral |

Problems found and fixed while building:
- **Taking a review off the rating totals failed** (`ck_rating_aggregates_count`). PostgreSQL checks CHECK constraints on the proposed row of `INSERT … ON CONFLICT` before resolving the conflict, so subtracting is now a plain `UPDATE` (D-102). The parallel-hide test proves the totals move once.
- **The top-shops query did not translate** (projection into a constructor before `OrderBy`); it now projects after ordering.
- **Admin intervention reasons** now require 5 characters, like every other admin reason, so the shared "at least 5 characters" message is true.
- **The E2E review test first navigated away before its report request completed.** It now waits for the confirmed row.

The acceptance criterion "a Support role cannot reveal a phone unless granted" is proven end to end in `PhoneReveal_RequiresPermission_AndAudits`: the grant is removed through the roles API, the next request gets 403, the grant is restored, and the reveal works and is audited again.

## Remaining risks → next phase
- **The last-SuperAdmin guard cannot be reached through the API.** The self rule and the SuperAdmin-only rule already make it impossible, so it stays as defence in depth and is not integration-tested.
- **Audit summaries are English technical text** (D-063: PII-free), shown as-is in the Arabic UI apart from the settings field names. Action and entity names are translated.
- **The overview's day boundaries use the platform time zone.** A future multi-time-zone rollout would need per-shop grouping.
- **The design's review action «تواصل مع المحل» (contact the shop)** waits for shop notifications (Phase 15).

Next: Phase 15 — WhatsApp, outbox, Hangfire & notifications.

**Commit:** `e9231a5` (feat: phase 14 admin operations dashboard). Not pushed.

**Review follow-up (after `e9231a5`)**
- **Blocking, fixed: staff invitations bypassed the escalation guards.** `POST /admin/staff/invitations` only checked that the role existed. An admin holding `Admin.Staff.Manage` through a custom role could have invited an address they control as SuperAdmin, or with any role whose permissions they lack.
  - Invitations now use the same `StaffGuards.CheckGrantableAsync` as role assignment (D-106 addendum).
  - The invite form offers only the roles the admin could grant.
  - `Roles_AndStaff_…` asserts both refusals (`role.superadmin_only`, `role.escalation`).
- **The N+1 in the review list is removed.** `IProfessionalDirectory.FindManyAsync` replaces one query per shop.
- **Axe now also runs on the bookings list.**
- **A shop's page links to its bookings** (`?shop=`), so the shop filter is reachable.
- **R-TEN-08 is back to `[~]`** (a "5 → all" row; Phase 15 adds notification actions).
- **E2E robustness:**
  - `shop-dashboard.spec.ts` counted rows before the list had loaded; on a reused volume, earlier runs' walk-ins made it flaky once (passed on retry). It now waits for the rows or the empty state.
  - The customers E2E searched the first page for a seeded customer, whom newer sign-ups push off it; it now searches by name.
- **Verification:**
  - build 0 warnings;
  - unit / architecture / integration **378 / 63 / 158**;
  - `has-pending-model-changes` clean; OpenAPI contract unchanged;
  - web `format:check`, `lint`, `typecheck` and `test` (**337**), `openapi:check`; E2E `typecheck` and format;
  - **fresh `down -v` + `up --build`: 68/68 ×2** (`CI=1`, 2 workers), no retries; API log: no 5xx, no 429.
