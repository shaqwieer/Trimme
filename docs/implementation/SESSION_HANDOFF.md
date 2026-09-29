# TRIMME Session Handoff

- **Updated:** 2026-09-29 (end of Session 7: Phase 12)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Phase 11 was pushed at the start of this session (`79280f6..4f441de`); CI run 36543760813 succeeded (recorded in the Phase 11 file).
  - Phase 12 is **committed locally and not pushed**. Push only when the user asks, then record the CI run in the Phase 12 file.
- **HEAD commit:** `f8fa5be` (`feat: phase 12 customer booking and account`), then `11d76ef` (review follow-up fix), then a `docs:` commit recording both. Run `git log --oneline -4`.
- **Working tree:** clean after the commit.
- **Local Docker stack: running.** It was recreated from an empty volume with the final images (11 migrations, 10 seeders), and E2E ran three times on it. Sara has three reviewed visits and five left; each demo customer has used 3 of its 5 OTP codes this hour.
  - Ports: web 3300, API 8080, DB 5434, Mailpit UI 8325.
  - Started with `TRIMME_SITE_URL=http://localhost:3300`.
- **Current phase:** 12 is complete. Phase 13 has not started.
- **Phase score:** 100 / 100 (Phase 12).
- **Last fully completed phase:** 12, customer booking and account.

## Completed this session
Phase 12 (`phases/phase-12-customer-booking.md`):
- **Booking wizard** `/shops/[slug]/book` (D-028, D-096):
  - Steps: service or package → professional (eligible only, "any" preselected) → date strip → slots by period → review (price paid at the shop, the policy window, optional note).
  - The URL holds every choice, so links from the shop and professional pages open on the right step.
  - Guests sign in when they confirm and come back to the re-checked review.
  - The submit uses one idempotency key per request. A 409 returns the customer to fresh times with "just taken".
  - After booking, the customer lands on the booking page with Confirmed or Pending copy.
- **Account:**
  - `/account/bookings`: upcoming/past, contextual actions, rebook card.
  - The booking page: countdown, `.ics`, directions, snapshot, the policy (after the cutoff: closed window + shop phone), cancel dialog with reasons.
  - `/reschedule`, `/review`, `/account/favorites`, and the `/account` profile rows plus `/account/profile` (name, language).
- **API:**
  - Review command: owner, Completed, 7-day window, once; tags; an atomic aggregate upsert (D-097).
  - Reschedule dates and slots, ignoring the booking's own interval.
  - `Review` action, `reviewRating` and `reviewDeadline` on customer bookings.
  - Favorites (Customers module, customer-owned, composite FK to the professional; D-098). `IShopCards` (discovery pipeline by ids, chunked).
  - `professionalIds` on public services and packages.
  - `favorites` rate limit.
- **Hearts** on the shop and professional pages. They probe with a plain fetch, so a guest sees a sign-in link; staff see none.
- **Demo seed:** Sara (`+966500100303`), with eight unreviewed visits (one to four days before seeding) and one upcoming booking, for E7.
- **Tests:**
  - integration `CustomerAccountTests` (reviews, double submit and parallel totals, favorites isolation, reschedule availability) and `OpenApi_has_no_payment_surface`;
  - unit `CustomerAccountDomainTests`;
  - 25 web tests;
  - E2E `customer-booking.spec.ts`: E1, E6, E7, favorites and profile, viewports.

## Verification evidence
| Command | Result |
|---|---|
| `dotnet build -c Release` | PASS, 0 warnings |
| Unit / architecture / integration | PASS, 377 / 63 / 143 (the full integration suite ran alone: 2 min) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web gates (`lint`, `typecheck`, `format:check`, `openapi:check`, `test`, `build`) | PASS, 282 web tests |
| Fresh `down -v` + `up --build` + `pnpm e2e` | First stack: **60/60 ×3**. Phase commit (after the demo rename): run 1 59/60 (a Phase 07 services toggle flake on the cold stack), then **runs 2–4 60/60 ×3**. Review follow-up (fresh stack): **60/60 ×3**. 0 HTTP 429, 0 HTTP 5xx |
| Review follow-up (`11d76ef`) | A heart stayed "signed out" after the sign-in round trip (module cache across client navigation): fixed, with a web test that fails on the old code and an E2E round trip. The Pending copy and the after-cutoff policy now have web tests. 285 web tests |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` and `git` (42 commits) | PASS after renaming the demo surname «الشهري» (contains «شهري») and rewording one comment; no leaks |

## Database and migrations
- Created this session: `20260929085259_ReviewTagsAndFavorites` (`reviews.reviews.tags`, `customers.favorites`).
- Applied locally only: Testcontainers databases and the compose volume.

## Decisions added
- D-096: guests confirm through a sign-in round trip (supersedes the inline part of D-033); idempotency key per request; conflict notice in the URL.
- D-097: review command, window and tags; atomic aggregates; reschedule availability; Sara's demo visits.
- D-098: favorites (model, API, hearts, module order).
- Design deviations: DV-S10 (UI), DV-S13, DV-S20 and DV-A24 applied. New: DV-C06 (wizard) and DV-C07 (account and appointments). DV-C05 updated (the heart is next to the shop name).

## Known issues or blockers
- **Flaky pre-existing E2E under load.**
  - Phase 07 services toggle: a click did not flip within 5 s on a cold stack, once in six full runs.
  - Phase 08 E5: the subscription card was not refreshed within 5 s, once in a development run.
  - Both passed on rerun. Suspected: a click before hydration, or a slow `router.refresh()` under parallel load. Worth a look in Phase 17/18.
- **Sara's reviewable visits expire seven days after seeding.** E7 then fails with a clear message; reset the volume.
- **At most five E2E runs per hour on one stack.** Each run signs in the demo customers Noura, Khalid and Sara once, and the OTP limit is 5 codes per number per hour. Recreate the stack (`down -v`) or wait between batches.
- **Integration suite:** run it alone (host load causes Npgsql timeouts; see Phase 11).
- **Production must-haves** (Phase 17): unchanged from Phase 11 (Nginx `X-Forwarded-For` and `KnownNetworks`, web reachable only via Nginx, `TRIMME_SITE_URL`, log retention of `lat`/`lng`, tile and geocoder host).
- **Carry-overs:**
  - "any professional" is assigned at submit and does not retry;
  - packages across professionals (D-020);
  - outbox processing and the WhatsApp preference row (Phase 15);
  - grace days and limits (D-077);
  - Serilog 400/409-as-500 and oversized uploads (Phase 17);
  - drag-and-drop ordering deferred.
- **Ports:** use `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.

## Exact next action
1. If the user asks, push `main` and record the CI run in the Phase 12 file.
2. Start Phase 13 (`phases/phase-13-shop-dashboard.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*CustomerAccountTests"`
   - `cd tests/E2E && E2E_BASE_URL=http://localhost:3300 npx playwright test flows/customer-booking.spec.ts` (on a volume seeded within seven days)
3. Phase 13 builds on:
   - the shop bookings API (Phase 10: list, detail, transitions, notes, walk-in);
   - `CustomerBookingAction`/`allowedTransitions` patterns;
   - the booking UI components (`components/booking/*`, `StatusBadge`, `SlotGrid`, `DateStrip`).
   Keep Faisal free (schedule E2E), and use Omar and Majed at Barber House carefully: E1/E6 book Omar 3–13 days ahead and cancel, and E7 uses Majed's past visits.

## Files intentionally left modified
- None.
