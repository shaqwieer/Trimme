# Phase 08 — Subscriptions foundation & platform settings

**Status:** [x] · **Score:** 100/100

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
- [x] 8.1 (5) Re-validate (Services integration 8/8 on the committed Phase 07 code; CI run 36323449600 green) and refine the checklist. The design review covered month-end drift, status by coverage, database guarantees, 409 on unique races, and enforcement for "no subscription".
- [x] 8.2 (14) `SubscriptionPlan` + append-only `PlanPrice` versions (D-079), with unit tests for versioning, history rules, publish/archive and interval bounds.
- [x] 8.3 (12) `ShopSubscription` with periods (plan name + price-version snapshot), renewals and overrides (D-077). Integration tests: snapshot kept, renewal takes the new version, concurrent assign → 409, overlap/gap, override on the period in force, audit.
- [x] 8.4 (8) Status calculator on the platform calendar, with boundary tests (threshold, last day, suspended, none; month-end chains without drift; a moving-clock integration test).
- [x] 8.5 (10) `PlatformSettings` model, synchronizer (inserts defaults, never overwrites), API, audit with the changed fields, version check, and tests (D-076).
- [x] 8.6 (9) `IShopBookability` over the coverage read model (D-078), with tests: none, expired and suspended are blocked; expiring soon is allowed; an inactive shop is blocked; enforcement `None` allows. "Future bookings untouched" is asserted again in Phase 10.
- [x] 8.7 (8) Permission tests: plans and overrides are SuperAdmin-only; Operations Manager, Support and shops are denied. Settings edit is denied to Operations Manager and shops.
- [x] 8.8 (16) SuperAdmin plan list (with ordering), editor, availability actions and price-history timeline with an add-version form (DV-A10). Admin subscriptions page with KPIs, filter and search. The shop page's subscription tab with activation/renewal, suspension, override and history (DV-A11, DV-S05).
- [x] 8.9 (8) Shop subscription view with the expiring/expired/suspended warning and renewal history; minimal platform settings page.
- [x] 8.10 (10) Seed (3 plans, versioned prices, one subscription per status), E5 + E3 subscription-part E2E, R-NEG-08 grep gate, control files, commit.

## Files/modules expected to change
`src/Modules/Subscriptions/**`, `src/Modules/Administration/**` (settings), web admin/shop subscription routes.

