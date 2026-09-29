# TRIMME Session Handoff

- **Updated:** 2026-09-29 (Session 10: Phase 14)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). The Phase 14 commit and its `docs:` follow-up are **local, not pushed**; everything up to `26114ee` was pushed (CI run #19 green).
- **HEAD commit:** the Phase 14 `docs:` commit on top of the `feat: phase 14 admin operations dashboard` commit. Run `git log --oneline -4`.
- **Working tree:** clean after the commits. `next dev` re-creates untracked `apps/web/AGENTS.md` and `apps/web/CLAUDE.md` when it starts; delete them or leave them untracked (the user's call).
- **Local stack: running, all in compose.** It runs on a fresh volume from this session's `down -v` + `up --build`: `postgres`, `migrate`/`seed` (done, 12 migrations), `api`, `web` (production build of this session's code) and `mailpit`.
  - Ports: web 3300, API 8080, DB 5434, Mailpit UI 8325. Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
  - The Session 9 `next dev` server had crashed (a "Jest worker" runtime error) and was stopped.
  - Two full E2E runs used this volume's hourly OTP budget twice (at most five per hour).
- **Current phase:** 14 is complete. Phase 15 has not started.
- **Phase score:** 100 / 100 (Phase 14).
- **Last fully completed phase:** 14, admin operations dashboard.

## Completed this session (Session 10)
Phase 14 (`phases/phase-14-admin-dashboard.md`):
- **Overview** `/admin` (D-101). `GET /admin/dashboard/overview?days=1|7|30`, read inside one admin scope from building-block ports (`IBookingStatistics`, `IShopStatistics`, `IProfessionalStatistics`, `ICustomerStatistics`, `IServiceCategoryLookup`), all SQL aggregates on the Riyadh platform calendar. The page shows:
  - eight KPIs with deltas;
  - a 14-day stacked trend with a data table;
  - popular categories;
  - top shops;
  - subscriptions to follow up.
- **Bookings** `/admin/bookings` and `/[id]` (D-103):
  - filters, chip counts, history, read-only notes;
  - transitions with a reason (5–300);
  - reschedule with collision rules, an idempotency key, the same transaction and 409 path, and an options endpoint for date and professional;
  - audited, with outbox events.
- **Customers** `/admin/customers` and `/[id]` (D-105): name search only, booking figures, the masked mobile, and «إظهار الرقم» (`Admin.Customers.ViewContact`, reason, audited without the number, `no-store`).
- **Reviews** `/admin/reviews` (D-102):
  - the queue with Reported, LowRating and ContainsPhone flags (one regex in .NET and PostgreSQL);
  - flag (Support), hide and publish (moderators);
  - an `xmin` version, with the totals moved in the same transaction (subtracting is a plain `UPDATE`).
- **Roles and staff** `/admin/roles`, `/[roleId]`, `/roles/staff` (D-106): the matrix, role create, rename and delete, permission editing, staff role assignment, disable and enable, and invitations. Guards:
  - managed and seed roles are protected;
  - no `SuperAdmin.*` on another role;
  - grant only what you hold;
  - not yourself;
  - SuperAdmin only by a SuperAdmin;
  - never the last SuperAdmin.
- **Audit** `/admin/audit` (D-104): filters, entity links, actor names, and a keyset cursor on the new `sequence` identity.
- **Settings** `/admin/settings` (D-107): section descriptions, ranges, a sticky save bar with discard, and recent changes.
- **Migration** `AdminOperations`: review moderation columns and the audit `sequence`, backfilled in time order.
- **Tests:**
  - integration `Administration/AdminOperationsTests.cs` (6);
  - web `lib/admin/admin.test.ts`, `components/admin/ops/ops.test.tsx` (19);
  - E2E `flows/admin-operations.spec.ts` (6, staff sign-in only, no shared state left changed).

## Verification evidence
| Command | Result |
|---|---|
| Re-validation of Phase 13 | `RealtimeTests` 4/4; `shop-dashboard.spec.ts` 2/2 on the compose web (it failed first because of the crashed `next dev`) |
| `dotnet build Trimme.slnx -c Release` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 378 / 63 / 158 |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| OpenAPI contract test (regenerated) and `pnpm openapi:check` | PASS |
| Web `format:check`, `lint`, `typecheck`, `test`, `build` | PASS: 337 web tests |
| E2E `typecheck`, `format:check` | PASS |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2` | **68/68 ×2**, no retries; API log: no 5xx, no 429 |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` (and `git` after the commit) | PASS: no hits, no leaks |

## Database and migrations
- Created and applied locally: `20260929164439_AdminOperations`, applied on the compose volume and from empty in the integration tests. No production migration was run.

## Decisions added
- D-101: overview KPI definitions and read ports.
- D-102: reviews moderation (flags, hide and publish, totals once, the plain `UPDATE` for subtraction).
- D-103: admin booking intervention (reason ≥ 5, collision rules, idempotency, options).
- D-104: the audit log read side and keyset `sequence`.
- D-105: the customers directory and the audited reveal (no phone search).
- D-106: roles and staff management with escalation guards.
- D-107: the settings screen; the remaining DV-S14 values are settled.
- Design deviations: DV-S14 and DV-S17 are applied; DV-A08, A09, A15, A16 and A17 are applied; new DV-C09 (overview, bookings, reviews and roles choices).

## Known issues or blockers
- **Guards and presentation:**
  - The last-SuperAdmin guard cannot be reached through the API (other guards come first); it is defence in depth.
  - Audit summaries are English technical text (PII-free, D-063); action and entity names are translated.
  - The overview groups days on the platform time zone (all shops are in Riyadh).
- **Deferred to Phase 15:** the review action «تواصل مع المحل» (contact the shop), shop notifications, WhatsApp dispatch links on the booking detail, and the booking outbox processor.
- **Carried over:**
  - click-then-navigate E2E timings on a cold stack (none seen this session);
  - production Nginx for `/hubs` and the SignalR backplane (Phase 17);
  - revocation lag for live events;
  - at most five E2E runs per stack per hour (OTP);
  - "any professional" does not retry;
  - packages across professionals (D-020);
  - grace days and limits (D-077);
  - Serilog 400/409-as-500 (none logged in this session's runs);
  - oversized uploads through the rewrite (Phase 17).

## Exact next action
1. Push when the user asks: `git push origin main` (CI will run the 68-test E2E suite).
2. Start Phase 15 (`phases/phase-15-notifications-whatsapp.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*AdminOperationsTests"`
   - `cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test flows/admin-operations.spec.ts`
3. Phase 15 can reuse:
   - the booking outbox rows (D-089);
   - the platform `ReminderOffsetMinutes` setting;
   - the admin audit log for template activation;
   - the booking detail page (add the dispatches section);
   - the reviews «contact the shop» slot.

## Files intentionally left modified
- None.
