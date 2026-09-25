# Phase 15 — WhatsApp, outbox processing, Hangfire & notifications

**Status:** [ ] · **Score:** 0/100

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
  - A dashboard at `/admin/jobs`, restricted to the admin permission and served behind the API auth.
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
- [ ] 15.1 (5) Re-validate; refine checklist.
- [ ] 15.2 (8) Hangfire + PostgreSQL storage + secured dashboard.
- [ ] 15.3 (10) Outbox processor, idempotent consumers, backoff/dead-letter + tests.
- [ ] 15.4 (12) Templates + versions + placeholder validation + renderer (ar/en, both audiences) + unit tests.
- [ ] 15.5 (8) Provider abstraction, fake, Meta adapter (contract-tested with a mocked HTTP handler), webhook stub.
- [ ] 15.6 (14) Lifecycle handlers + reminder scheduling/cancellation for both audiences + integration tests (R-NTF-03/04/06/09).
- [ ] 15.7 (6) Dispatch records with template version + history immutability test (R-NTF-07).
- [ ] 15.8 (6) Subscription automation jobs + tests.
- [ ] 15.9 (10) In-app notifications (entity, events, SignalR, pages).
- [ ] 15.10 (12) Admin template editor + test send + dispatch log/retry.
- [ ] 15.11 (9) E4 E2E, docs, seed, gates, control files, commit.

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
- Migration `0012_Notifications`.

## API contracts and UI routes
As listed, plus `/api/v1/webhooks/whatsapp`.

## Security, tenancy, privacy, RTL, a11y, responsive
- Tokens are never logged; recipients are masked in logs and in the UI.
- Professional messages never include the customer's phone.
- The webhook signature is verified.
- The Hangfire dashboard is not publicly reachable.

## Tests and verification commands
```
dotnet test --filter "Category=Notifications|Category=Outbox"
pnpm exec playwright test e4
```

## Acceptance criteria
- E4 passes.
- A reschedule replaces the reminder jobs.
- A template edit changes only future dispatches.

## Rollback / recovery
Revert the commit. Disable the jobs through configuration.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- Meta template approval lead time.
- The OTP authentication template category.
- Next: Phase 16 — QR & attribution.
