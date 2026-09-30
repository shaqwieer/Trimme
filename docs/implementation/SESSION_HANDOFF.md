# TRIMME Session Handoff

- **Updated:** 2026-09-30 (Session 11: Phase 15)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Pushed: everything up to `26114ee`.
  - Local only: the Phase 14 commits (`e9231a5`, `212da66`, `714661c`, `26036bd`) and the Phase 15 commit.
- **HEAD commit:** the Phase 15 commit, `feat: phase 15 whatsapp, outbox, hangfire and notifications`. Run `git log --oneline -6`.
- **Working tree:** clean after the commit.
- **Local stack: running, all in compose,** on a fresh volume from this session's last `down -v` + `up --build`, which built this session's code.
  - The web container predates a Prettier-only reformat of two files; there is no behaviour difference.
  - Services: `postgres`, `migrate` and `seed` (done, 13 migrations, Hangfire schema, 18 templates), `api` (Hangfire server and outbox processor running), `web`, `mailpit`.
  - Ports: web 3300, API 8080, DB 5434, Mailpit 8325. Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
  - This volume has had two full E2E runs this hour. The OTP budget allows at most five per stack per hour.
- **Current phase:** 15 is complete. Phase 16 has not started.
- **Phase score:** 100 / 100 (Phase 15).
- **Last fully completed phase:** 15, WhatsApp, outbox, Hangfire and notifications.

## Completed this session (Session 11)
Phase 15 (`phases/phase-15-notifications-whatsapp.md`):
- **Background work** (D-108):
  - Hangfire 1.8.25 on PostgreSQL (schema `hangfire`, installed by `migrate`), with explicit lazy storage.
  - `IJobScheduler` port and recurring jobs: `outbox-maintenance`, `notifications-sweep`, `notifications-retention`, `subscription-expiry`.
  - The outbox processor: a hosted loop with a leader advisory lock, per-consumer transactions, `infra.processed_messages`, backoff, and a dead letter after 8 attempts.
  - The read-only dashboard `/api/ops/jobs`, gated by the new `Admin.Jobs.View`.
  - `Jobs__Enabled` is the switch, off in Testing.
- **Templates** (D-109):
  - 18 slots (event × audience × locale) with versions: draft, activate (audited), restore.
  - The placeholder whitelist, with no phone and `manage_url` for customers only.
  - Validation, and rendering identical to `format.ts`.
  - Default wording created by `migrate`.
- **Dispatches and providers** (D-110):
  - Encrypted and masked recipients, the template version, hash and retention.
  - A send job with backoff; Failed notifies the admins; an audited admin retry.
  - Providers: Fake (dev and test), Meta (Graph API, contract-tested) and None.
  - The signed webhook `/api/v1/webhooks/whatsapp`.
  - The safe test send, which refuses customer numbers.
  - OTP over WhatsApp (`Identity__Otp__Sender=WhatsApp`).
- **Lifecycle and reminders** (D-111):
  - Confirmed, pending, rescheduled and cancelled messages for customers and professionals.
  - Reminders at start − `ReminderOffsetMinutes` in `reminder_schedules` with Hangfire job ids, replaced on reschedule and cancelled on cancel.
  - Events older than 24 hours send no message.
- **In-app notifications** (D-112):
  - `shop_notifications` (tenant-scoped) and `user_notifications`.
  - `/hubs/notifications`; the hub origin guard now covers all of `/hubs`.
  - Pages `/shop/notifications` and `/account/notifications`, and bells (the admin bell is a menu).
  - «تواصل مع المحل» from the reviews page (the Phase 14 carry-over).
- **Subscription expiry notices** (D-113): the threshold, then 7, 3 and 1 day(s), then the first day after the end, for the shop and the admins.
- **Admin UI:**
  - `/admin/whatsapp/templates` and the editor (placeholder chips, neutral bubble preview, validation, history, test send);
  - `/admin/whatsapp/dispatches` and the dispatch detail (retry);
  - the WhatsApp section on the admin booking page (messages and reminders).
