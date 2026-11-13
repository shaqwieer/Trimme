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

**Per-shop switches (D-142).**
- On each shop's admin page, the «رسائل واتساب» tab turns each of the nine messages above on or off for that shop. That covers the customer's 5 and the barber's 4; a barber never gets the pending request.
- All nine are on until an admin turns one off. A message turned off is not sent for any booking of that shop, to the customer or to any of its barbers. It applies whatever the recipient's language.
- The check happens when the message is due, so a scheduled reminder follows the switch as it is at its due time.
- Reading needs `Admin.WhatsApp.View`; changing needs `Admin.WhatsApp.Templates.Edit`, and each change is audited. The shop never sees the setting.
- API: `GET` and `PUT /api/v1/admin/shops/{shopId}/whatsapp-messages`. Stored as opt-outs in `notifications.shop_message_opt_outs`.

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
| `WhatsApp__Meta__GraphApiVersion` | Default `v27.0`, the app's version (D-140) |
| `WhatsApp__Meta__AppSecret` | Verifies the webhook signature; every POST answers 404 without it |
| `WhatsApp__Meta__VerifyToken` | Echo token for the webhook subscription check (GET); the check answers 404 without it, and needs nothing else (D-138) |
| `WhatsApp__Meta__AuthenticationTemplateName` | The approved authentication template for sign-in codes (copy-code button) |
| `WhatsApp__Meta__ButtonBaseUrl` | The base URL registered for URL buttons, usually `https://<domain>/`; the rest of the link is sent as the dynamic suffix |
| `WhatsApp__AllowedRecipients` | Pilot or demo switch (D-140): numbers separated by commas. When set, messages, test sends and sign-in codes go only to them; any other recipient is refused before the provider with `whatsapp.recipient_not_allowed`. The refusal shows as failed in the log, but raises no admin alert and does not mark a professional's number as failing. Unset means everyone. |
| `Identity__Otp__Enabled` | `true` turns sign-in codes on (D-143). Default `false`: customers sign in with their number alone and no code is sent |
| `Identity__Otp__Sender` | `WhatsApp` sends sign-in codes through the authentication template (`DevInbox` locally), while `Identity__Otp__Enabled=true` |
| `Jobs__Enabled` | `false` stops the Hangfire server, the outbox processor and the recurring jobs (the rollback switch); messages then wait in the outbox |
| `Notifications__ContentRetentionDays` | Days to keep rendered text (default 90) |

**The fake provider** records messages in memory with masked numbers. It never stores a sign-in code. It reports every
message delivered, except for these test numbers:
- numbers ending in `0000` fail transiently;
- numbers ending in `9999` fail permanently.

**Meta setup (production, D-140):**
1. Create a Meta Business account, a WhatsApp Business Account (WABA) and a phone number. Note the phone number id and
   the WABA id (App dashboard → WhatsApp → API Setup).
2. Create a system user with `whatsapp_business_messaging` and `whatsapp_business_management`, and generate a permanent
   token (`WhatsApp__Meta__AccessToken`). **Assign the WABA to the system user with full control.** Without that the
   token is valid but sees no account. `GET /me/assigned_whatsapp_business_accounts` lists the accounts it can use.
3. Register the templates from the code. `docs/whatsapp/meta-templates.json` is generated from the default wording by
   `MetaTemplateCatalogTests` (`TRIMME_UPDATE_SNAPSHOTS=1` rewrites it):
   ```bash
   WHATSAPP_ACCESS_TOKEN=… WHATSAPP_WABA_ID=… node tools/whatsapp/register-meta-templates.mjs           # submit the missing ones
   WHATSAPP_ACCESS_TOKEN=… WHATSAPP_WABA_ID=… node tools/whatsapp/register-meta-templates.mjs --status  # list what Meta has
   ```
   - One UTILITY template per slot, named like the version's "Meta template name" (for example
     `trimme_customer_booking_confirmed`), in `ar` and `en`. Body parameters are `{{1}}`, `{{2}}` … in the order the
     placeholders first appear in the TRIMME wording, the order the sender uses. URL buttons are
     `https://groomhere.sa/{{1}}` (`WhatsApp__Meta__ButtonBaseUrl` plus a dynamic suffix).
   - **Meta's rules on a body** (the unit tests check them):
     - **Ends:** a variable cannot open or close the body, and trailing punctuation does not count (`{{7}}.` is refused,
       subcode 2388299).
     - **Length:** keep at least 2.5 words of fixed text per variable. Meta refused 2 and accepted 2.67 ("too many
       variables for its length", 2388293).
   - **Changing the wording.** Meta renders its approved text and fills in the values we send, so they must line up:
     - New wording with the same placeholders in the same order works with the approved template. Only the admin page
       shows the new text.
     - Adding, removing or reordering placeholders needs the new text approved by Meta before the version is activated.
4. Submit the **authentication** template `trimme_sign_in_code` (copy-code button, 5-minute expiry) with the same
   script, and set `WhatsApp__Meta__AuthenticationTemplateName`. **Meta allows authentication templates only after
   business verification** (Business Settings → Security Center). Before that, the submission fails with subcode
   2388185.
5. Configure the webhook `https://<domain>/api/v1/webhooks/whatsapp`:
   - verify token `WhatsApp__Meta__VerifyToken`. Only the token needs to be set for Meta's "Verify and save" (D-138);
   - subscribe to `messages`;
   - set `WhatsApp__Meta__AppSecret` to the app secret. Until then every status update is refused (404);
   - **subscribe the app to the WABA** with `POST /{waba-id}/subscribed_apps`, after the app secret is set. Without
     this, the app's webhook gets nothing for that account.
6. Set `WhatsApp__Provider=Meta`. Send a test from Admin → WhatsApp → Templates to a staff test number. Body parameters
   are sent on one line and never empty: new lines and repeated spaces become one space, and an empty value becomes "-".

**groomhere.sa (2026-11-05).** The account is «Groom here Establishment» (WABA `1798791391452264`). The number is
+966 54 637 9134 (phone number id `1469285809598518`), on the Cloud API with 250 business-initiated conversations a
day until the business is verified. The token and the app secret are only in the server's `.env`.

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
