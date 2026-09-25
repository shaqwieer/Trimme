# Phase 08 — Subscriptions foundation & platform settings

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
- SuperAdmin manages the subscription-plan catalogue and its versioned prices.
- Authorised admins assign plans to shops, renew them and apply audited overrides, with full history.
- Shops see their subscription status and expiry.
- Platform settings exist as typed, audited configuration that later phases read: booking policy, thresholds, enforcement, reminder offset, locale/currency/time zone, and map defaults.

## Prerequisites
Phase 07 complete.

## In scope
- **`SubscriptionPlan`:**
  - Localized name/description, features (localized list), limits (typed key/values, e.g. max professionals).
  - Trial/grace days (optional), billing interval (days/months), published, available-to-new-shops, display order, archived.
  - Concurrency.
- **`SubscriptionPlanPrice`:**
  - Versioned: amount, currency, effective-from, created-by.
  - Immutable once used.
  - A new version applies only to future assignments and renewals (R-SUB-02).
- **`ShopSubscription`:**
  - Plan, **price snapshot** (plan price version id + amount + currency), start, end, status.
  - Manual activation record: no payment (spec §15).
- **`SubscriptionRenewal`** (history).
- **`SubscriptionOverride`:**
  - Shop-specific price or duration.
  - SuperAdmin-only, reason required, audited.
- **Status calculator:**
  - Active / ExpiringSoon (threshold setting) / Expired / Suspended.
  - Unit tests at boundaries in the shop time zone.
- **`PlatformSettings`** (typed sections, versioned or audited):
  - `BookingPolicy`: lead time, horizon days, slot step minutes (5), cancellation cutoff, review window days.
  - `ReminderOffsetMinutes` = 30.
  - `ExpiringSoonThresholdDays`.
  - `ExpiredSubscriptionEnforcement` (D-014).
  - `HidePausedShopsFromDiscovery` (D-013).
  - Locale defaults, currency SAR, time zone.
  - Map defaults (centre and zoom for Riyadh).
- **Enforcement hook:** `IShopBookability` answers "can accept new online bookings?" and "visible in discovery?". It is consumed by Phases 9–11. Future bookings are never touched (R-SUB-05).
- **API:**
  - `/admin/subscription-plans` CRUD + `/prices` (permission `SuperAdmin.SubscriptionPlans.Manage`).
  - `/admin/shops/{id}/subscription`: assign, renew, suspend, reinstate, override (override is SuperAdmin-only), history.
  - `/admin/subscriptions` list with status filters.
  - `/shop/subscription` read.
  - `/admin/settings` get/put (the section UI is completed in Phase 14).
- **Web:**
  - SuperAdmin plans list/editor and price-history timeline (DV-A10).
  - Admin subscriptions page (a-subs) with renew/assign/override (DV-A11, DV-S05).
  - Shop `/shop/subscription` card with an expiry warning.
  - A minimal settings page for booking policy, reminder offset and thresholds.
- **Seed:**
  - Multiple plans with versioned price examples.
  - Subscriptions: Active, ExpiringSoon, Expired and Suspended examples.
- **Tests:** R-SUB-01..05, R-AUTH-10, R-NEG-08, and E2E flows E5 and E3 (subscription part → E3 complete).

## Explicitly out of scope
Automated expiry jobs and warning notifications (Phase 15), and payment collection (never in v1).

## Checklist (100 points)
- [ ] 8.1 (5) Re-validate; refine checklist.
- [ ] 8.2 (14) Plan + versioned price aggregates + unit tests.
- [ ] 8.3 (12) ShopSubscription, renewal, override, snapshots + integration tests.
- [ ] 8.4 (8) Status calculator + boundary tests.
- [ ] 8.5 (10) PlatformSettings model, API, audit, defaults + tests.
- [ ] 8.6 (9) `IShopBookability` enforcement service + tests (future bookings untouched is asserted again in Phase 10).
- [ ] 8.7 (8) Permission tests: SuperAdmin-only plans/overrides; Ops/Support/shop denied.
- [ ] 8.8 (16) SuperAdmin plan editor + price history UI; admin subscriptions UI.
- [ ] 8.9 (8) Shop subscription view + settings page (minimal).
- [ ] 8.10 (10) Seed, E5 + E3 complete E2E, grep gate for hardcoded plan prices, control files, commit.

## Files/modules expected to change
`src/Modules/Subscriptions/**`, `src/Modules/Administration/**` (settings), web admin/shop subscription routes.

## Data model and migration impact
- Schema `subscriptions` (plans, plan_prices, shop_subscriptions, renewals, overrides) and `administration.platform_settings`.
- Migration `0006_SubscriptionsSettings`.

## API contracts and UI routes
- `/admin/subscription-plans(/new|/[id])`
- `/admin/subscriptions`
- `/admin/shops/[id]` (subscription tab)
- `/shop/subscription`
- `/admin/settings`

## Security, tenancy, privacy, RTL, a11y, responsive
- The permission split is enforced server-side.
- Price history is append-only.
- Money uses `decimal` + currency and is never floating-point.
- Currency is formatted per locale.

## Tests and verification commands
```
dotnet test --filter Category=Subscriptions
pnpm exec playwright test superadmin-plans admin-subscriptions
```

## Acceptance criteria
- E5 proves that changing a plan's future price leaves existing subscription history unchanged.
- Non-SuperAdmin users are denied.

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- Enforcement defaults (D-014) need confirming.
- Next: Phase 09 — Schedules & availability engine.
