# Phase 15 — WhatsApp, outbox processing, Hangfire & notifications

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
Booking lifecycle events reliably produce correctly rendered WhatsApp messages through the fake provider, for both customers and professionals:
- confirmation;
- update or cancel;
- a reminder exactly 30 minutes before the appointment (configurable).

Admins also get:
- a template editor with ar/en versions, preview, validation, activation and a safe test send;
- a dispatch log with retry.

In-app notification centres work for customers, shops and admins.

## Prerequisites
Phase 14 complete.

## In scope
- **Hangfire** with PostgreSQL storage (its own schema):
  - A dashboard at `/api/ops/jobs`, served by the API behind admin cookie auth and the `Admin.Jobs.View` permission. It lives under `/api/*` so the D-027 Nginx routing reaches it; the Next.js `/admin/*` space stays with the web app, which can link to it.
  - Recurring jobs:
    - an outbox processor (or a hosted service with a lock);
    - subscription status transitions and ExpiringSoon warnings (R-SUB-04/05);
    - stale idempotency cleanup.
- **Outbox processor:**
  - At-least-once delivery with an idempotent consumer (a processed-message table).
  - Backoff and a dead-letter state (R-NTF-05).
- **Notifications module:**
  - `WhatsAppTemplate`: event × audience (Customer/Professional) × locale.
  - `WhatsAppTemplateVersion`: body, buttons, placeholders, status Draft/Active/Archived, created by.
  - The placeholder whitelist: customer name, professional name, shop name, service name, booking date, booking time, time remaining, duration, amount, address, and the manage-booking URL (DV-S design additions).
  - Validation, and a renderer with locale formatting.
- **`WhatsAppDispatch`:**
  - Audience, recipient (encrypted + masked), template version id, rendered content hash (+ content stored with a retention policy), provider message id, status, attempts, error, and timestamps (R-NTF-07).
- **Provider abstraction `IWhatsAppProvider`:**
  - `FakeWhatsAppProvider` (dev/test: records to a table and a dev inbox view).
  - `MetaCloudApiProvider` (Graph API send-template; configuration only via environment variables; never enabled locally).
  - A webhook status endpoint stub with signature verification (R-NTF-01).
- **Event handlers:**
  - BookingCreated → customer confirmation, plus a professional new-booking alert if the professional is eligible (R-NTF-04).
  - Rescheduled or Cancelled → updates for both audiences; cancel obsolete reminder jobs and schedule replacements (R-NTF-06).
  - Reminder jobs are scheduled at `start − ReminderOffsetMinutes` for each audience. Job ids are stored on the booking so they can be cancelled.
  - Handlers are idempotent, and nothing is dispatched for seed data or rolled-back transactions (R-NTF-09).
- **OTP delivery:** the Phase 4 `IOtpSender` gains a WhatsApp authentication-template implementation (fake in dev).
- **In-app notifications:**
  - A `Notification` entity per recipient (user or shop).
  - Created from events.
  - SignalR push.
  - Pages `/account/notifications` (R-CUS-11), `/shop/notifications` (DV-A05) and an admin bell popover; mark-read and mark-all.
- **Admin UI:**
  - `/admin/whatsapp/templates`: two-pane editor with placeholder chips, a WhatsApp bubble preview in TRIMME neutral styling (DV-T06), validate, activate and version history (DV-A12).
  - A test send to an explicitly entered test recipient only; production customer numbers are never prefilled (R-NTF-08).
  - `/admin/whatsapp/dispatches`: filters, masked recipient, template version, error, and retry for failed dispatches (DV-A13).
- **Seed:** templates (ar/en, both audiences, all events) and fake dispatch history.
- **Docs:** `docs/whatsapp-integration.md`, covering Meta setup, template approval, the webhook and the booking/reminder lifecycle Mermaid diagram (R-DOC-03).
- **E2E:** **E4**.

## Explicitly out of scope
Real Meta credentials, and the daily shop summary (D-024, deferred).