## Data model and migration impact
- Schema `subscriptions` (plans, plan_prices, shop_subscriptions, renewals, overrides) and `administration.platform_settings`.
- Migration `SubscriptionsSettings` (`20260927141816`):
  - `subscriptions.subscription_plans` (features as JSON);
  - `plan_prices` (unique per date and per version number);
  - `shop_subscriptions` (unique `shop_id`, shop-scoped key);
  - `subscription_periods` and `subscription_overrides` (composite same-shop FKs, restrict deletes);
  - `subscription_coverage` (read model);
  - `administration.platform_settings` (singleton + range CHECKs).

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
Smallest decisive re-validation (for the next session):
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Subscriptions"
dotnet test --project tests/Trimme.UnitTests -c Release --filter-namespace "*Subscriptions"
E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 pnpm -C tests/E2E exec playwright test flows/subscriptions.spec.ts
```

## Acceptance criteria
- E5 proves that changing a plan's future price leaves existing subscription history unchanged.
- Non-SuperAdmin users are denied.

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
Session 4, 2026-09-27 (same session as Phases 06–07; the user asked to push Phase 07, confirm CI, then proceed).

**What was built**
- **Subscriptions module.**
  - Domain: `SubscriptionPlan`, `PlanPrice`, `ShopSubscription` + `SubscriptionPeriod` + `SubscriptionOverride`, `SubscriptionCoverage`, `SubscriptionDates` (inclusive end; month-end clamp to the month end, so chains never drift), `SubscriptionStatusCalculator`.
  - Use cases:
    - SuperAdmin plans: CRUD, publish/deactivate/archive, ordering, price versions.
    - Admin subscriptions: list with SQL status filters and counts over every shop, shop detail, assign, renew, override, suspend, reinstate.
    - The shop's own view: tenant from claims, no override reasons.
  - `IShopBookability` (D-078) and the demo seeder (3 plans, one subscription per status).
- **Administration module.** `PlatformSettings` (one typed row), `PlatformSettingsSynchronizer`, the `IPlatformSettings` reader, and `/admin/settings` GET/PUT.
- **Building blocks.**
  - `IPlatformSettings`, `IShopBookability` and `SubscriptionEnforcement` contracts.
  - `IShopDirectory.SearchIdsAsync`/`CountAsync`.
  - A unique violation now answers 409 (D-080).
  - Two extra demo shops without users (`DemoData.ExtraShops`).
- **Web.**
  - `/admin/subscription-plans` (+ `/new`, `/[id]`), `/admin/subscriptions`, the shop page's Subscription tab, `/shop/subscription`, `/admin/settings`.
  - `components/subscriptions/*`: previews mirror the server's date rules and are tested; durations and prices always come from plan data.
  - A `None` subscription badge.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration tests | PASS — 206 / 63 / 113 at the commit; 114 integration after the review follow-up (Phase 07 end: 171 / 63 / 106) |
| New unit suite `Subscriptions/SubscriptionDomainTests` (35) | End dates (incl. 31 Jan, 29 Feb + 1 year, a 14-period monthly chain); status boundaries (15 days left = Active, 14 = ExpiringSoon, the last day is covered, the day after = Expired, Suspended wins, None); Riyadh midnight; the price version in force by date; history rules (past date, same date, amount, currency, archived plan); publish/archive; assign/renew/override rules; settings defaults and changed-field names |
| New integration suite `Subscriptions/SubscriptionTests` (7; 8 with the follow-up `PlanFeatures_RoundTripThroughTheDatabase_IncludingAnEmptyList`) + extended `DemoSeedTests` | **Plans:** CRUD with the JSON feature list round-tripping; stale version → 409; a future version becomes current once the clock moves; archive is final; audit text. **Permissions:** Ops, Support and shop denied. **History:** the snapshot is kept after a new price and a plan rename; the renewal takes version 2; two concurrent assigns give one 200 + one 409; overlap, gap, override, suspend and reinstate, with the audit sequence (no phone numbers). **Clock:** status and bookability on a moving clock, SQL-filtered list and counts, enforcement `None`. **Settings:** defaults, ranges, 409, audit, and `migrate` keeps an admin's edits. **Tenancy:** a shop sees only its own subscription. **Seed:** yields Active, ExpiringSoon, Expired and Suspended |
| Integration suite repeat runs | 113/113 on 5 of 6 full runs. The first full run after adding the suite had 3 failures at about 37 s (gallery, professional immutability, one subscription test), which did not reproduce. This is the same pattern as the Phase 07 watch item |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — 212 web tests. 16 are new: period/price previews, plan form, record-period form (incl. plan filtering and API field errors), settings form, and the suspend dialog conflict |
| Compose on the Phase 07 volume (upgrade) | PASS — migration applied, settings row synchronized, 6 seeders ran, 4 coverage rows (Active, ExpiringSoon, Expired, Suspended) |
| `pnpm e2e` on fresh `down -v` + `up --build` stacks | **Stack A: 44/45, 45/45, 44/45.** The two failures were a Phase 06 map pin not rendered within 5 s on the cold stack, and the E5 flow suspending before the renewal's refresh (a real 409, whose message was hidden behind the dialog). Both are fixed: the error now shows inside the dialog (with a web test), the flow waits for the refreshed history, and the map-pin wait is 15 s. **Stack B (fresh, all fixes): 44/45, 45/45, 45/45.** The one failure was the first run on the cold stack: a Phase 04 OTP redirect took longer than 5 s |
| No-transfer grep gate (D-069) | PASS — no hits |
| R-NEG-08 grep gate (see below) | PASS — no hits. `Geocoding/` is excluded because its 1100 ms throttle constant is not plan data |
| gitleaks `dir` + `git` | PASS — no leaks |
| Visual check at 1440 and 390: `/ar/admin/subscriptions`, the shop page's subscription tab, `/ar/admin/subscription-plans(/{id})`, `/ar/admin/settings`, `/ar/shop/subscription` | RTL mirrored; 0 px horizontal overflow at 390. Fixed a missing progress-track token and the history order for periods recorded at the same moment |

R-NEG-08 gate command:

```
rg -n "\b(1900|2400|1100|199)(\.0+)?\b|نصف سنوي|سنوي|شهري|Semi-annual|\bAnnual\b|\bMonthly\b|٣ أشهر|٦ أشهر|3 months|6 months|12 months" \
  apps/web/src apps/web/messages src -g '!**/Seeding/**' -g '!**/*.test.*' -g '!**/bin/**' -g '!**/obj/**' \
  -g '!**/schema.d.ts' -g '!**/Geocoding/**'
```

**Review follow-up (after commit `2f2b42c`)**
- Added `PlanFeatures_RoundTripThroughTheDatabase_IncludingAnEmptyList`: an empty feature list and edited features re-read from the database, alone and in the list. It passed (Subscriptions namespace 8/8), so no mapping change was needed.
- The verification commands above now name the real test commands.
- D-077 recorded the pricing rule for custom durations and back-dating as an open question; the user then decided it (see below).

**User decisions (Session 4, after the review) — implemented and verified**
- **D-078 confirmed.** Shops without a subscription in force stay hidden and take no online bookings. The enforcement setting `None` remains available for a temporary warning-only rollout.
- **D-081 implemented.** A custom duration, a start before today or an explicit price needs a SuperAdmin override with an explicit total price and a reason.
  - The domain refuses such a period without custom pricing, and the API answers 403 `subscription.custom_pricing_required` to other admins.
  - The period keeps the explicit total, the plan price it replaces and the reason (migration `SubscriptionCustomPeriods`: `pricing_reason`, `standard_amount`). The audit entry says "SuperAdmin custom period, total X instead of Y" with the reason.
  - The web form offers custom days and past dates to SuperAdmin only, with total and reason fields. The history shows the custom price, its reason and the plan price.
  - The demo seed records history as of each period's own start date.
- **Found and fixed while verifying:**
  - **Stale start date after a refresh.** After a renewal, the form kept the old default start date, so a second renewal from the same page was refused as an overlap. It now follows the refreshed value (web test).
  - **Phase 06 map-pin race.** A pin chosen before the lazily loaded map finished initialising was never shown. This was the real cause of the "map pin not visible" E2E failures that looked like cold-stack slowness. Map creation now uses the latest pin. A regression test fails on the old code and passes now, and the temporary 15 s E2E wait is removed.
- **Evidence:**

| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings |
| Unit / architecture / integration | PASS — 206 / 63 / 114 |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — 217 web tests |
| Fresh `down -v` + `up --build`, `pnpm e2e` ×3 | PASS — **45/45, 45/45, 45/45** (including the first run on the cold stack) |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` + `git` | PASS — no hits, no leaks |
| GitHub Actions on `8ccef02` (pushed at the user's request) | PASS — run 36333734614: web, gitleaks, backend (incl. integration and migrations), compose + Playwright smoke |

**Findings during the gates**
- **Two "Suspend" buttons.** The shop page showed one for the shop and one for the subscription. The subscription actions are now "Suspend subscription" and "Reinstate subscription".
- **Cold-stack timing.** The first E2E run on a freshly built stack sometimes exceeds a 5 s wait in an older flow (map pin, OTP redirect); later runs on the same stack pass. This is recorded as a watch item, and CI retries once.

## Remaining risks → next phase
- **Enforcement defaults** (D-014, D-078) are overridable. With the default, a shop without a subscription in force is hidden and takes no online bookings, so test setup in Phases 10–12 must assign one (or switch the setting to `None`).
- **Not enforced yet:** grace/trial days and plan limits are stored but not applied (D-077).
- **Later phases:** automated expiry warnings (notifications) come in Phase 15; "future bookings untouched on expiry" is asserted in Phase 10.
- **Watch item:** the integration timeouts and cold-stack E2E waits continue.
- **Next:** Phase 09 — Schedules & availability engine. It reads `IPlatformSettings` for the lead time, horizon and slot step.
