# WhatsApp, notifications and background jobs

Phase 15 (spec §16, §17; D-108…D-113). The customer's phone number never reaches a shop, a professional or a log. A
professional's number is used only by the notification jobs and by audited admin commands.

## What is sent, to whom

| Event (booking outbox) | Customer | Professional |
|---|---|---|
| Created, Confirmed (or a walk-in starting now) | `BookingConfirmed` | `BookingConfirmed`, the "new booking" alert |
| Created, Pending (shop confirms manually, D-006) | `BookingPending` | nothing yet |
| Pending → Confirmed | `BookingConfirmed` | `BookingConfirmed` |
| Rescheduled | `BookingRescheduled` | `BookingRescheduled`, only when the booking is confirmed |
| Cancelled | `BookingCancelled` | `BookingCancelled`, only if a confirmation was sent to them |
| `ReminderOffsetMinutes` (default 30) before the start | `BookingReminder` | `BookingReminder` |

**Who can receive.**
- **Customer:** a signed-in customer account with a verified mobile; walk-ins have no number. The customer's preferred
  locale picks the template.
- **Professional:** an active professional with a number and notifications on (spec §16). The platform's default locale
  (Arabic) picks the template.

**In-app notices.**
- The shop gets a notice for what customers and admins did; its own actions are not echoed back to it.
- The customer gets a notice for what the shop or the platform did.
- Admins who hold `Admin.WhatsApp.View` get a notice when a message fails for good.
- The shop and the admins who follow subscriptions get subscription warnings.

## Lifecycle

```mermaid
sequenceDiagram
    autonumber
    participant C as Customer / shop / admin
    participant API as Booking command
    participant DB as PostgreSQL
    participant P as Outbox processor (hosted, leader lock)
    participant N as Notifications consumer
    participant H as Hangfire
    participant W as WhatsApp provider (fake / Meta)

    C->>API: create / reschedule / cancel
    API->>DB: booking + booking.* outbox row (one transaction)
    loop every 2 s on one API instance
        P->>DB: due, unprocessed messages (oldest first)
        P->>N: deliver (own transaction, system scope)
        N->>DB: dispatch rows (dedupe key), in-app notices, reminder rows; processed-message record
        N-->>H: after commit: enqueue send jobs, delete obsolete reminder jobs, schedule new ones
    end
    H->>W: SendDispatchJob (one attempt)
    W-->>H: accepted / delivered, or transient / permanent error
    H->>DB: status, attempts, error; retry scheduled with backoff (30 s, 2 min, 10 min, 30 min)
    H->>DB: at start − 30 min: BookingReminderJob re-reads the booking and sends only if still confirmed at that time
    W-->>API: status webhook (sent / delivered / read / failed, signed)
```

**Guarantees and choices:**
- **No message without a committed change.** The outbox row is written in the booking's transaction (D-089). A
  rolled-back command writes none, and the development seed writes none (R-NTF-09).
- **At least once, never twice.** Each consumer runs in its own transaction with its `(message, consumer)` record. Each
  dispatch has a unique dedupe key: `message:audience` for a lifecycle message, the reminder id for a reminder.
- **Failures.**
  - A message that fails is retried with backoff (30 s … 1 h). After 8 attempts it is dead-lettered: it is kept and
    logged as an error, and never retried by itself.
  - A dispatch that fails permanently, or after 5 attempts, is `Failed`; an admin can retry it from the log (audited).
- **Old events.** Messages about an event older than 24 hours are not sent, for example a backlog after an outage.
  Reminders are still reconciled to the booking's current state (D-111).
- **Reminders.**
  - A reminder whose time has already passed is not scheduled.
  - A reminder already scheduled keeps its time when the offset setting changes.
  - A reschedule deletes the old jobs and schedules new ones (R-NTF-06).
- **Sweep.** Every 5 minutes a sweep re-queues work whose job was lost between a commit and the enqueue.
- **History.** A dispatch stores the template version that rendered it. A later edit never changes it (R-NTF-07). The
  rendered text is kept for `Notifications__ContentRetentionDays` (default 90 days); the hash and the version stay.

## Templates

- There is one slot per event × audience × locale: 18 slots (customers 5 events, professionals 4, Arabic and English).
- `migrate` creates any missing slot with a default wording as its active version 1. It never overwrites an edit.
- **Editing** (Admin → WhatsApp → Templates):
  - The draft is edited, validated and previewed with fixed sample data, then activated (`Admin.WhatsApp.Templates.Activate`, audited).
  - Active and archived versions never change.
  - "Restore to draft" copies an older wording into the draft.
