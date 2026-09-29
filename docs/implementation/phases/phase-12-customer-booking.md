# Phase 12 — Customer booking experience & account

**Status:** [x] · **Score:** 100/100 (Session 7)

## Goal and user-visible outcome
A customer can:
- book through the wizard (service or package → professional or any → date → slot → review → confirmation);
- see upcoming and past bookings and their details;
- cancel or reschedule within policy;
- add a booking to their calendar;
- review a completed booking once;
- manage favorites and profile.

## Prerequisites
Phase 11 complete.

## In scope
- **Wizard** `/shops/[slug]/book?service|package&pro&date&time&step` (D-028):
  - Service/package step; professional step, including "any" (D-012), listing only the professionals assigned to the offer.
  - Date strip from the availability dates API, and a slot grid of bookable slots only, grouped by period (D-009).
  - Review step with the price "يُدفع في المحل" and the cancellation window from the shop's policy (DV-S14).
  - **Guests confirm through a sign-in round trip** (D-096, refines D-033): the existing OTP pages carry `returnTo` back to the review step, which re-checks the time.
  - Submit with an idempotency key (one per request tuple).
  - Confirmation copy that depends on the resulting status (DV-S13).
  - Conflict state: a typed 409 → a "slot just taken" UI that refreshes slots (DV-A24).
- **Account:**
  - `/account/bookings` with upcoming/past tabs.
  - `/account/bookings/[id]`: details, `allowedActions`, a cancel dialog with reason chips and cutoff behaviour (D-015, DV-S10), an `.ics` download, directions and a countdown.
  - `/account/bookings/[id]/reschedule`: same service and professional; the slot is re-picked from reschedule availability (own time not blocking), and the old slot is held until the new one is committed (D-089).
  - `/account/bookings/[id]/review`: stars, tags and comment.
- **Reviews module:** the customer review command (owner, Completed, within the window, once), atomic aggregate upsert, tags (D-097). `GET /public/shops/{slug}/reviews` exists since Phase 11.
- **Favorites:** shops and professionals, `PUT/DELETE /me/favorites/{shops|professionals}/{id}`, `GET /me/favorites`; `/account/favorites`; hearts on the shop and professional pages (R-CUS-10, D-098).
- **Profile:** `/account` (name, language, notification placeholder, payment-method read-only row DV-S20, privacy and terms rows, security, sign out) and `/account/profile` (name and language).
- **Tests:** wizard step state (W), form/error mapping (W), E2E flows **E1**, **E6** (two browser contexts), **E7**, and R-NEG-02 (no payment step).

## Explicitly out of scope
The notifications center (Phase 15) and QR attribution (Phase 16).

## Checklist (100 points)
- [x] 12.1 (5) Re-validate; refine checklist.
  - The Phase 11 checks are part of every gate below: the Discovery integration namespace, and `public-discovery.spec.ts` in each E2E run. CI for the Phase 11 push (run 36543760813) was green.
  - Refined: the inline auth sub-steps became a sign-in round trip (D-096). The review command, reschedule availability and favorites designs were recorded (D-097, D-098).
- [x] 12.2 (20) Booking wizard: all steps, URL state (links from the shop and professional pages open on the right step), eligible professionals with "any" preselected, date strip, slots by period, review with the price paid at the shop and the policy window.
  - Guest sign-in round trip with `returnTo` (a silent refresh is tried first); profile-incomplete redirect; idempotent submit; status-dependent confirmation on the booking page.
  - Evidence: web tests in `components/booking/booking.test.tsx` and `lib/booking/wizard.test.ts`, and E2E E1.
- [x] 12.3 (6) Conflict UI + slot refresh. A 409 `booking.slot_unavailable` returns to the time step with `notice=conflict`, refetches dates and slots, and clears the taken time. A time from the URL that is gone is caught too (`timeGone`, `dateGone`). Evidence: web tests and E2E E6.
- [x] 12.4 (12) Bookings list/detail + cancel dialog + `.ics`.
  - Upcoming/past tabs with contextual actions, the rebook card and paging.
  - Detail with status, time range, countdown, calendar file, directions, snapshot rows and total, and the policy. After the cutoff: the closed-window text and the shop's phone.
  - Cancel dialog: optional reason chips, version sent, "keep" focused.
  - Evidence: E1 and web tests.