- **Cross-module ports** (building blocks): `IBookingNotificationSource` (Bookings), `ICustomerContactReader`, `ICustomerNumberCheck` and `IStaffDirectory` (Identity), `IProfessionalContactReader` (Professionals), `INotificationCenter` (Notifications), `IWhatsAppAuthenticationSender`. `ShopSummary` gains the address.
- **Other fixes:**
  - `Card` now passes HTML attributes through.
  - Serilog request logging wraps the exception handler, so aborted requests log 499, not 500.
  - The E2 walk-in takes the day's last free time (a date-dependent flaw).
- **Docs:**
  - `docs/whatsapp-integration.md` (Meta setup, template approval, webhook, lifecycle Mermaid);
  - domain model, architecture diagram, README, permissions matrix;
  - design deviations: DV-S06, A05, A12, A13 and T06 applied; new DV-C10.

## Verification evidence
| Command | Result |
|---|---|
| Re-validation of Phase 14 | `AdminOperationsTests` 6/6, `admin-operations.spec.ts` 6/6 |
| `dotnet build Trimme.slnx -c Release` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 395 / 65 / 172 (incl. `NotificationsHubTests` and the retention/maintenance job test) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| OpenAPI contract test (regenerated) and `pnpm openapi:check` | PASS (22 operations added, none removed) |
| Web `format:check`, `lint`, `typecheck`, `test`, `build` | PASS: 357 web tests |
| E2E `tsc`, `prettier --check` | PASS |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2` | **69/69 ×2** on the final code, no retries; API log 0 × 5xx, 0 × 429, no warnings; outbox 0 pending, 0 dead-lettered. An earlier rebuild's run 1 was 66/69: three issues, all fixed (phase file §Evidence). |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` (and `git` after the commit, recorded in the follow-up `docs:` commit) | PASS: no hits outside tests, seeders and the excluded geocoder; no leaks |

## Database and migrations
- Created and applied locally: `20260930065901_Notifications`, on the compose volume and from empty in the integration tests. It adds the `notifications` schema and the outbox retry columns and `infra.processed_messages`.
- `migrate` also installs the Hangfire schema and the default templates.
- No production migration was run.

## Decisions added
- D-108: Hangfire and the outbox processor (storage, port, leader lock, processed messages, backoff and dead letter, dashboard and `Admin.Jobs.View`, switch).
- D-109: WhatsApp templates (slots, versions, whitelist, validation, rendering, defaults, Meta mapping).
- D-110: dispatches, providers, retries, webhook, test send, OTP over WhatsApp.
- D-111: lifecycle messages and reminders (plan, recipients, freshness, reconciliation, the job ids' deviation).
- D-112: in-app notifications (two tables, hub, pages and bells, contact the shop, no shop resend).
- D-113: subscription expiry notices.
- Design deviations: DV-C10.

## Known issues or blockers
- **Meta.** Production WhatsApp needs a Meta account, approved templates matching each active version (by name, parameters in order), an authentication template and the webhook. None of it is exercised without credentials (`docs/whatsapp-integration.md`).
- **Operations gaps:**
  - dead-lettered outbox messages are only logged (no replay screen);
  - the in-app hub needs a backplane for more than one API instance (Phase 17, with `/hubs/operations`);
  - shops have no "resend confirmation" (DV-C10).
- **Carried over:**
  - production Nginx for `/hubs` and client IP forwarding (Phase 17);
  - at most five E2E runs per stack per hour (OTP);
  - "any professional" does not retry;
  - packages across professionals (D-020);
  - grace days and limits (D-077);
  - oversized uploads through the rewrite (Phase 17).
- **Resolved this session:** "Serilog 400/409-as-500".

## Exact next action
1. Push when the user asks: `git push origin main`. CI runs the 69-test E2E suite; its compose stack starts the Hangfire server and the outbox processor.
2. Start Phase 16 (`phases/phase-16-qr-analytics.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*NotificationsTests"`
   - `cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test flows/whatsapp-notifications.spec.ts`
3. Phase 16 can reuse:
   - the outbox consumers (`IOutboxConsumer`) to attribute bookings;
   - `IRecurringJob` for aggregation;
   - the in-app notifications, for example weekly QR summaries if wanted;
   - the admin page patterns of `/admin/whatsapp`.

## Files intentionally left modified
- None.
