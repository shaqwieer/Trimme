# Phase 12 — Customer booking experience & account

**Status:** [ ] · **Score:** 0/100

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
- **Wizard** `/shops/[slug]/book?step=…` (D-028):
  - Service/package step; professional step, including "any" (D-012).
  - Date strip from the availability dates API, and a slot grid of bookable slots only, grouped by period (D-009).
  - Review step with price "يُدفع في المحل" and the policy text from settings (DV-S14).
  - Inline auth sub-steps for guests: phone → OTP → name (D-033).
  - Submit with an idempotency key.
  - Confirmation copy that depends on the resulting status (DV-S13).
  - Conflict state: a typed 409 → a "slot just taken" UI that refreshes slots (DV-A24).
- **Account:**
  - `/account/bookings` with upcoming/past tabs.
  - `/account/bookings/[id]` showing details, `allowedActions`, a cancel dialog with reason chips and cutoff behaviour (D-015, DV-S10), and an `.ics` download.
  - `/account/bookings/[id]/reschedule`: same service and professional; the slot is re-picked and the old slot is held until the new one is committed, atomically in one command.
  - `/account/bookings/[id]/review`: stars, tags and comment.
- **Reviews module:**
  - `Review` with a unique index on booking id, eligibility rules (owner, Completed, within the window), and first-name display.
  - A rating aggregate updated transactionally.
  - Endpoints `POST /me/bookings/{id}/review` and `GET /public/shops/{slug}/reviews`.
  - Tests: R-CUS-09, R-RVW-01.
- **Favorites:**
  - Shops and professionals: `PUT/DELETE /me/favorites/{type}/{id}`, `GET /me/favorites`.
  - `/account/favorites` page, and hearts on the shop and professional pages (R-CUS-10).
- **Profile:** `/account` (name, language, location preference, notification preference placeholder, payment-method read-only row DV-S20, privacy row, sign out). The security page already exists from Phase 4.
- **Web tests:** wizard step state (W), form error mapping (W), E2E flows **E1** (complete), **E6** (UI variant with two browser contexts), **E7**, and R-NEG-02 (no payment step).

## Explicitly out of scope
The notifications center (Phase 15) and QR attribution (Phase 16).

## Checklist (100 points)
- [ ] 12.1 (5) Re-validate; refine checklist.
- [ ] 12.2 (20) Booking wizard (all steps, URL state, any-professional, review, inline auth, idempotent submit, confirmation).
- [ ] 12.3 (6) Conflict UI + slot refresh.
- [ ] 12.4 (12) Bookings list/detail + cancel dialog + `.ics`.
- [ ] 12.5 (10) Reschedule flow.
- [ ] 12.6 (14) Reviews module (API, eligibility, aggregate) + review page + tests.
- [ ] 12.7 (8) Favorites API + UI.
- [ ] 12.8 (6) Account/profile page.
- [ ] 12.9 (12) E2E E1, E6, E7 + no-payment assertion + axe + 3 viewports.
- [ ] 12.10 (7) Gates, control files, commit.

## Files/modules expected to change
- `src/Modules/Reviews/**`
- `src/Modules/Customers/**` (favorites, profile)
- web `(customer)` routes

## Data model and migration impact
- `reviews.reviews` (unique booking_id), rating aggregates, `customers.favorites`.
- Migration `0010_ReviewsFavorites`.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- Owner-only access to bookings and reviews; a foreign ID returns 404.
- The reviewer's name is minimised.
- The wizard is keyboard-accessible; the stepper mirrors in RTL; times and prices are bidi-isolated.

## Tests and verification commands
```
dotnet test --filter "Category=Reviews|Category=Bookings"
pnpm -C apps/web test
pnpm exec playwright test e1 e6 e7
```

## Acceptance criteria
- E1, E6 and E7 pass.
- The review uniqueness constraint holds under a double submit.

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
_(fill)_

## Remaining risks → next phase
Next: Phase 13 — Shop operational dashboard.