- **Placeholders** (whitelist): `customer_name`, `professional_name`, `shop_name`, `service_name`, `booking_date`,
  `booking_time`, `time_remaining`, `duration`, `amount`, `address`, `booking_reference`, `manage_url` (customers only).
  - There is no phone placeholder.
  - Dates and times follow the web rules: Arabic-Indic clock digits and the Gregorian calendar (D-040).
- **Buttons:** up to two, "manage booking" (customers only) or "shop page". The platform builds the links from
  `Web__PublicBaseUrl`.
- **Test send** (`Admin.WhatsApp.TestSend`):
  - The admin types a number, which is never prefilled, and confirms it is a test number.
  - A registered customer's number is refused (409).
  - Sample data only; audited without the number.

## Providers and configuration

Secrets come from environment variables only; nothing is committed.

| Setting | Meaning |
|---|---|
| `WhatsApp__Provider` | `Fake` (the default in Development and Testing; refused elsewhere), `Meta`, or `None` (the default elsewhere: every message fails with `whatsapp.not_configured`, visible in the log) |
| `WhatsApp__Meta__PhoneNumberId` | The WhatsApp Business phone number id |
| `WhatsApp__Meta__AccessToken` | A system-user token with `whatsapp_business_messaging` |
| `WhatsApp__Meta__GraphApiVersion` | Default `v21.0` |
| `WhatsApp__Meta__AppSecret` | Verifies the webhook signature; the webhook answers 404 without it |
| `WhatsApp__Meta__VerifyToken` | Echo token for the webhook subscription check |
| `WhatsApp__Meta__AuthenticationTemplateName` | The approved authentication template for sign-in codes (copy-code button) |
| `WhatsApp__Meta__ButtonBaseUrl` | The base URL registered for URL buttons, usually `https://<domain>/`; the rest of the link is sent as the dynamic suffix |
| `Identity__Otp__Sender` | `WhatsApp` sends sign-in codes through the authentication template (`DevInbox` locally) |
| `Jobs__Enabled` | `false` stops the Hangfire server, the outbox processor and the recurring jobs (the rollback switch); messages then wait in the outbox |
| `Notifications__ContentRetentionDays` | Days to keep rendered text (default 90) |

**The fake provider** records messages in memory with masked numbers. It never stores a sign-in code. It reports every
message delivered, except for these test numbers:
- numbers ending in `0000` fail transiently;
- numbers ending in `9999` fail permanently.

**Meta setup (production):**
1. Create a Meta Business account, a WhatsApp Business Account (WABA) and a phone number. Note the phone number id.
2. Create a system user with `whatsapp_business_messaging`, and generate a permanent token (`WhatsApp__Meta__AccessToken`).
3. Submit one template per slot you use, named like the version's "Meta template name" (for example
   `trimme_customer_booking_confirmed`), in the matching language (`ar` / `en`).
   - Body parameters are `{{1}}`, `{{2}}` … in the order the placeholders first appear in the TRIMME wording.
   - URL buttons use `WhatsApp__Meta__ButtonBaseUrl` plus a dynamic `{{1}}`.
   - Changing the wording in TRIMME requires approving the new text with Meta before activating it, because Meta
     renders its approved text.
4. Submit an **authentication** template with a copy-code button, and set `WhatsApp__Meta__AuthenticationTemplateName`.
5. Configure the webhook `https://<domain>/api/v1/webhooks/whatsapp`:
   - verify token `WhatsApp__Meta__VerifyToken`;
   - subscribe to `messages`;
   - set `WhatsApp__Meta__AppSecret` to the app secret.
6. Set `WhatsApp__Provider=Meta`. Send a test from Admin → WhatsApp → Templates to a staff test number.

**Lead time.** Template approval usually takes minutes to a day. The authentication template category has stricter
formatting rules.

## Operations

- **Jobs dashboard:** `/api/ops/jobs`.
  - Read-only, for platform admins with `Admin.Jobs.View`.
  - It is served by the API and reached through the same `/api` route as the API.
  - It lists scheduled reminders, send jobs, retries and the recurring jobs:
    - `outbox-maintenance`, daily 03:30;
    - `notifications-sweep`, every 5 minutes;
    - `notifications-retention`, daily 04:15;
    - `subscription-expiry`, daily 08:00 on the platform calendar.
- **Storage:** Hangfire uses PostgreSQL schema `hangfire`, installed by `migrate`; the API never installs it at startup.
- **One processor at a time:** the outbox processor holds a PostgreSQL advisory lock on a dedicated connection, so only
  one API instance processes at a time. The others take over if it goes away.
- **Admin log:** Admin → WhatsApp → Message log has filters, the masked recipient, the template version, attempts and
  errors, the delivery rate over the last 24 hours, and the retry. The admin booking page lists a booking's messages
  and reminder jobs.