- [x] 12.5 (10) Reschedule flow. `GET /me/bookings/{id}/reschedule/dates|slots` return the booking's own professional and duration, ignore its own interval, and refuse after the cutoff. The page confirms with an idempotency key and returns with "تم تحديث موعدك". Evidence: integration `RescheduleAvailability_…` and E1.
- [x] 12.6 (14) Reviews: API, eligibility, window, tags, atomic aggregate, review page, tests.
  - Integration: `Review_IsForTheCustomersOwnCompletedVisit_OnceWithinTheWindow_AndUpdatesTheRatings`; `Reviews_SubmittedTwiceAtOnce_OrInParallel_KeepOneReviewPerVisit_AndExactTotals` (double submit: one 201 + one 409; six parallel reviews: exact count, sum and histogram for shop and professional).
  - Unit: review action and window, tags. Web: `ReviewForm`. E2E: E7.
- [x] 12.7 (8) Favorites API + UI.
  - Integration `Favorites_AreTheCustomersOwn_Idempotent_AndOnlyForListedShopsAndActiveProfessionals`: another customer can't see, remove or create for the owner; disabled professionals and suspended shops leave the list.
  - Web `FavoriteButton` (signed-out link, optimistic with rollback); E2E favorites test.
- [x] 12.8 (6) Account/profile page. Profile header, settings rows (payment "في المحل"), name and language editing through the profile command (terms kept). E2E.
- [x] 12.9 (12) E2E E1, E6, E7 + favorites/profile + no-payment assertion + axe on every new page + 3 viewports (wizard, appointments, account; no horizontal scroll at 390/768/1440).
- [x] 12.10 (7) Gates, control files, commit.

## Files/modules changed
- **Backend:**
  - `src/Modules/Reviews/**`: review command, tags, `RatingBook` upsert, `ReviewLookup`.
  - `src/Modules/Customers/**`: favorites, the module's first features.
  - `src/Modules/Bookings/**`: `CompletedAt`, Review action, reschedule availability.
  - `src/Modules/Shops/**`: `ShopCards`, discovery id filter.
  - `src/Modules/Services/**`: `professionalIds` on public services and packages.
  - `src/Modules/Availability/**`: probe ignores a booking.
  - Building-block contracts; `ModuleCatalog` order; the `favorites` rate limit; the demo seed (Sara).
- **Migration:** `20260929085259_ReviewTagsAndFavorites` (the plan named it `0010_ReviewsFavorites`): `reviews.reviews.tags`, `customers.favorites`.
- **Web:**
  - `app/[locale]/shops/[slug]/book`, `app/[locale]/account/{page,profile,bookings/**,favorites}`;
  - `components/booking/*`, `components/favorites/FavoriteButton`, `components/account/ProfileForm`;
  - `lib/booking/{wizard,format,links}`; `requireCustomer`; messages ar/en; the test navigation mock (`useSearchParams`).
- **Tests:** integration `Bookings/CustomerAccountTests`, OpenAPI `OpenApi_has_no_payment_surface`, unit `Bookings/CustomerAccountDomainTests`, web tests, E2E `flows/customer-booking.spec.ts`. The Phase 11 E2E review count became "at least 2", because E7 adds one per run.

## Data model and migration impact
- `reviews.reviews.tags text[]` (default empty).
- `customers.favorites`: `id`, `shop_id` (FK shops), `customer_id` (required), `professional_id` (nullable; composite FK `(shop_id, professional_id)` → professionals), `created_at`. Filtered unique indexes per customer and shop, and per customer and professional.
- Additive only.

## API contracts and UI routes
- **API (new):**
  - `POST /me/bookings/{id}/review`;
  - `GET /me/bookings/{id}/reschedule/dates|slots`;
  - `GET /me/favorites`, `PUT|DELETE /me/favorites/shops/{id}`, `PUT|DELETE /me/favorites/professionals/{id}`.
  All are customer-only (authorization matrix).