## Checklist (100 points)
- [x] 15.1 (5) Re-validate; refine checklist. `AdminOperationsTests` 6/6 and `admin-operations.spec.ts` 6/6 on the restarted stack (its Postgres had exited when Docker restarted). Refinements: the Hangfire schema is installed by `migrate`; reminder job ids live on the notifications side (D-111); two in-app tables (D-112); the Phase 14 carry-over «تواصل مع المحل» is included (D-112).
- [x] 15.2 (8) Hangfire + PostgreSQL storage + secured dashboard (D-108). Lazy explicit storage, `Jobs__Enabled` switch, recurring registrations; `/api/ops/jobs` read-only with `Admin.Jobs.View` and a same-origin CSP. I `JobsDashboard_IsOnlyForAdminsWithThePermission`; the stale idempotency and outbox clean-up and the dispatch retention run on a fake clock in I `RetentionAndMaintenance_…`; E4 opens the recurring jobs through the web origin and an anonymous visitor is refused.
- [x] 15.3 (10) Outbox processor, idempotent consumers, backoff/dead-letter + tests (D-108). I `Outbox_ProcessedOnce_RetriesWithBackoff_AndDeadLetters`; U `Outbox_Backoff_ThenDeadLetter`.
- [x] 15.4 (12) Templates + versions + placeholder validation + renderer (ar/en, both audiences) + unit tests (D-109). U placeholders, rendering (exact `format.ts` strings), version rules, the 18 default slots.
- [x] 15.5 (8) Provider abstraction, fake, Meta adapter (contract-tested with a mocked HTTP handler), webhook with signature verification (D-110). U five provider tests + signature/parse; I `Webhook_VerifiesTheSignature_AndMovesDispatchStatuses`; OTP over WhatsApp I `Otp_OverWhatsApp_…`.
- [x] 15.6 (14) Lifecycle handlers + reminder scheduling/cancellation for both audiences + integration tests (R-NTF-03/04/06/09, D-111). I `CustomerAndProfessionalDispatches_ForLifecycle_AndReschedule_ReplacesReminderJobs`, `Reminder_Idempotent_AndSkipsWhenTheBookingMoved`, `Rollback_NoDispatch_AndSeed_DoesNotDispatch`; U `Professional_NotificationEligibility`.
- [x] 15.7 (6) Dispatch records with template version + history immutability test (R-NTF-07). I `TemplateEdit_AffectsOnlyFutureDispatches_AndFailedDispatchesCanBeRetried`.
- [x] 15.8 (6) Subscription automation jobs + tests (D-113). I `SubscriptionExpiry_NotifiesTheShopAndAdmins_OncePerMilestone`; U milestones.
- [x] 15.9 (10) In-app notifications (entity, events, SignalR, pages) (D-112). `/shop/notifications`, `/account/notifications`, admin bell menu, `/hubs/notifications`; I scoping, mark-read, contact-the-shop, and `NotificationsHubTests` (per-shop and per-user groups, refusals); W panel and bells; E4 shop page.
- [x] 15.10 (12) Admin template editor + test send + dispatch log/retry (DV-A12, DV-A13). W `TemplateEditor` tests; I test send, retry; E4 editor, log, detail, booking section.
- [x] 15.11 (9) E4 E2E (`whatsapp-notifications.spec.ts`), docs (`docs/whatsapp-integration.md` with the lifecycle diagram, domain model, architecture, README, deviations DV-C10), seed (templates by `migrate`, demo dispatch history), gates (full suite 69/69 ×2 on a fresh stack), control files, commit.

## Files/modules expected to change
- `src/Modules/Notifications/**`
- `src/Modules/Bookings/**` (job ids)
- `src/Modules/Subscriptions/**` (jobs)
- `apps/api` (Hangfire)
- web notification pages and the admin WhatsApp pages
- `docs/whatsapp-integration.md`

## Data model and migration impact
- Schema `notifications`: templates, versions, dispatches, notifications, processed_messages.
- The Hangfire schema.
- Migration `20260930065901_Notifications` (also the outbox retry columns and `infra.processed_messages`).

## API contracts and UI routes
As listed, plus `/api/v1/webhooks/whatsapp`.

## Security, tenancy, privacy, RTL, a11y, responsive
- Tokens are never logged; recipients are masked in logs and in the UI.
- Professional messages never include the customer's phone.
- The webhook signature is verified.
- The Hangfire dashboard is not publicly reachable.