- **API (changed):** `CustomerBookingResponse` gained `reviewRating` and `reviewDeadline`, and the `Review` action. Public services and packages gained `professionalIds`.
- **UI:** `/shops/[slug]/book`, `/account`, `/account/profile`, `/account/bookings`, `/account/bookings/[id]`, `/account/bookings/[id]/reschedule`, `/account/bookings/[id]/review`, `/account/favorites`.

## Security, tenancy, privacy, RTL, a11y, responsive
- **Owner-only access.** Bookings, reviews and favorites are read through the customer filter (D-085); a foreign id is 404, and the data layer refuses writes for another customer (integration).
- **Privacy.** The reviewer's name is minimised (first name + initial). The customer's phone never appears in any new response (asserted in the favorites and review tests).
- **Hearts.** They never use the session client for anonymous visitors.
- **Accessibility.** The wizard uses native radios with a focus move to the step heading. The stepper mirrors in RTL, and times and prices use the D-040 digit rules. axe finds nothing serious on the wizard steps, bookings list and detail, reschedule, review, favorites, account and the conflict state.
- **Layout.** No horizontal scroll at 390/768/1440.

## Tests and verification commands
Smallest decisive re-validation (for the next session):
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*CustomerAccountTests"
dotnet test --project tests/Trimme.UnitTests -c Release --filter-class "*CustomerAccountDomainTests"
pnpm -C apps/web vitest run src/lib/booking src/components/booking
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 npx playwright test flows/customer-booking.spec.ts
```

## Acceptance criteria
- E1, E6 and E7 pass. ✔
- The review uniqueness constraint holds under a double submit. ✔ (integration: one 201 + one 409)

## Rollback / recovery
Revert the commit, then drop and recreate the volume. The migration is additive.

## Completion evidence
Session 7, 2026-09-29.

**Re-validation.** Phase 11 was pushed at the start of the session; CI run 36543760813 on `4f441de` succeeded. The Phase 11 integration and E2E checks run inside every gate below.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build -c Release` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration | PASS — **377 / 63 / 143** (Phase 11 end: 373 / 63 / 138). New: 4 unit (`CustomerAccountDomainTests`), 5 integration (`CustomerAccountTests` ×4, `OpenApi_has_no_payment_surface`); the full integration suite ran alone (2 min) |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — **282** web tests (25 new); `/[locale]/shops/[slug]/book` rendered per request |
| Fresh `down -v` + `up --build`, then `pnpm e2e` (before the demo rename, see below) | **60/60, 60/60, 60/60**; 0 HTTP 429, 0 HTTP 5xx |
| Fresh `down -v` + `up --build` with the final code, then `pnpm e2e` | Run 1: 59/60. The failure was the Phase 07 services test: a toggle click did not flip within 5 s on the cold stack. It passed in all five other full runs. **Runs 2–4: 60/60, 60/60, 60/60**; 0 HTTP 429, 0 HTTP 5xx |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` | PASS after two fixes: a code comment used the forbidden term, and the new demo surname «الشهري» contains «شهري» (renamed to «العنزي»). gitleaks: no leaks |

**Found and fixed while verifying**
- **Favorites cap.** `IShopCards` read at most 200 candidates, while a customer can save up to 200 shops plus the shops of 200 professionals. It now reads in chunks (advisor review).
- **Lint (React 19 rules).** The wizard set state in an effect; the "gone/conflict" notice moved into the URL. The booking page called `Date.now()` in render; the countdown decides for itself.
- **E2E.** The favorites test left the page before the optimistic heart's PUT finished; it now waits for the API response. The Phase 11 review count assertion now allows the reviews E7 adds.
- **Flakes seen under the heavier parallel load** (pre-existing tests, not Phase 12 code): E5 (subscription card refresh, dev run) and the Phase 07 services toggle (final run 1). Each passed on rerun and in every other full run. Watch item for Phase 17/18.

## Remaining risks → next phase
- The wizard offers only bookable dates and slots, without disabled reasons (D-009). "Any professional" is assigned at submit and does not retry on a lost race (carry-over).
- The WhatsApp notification preference row is a placeholder until Phase 15.
- Sara's review E2E needs a volume seeded within the last seven days (README).
- Next: Phase 13 — Shop operational dashboard.