## Tests and verification commands
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*NotificationsTests"
dotnet test --project tests/Trimme.UnitTests -c Release --filter-class "*NotificationDomainTests"
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test flows/whatsapp-notifications.spec.ts
```

## Acceptance criteria
- E4 passes.
- A reschedule replaces the reminder jobs.
- A template edit changes only future dispatches.

## Rollback / recovery
Revert the commit. Disable the jobs through configuration.

## Completion evidence
Session 11 (2026-09-30).

| Check | Result |
|---|---|
| Re-validation of Phase 14 | `AdminOperationsTests` 6/6; `admin-operations.spec.ts` 6/6 (after restarting the compose Postgres, which had exited with Docker) |
| `dotnet build Trimme.slnx -c Release` | PASS, 0 warnings, 0 errors |
| Unit tests | PASS 395/395 (378 → 395): `NotificationDomainTests` 9, `WhatsAppProviderTests` 8 (Meta contract with a mocked handler incl. a 3-case theory, fake provider, webhook signature) |
| Architecture tests | PASS 65/65 (63 → 65): `ContactReaders_AreUsedOnlyByNotificationJobs`, `Notifications_Handlers_DoNotContainMessageLiterals` (both probe-verified); the tenancy allow-list gains the dispatch log |
| Integration tests (Testcontainers PostGIS) | PASS 172/172 (158 → 172): `NotificationsTests` 12 (lifecycle and reminder replacement in Hangfire storage, reminder idempotency and skip, template edit immutability and retry, test send, outbox at-least-once, backoff and dead letter, rollback and seed, webhook, subscription expiry, OTP over WhatsApp, jobs dashboard, contact the shop, retention and maintenance jobs on a fake clock) and `NotificationsHubTests` 2 (each shop hears only its own notices, the customer hears the shop, no group can be requested; anonymous and foreign origins refused); the endpoint matrix covers the 22 new endpoints |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| OpenAPI (regenerated) and `pnpm openapi:check` | PASS; 22 operations added, none removed |
| Web `format:check`, `lint`, `typecheck`, `test`, `build` | PASS; 357 tests (337 → 357); `next build` 115 pages |
| E2E `tsc --noEmit`, `prettier --check` | PASS |
| E4 alone on the stack | PASS (24–26 s): both Arabic templates edited and activated, a new customer's booking, both dispatches delivered with the new wording, both reminders at start − 30 min, masked numbers only, the shop's notification, cancel → both reminders cancelled, templates restored, jobs dashboard through the web origin, an anonymous visitor refused |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2` | **69/69 ×2** (runs 1 and 2 on the final code), no retries. API log after both runs: 0 × 5xx, 0 × 429, no warnings or errors. Outbox: 0 pending, 0 dead-lettered. |
| No-transfer grep gate (D-069) and R-NEG-08 gate (the Phase 08 command) | PASS: no-transfer hits only in the two test files that assert absence; R-NEG-08 hits only in seeders, tests and the excluded geocoder |
| gitleaks v8.30.1 `dir` | PASS: no leaks (`git` history scan after the commit is recorded in the follow-up docs commit) |

**Found and fixed during the gates** (the two final full E2E runs used the final product code; the hub, retention and maintenance integration tests were added afterwards and changed no product code):
- **Customer confirmations had lost their buttons.** The default templates shared static button records, and EF's owned JSON entities cannot be shared between owners. Each version now copies its buttons; restoring a version had the same flaw.
- **The endpoint matrix saw `/api/v1/me/notifications/` with a trailing slash.** Group routes with `MapGet("")` keep the slash, so the self-service inbox is mapped with full paths.
- **`Card` dropped extra attributes.** `data-testid` and `aria-labelledby` never reached the DOM (TypeScript accepts hyphenated props silently). `Card` now passes HTML attributes through.
- **Full run 1 on the first rebuild: 66/69.** Three failures, each explained and fixed:
  - Phase 14's booking-page check (no `+966`) caught the masked professional number in the new WhatsApp section. The section lists messages without recipients; the detail page keeps the mask.
  - After sign-out, the bell's count fetch hit 401. The client's session protocol then redirected to sign-in with `returnTo`. The bell now uses a plain `fetch`, like the favorites probe (D-098).
  - The E2 walk-in took the day's first free time, 00:00 on Friday. That belongs to Thursday's business day, because Thursday's window runs past midnight (D-100), so the Friday calendar was empty. This is a date-dependent test flaw that predates this phase. The test now takes the day's last free time.
- **The test-send integration test was flaky** (1 in ~3 runs): `IdentityTestData.NewPhone()` can produce `+96652…`, which the sign-in rule accepts and libphonenumber's mobile check refuses, so the test send answered 400 before looking for the customer. That is safe (nothing is sent to an invalid number). The test now signs up a customer with a number both rules accept.
- **Client-aborted requests were logged as 500** (superseded previews, count fetches during navigation). Serilog request logging now wraps the exception handler, so it records the final status (499 or the mapped 4xx). This also closes the carried-over "Serilog 400/409-as-500".

**Database.** Migration `20260930065901_Notifications`:
- the `notifications` schema: templates, versions, dispatches, reminder schedules, shop and user notifications;
- `infra.processed_messages` and the outbox's `next_attempt_at` and `dead_lettered_at`.

It was applied on fresh compose volumes and from empty in the integration tests. `migrate` installs the Hangfire schema and the 18 default templates. No production migration was run.

## Remaining risks → next phase
- **Meta.** Template approval lead time. Every wording change needs Meta approval before activation, because Meta renders its approved text. The authentication template category has stricter formatting.
- **Delivery in production.** Real WhatsApp delivery is untested without credentials; the adapter is contract-tested only.
- **Single process.** The outbox processor and the Hangfire server run in the API process. Scale-out works through the leader lock and the shared storage, but the in-app SignalR push needs a backplane when the API runs on more than one instance (Phase 17, with the operations hub).
- **Dead letters.** Dead-lettered outbox messages are only logged; there is no admin screen to replay them (Phase 17/18 operations).
- **Resend.** Shops have no "resend confirmation" (DV-C10); the `Shop.Bookings.ResendNotification` permission is unused.
- **Next:** Phase 16, QR codes and attribution analytics.
