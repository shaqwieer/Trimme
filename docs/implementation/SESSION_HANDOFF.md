# TRIMME Session Handoff

- **Updated:** 2026-09-29 (end of Session 6: Phase 11)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). Phase 10 is pushed and CI-green (run 36420287372). Phase 11 is **committed locally and not pushed**. Push only when the user asks; then record the CI run in the Phase 11 file.
- **HEAD commit:** `feat: phase 11 public discovery and shop pages` (run `git log --oneline -3`).
- **Working tree:** clean after the commit.
- **Local Docker stack: running.** It was recreated from an empty volume with the Phase 11 images (10 migrations, 10 seeders). Ports: web 3300, API 8080, DB 5434, Mailpit UI 8325, started with `TRIMME_SITE_URL=http://localhost:3300`.
- **Current phase:** 11 is complete. Phase 12 has not started.
- **Phase score:** 100 / 100 (Phase 11).
- **Last fully completed phase:** 11, public discovery and shop pages.

## Completed this session
Phase 11 (`phases/phase-11-public-discovery.md`):
- **Discovery API** (D-090/D-091):
  - PostGIS nearby search (nearest first, radius, city);
  - Arabic-normalized text search over shop names and offers;
  - category, open-now, verified, bookable-today and price filters; distance, rating or earliest sort; paging;
  - a bounded availability probe that takes each professional's shortest bookable offer;
  - popular categories ("from X"), stats, areas, sitemap data and top professionals.
  All of it reads through a new multi-shop public scope with a fixed number of queries.
- **Public pages API.** The shop detail is extended (rating, prices, hours, cancellation policy, listed flag). New endpoints: the live `/status` (open now, booking state, each professional's next time), professional detail and next slots, and paged reviews.
- **Reviews** (D-092). A `Review` entity (shop- and customer-owned, one per booking, first name + initial), plus rating aggregates as a platform read model, updated with the review by `RatingBook`. Six demo visits have reviews. The review *command* is Phase 12.
- **Caching** (D-093). The API output-caches anonymous public reads for 5 minutes, and saving any `IPublicContent` entity evicts them. The web reads public data with a cookie-less, `no-store` client that forwards the visitor's address (D-094).
- **Web:**
  - the landing page, `/shops`, `/discover`, `/search` (list, map, filter drawer), `/onboarding/location` and the location sheet (the cookie stays on the device, D-095);
  - the shop page (all sections), the professional page, `/terms` and `/privacy`;
  - dynamic `robots.txt` and `sitemap.xml`; canonical, hreflang, Open Graph and JSON-LD.
  - The sign-up terms checkbox now links to the legal pages.
- **Fixes found while verifying:** see the phase file (the earliest-slot probe; a stale plan-price version accepted, a Phase 08 latent bug; review fixes).

## Verification evidence
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS, 373 / 63 / 138 (the full integration suite run alone) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web gates (`lint`, `typecheck`, `format:check`, `openapi:check`, `test`, `build`) | PASS, 257 web tests |
| Fresh `down -v` + `up --build` + `pnpm e2e` | Runs 1–3: 54/55. The only failure was the new robots test's own regex, then fixed. Runs 4–6: **55/55 ×3**. 0 HTTP 429 |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` (and `git` after the commit) | PASS |

## Database and migrations
- Created this session: `20260929065351_Reviews` (`reviews.reviews`, `reviews.rating_aggregates`, plus the same-shop review→booking FK by SQL).
- Applied locally only: Testcontainers databases and the compose volume.

## Decisions added
- D-090: multi-shop public scope.
- D-091: discovery pipeline, text search and the bounded probe.
- D-092: reviews read side and aggregates.
- D-093: API public cache with eviction on save.
- D-094: client address for server-rendered calls.
- D-095: location kept on the device; `TRIMME_SITE_URL` and `TRIMME_PARTNER_CONTACT_URL`.
- Design deviations: DV-S11, S12, S15, S21, A21, A23, A27 and A28 applied; DV-S14 partly applied. New: DV-C03 (landing), DV-C04 (professional statistics not shown) and DV-C05 (shop header QR/heart later).

## Known issues or blockers
- **Integration suite under host load.** With a build running and about 10 other containers, full runs hit 30 s Npgsql connection-open timeouts. A quiet run passes 138/138. Run the full suite alone and keep the log.
- **Booking links** from the shop and professional pages go to `/shops/{slug}/book?…`, which answers 404 until Phase 12 builds the wizard (`robots.txt` disallows it).
- **Production must-haves** (Phase 17):
  - Nginx overwrites `X-Forwarded-For` to both the web and the API, and the web network goes in `ReverseProxy:KnownNetworks` (D-094). Without it, public search shares one rate-limit bucket;
  - the web container is reachable only through Nginx;
  - `TRIMME_SITE_URL` is set;
  - the Nginx access logs will contain `lat`/`lng` from the browser's search calls, so decide their retention;
  - the tile and geocoder host (D-007).
- **Scale limits (D-091):** at most 200 search candidates, the probe covers 24 shops, and the cache is per instance.
- **Earlier carry-overs:**
  - packages across professionals (D-020);
  - "any professional" does not retry;
  - outbox processing (Phase 15);
  - grace days and limits not enforced (D-077);
  - Serilog logs handled 400/409 as 500 (Phase 17);
  - oversized uploads through the rewrite (Phase 17);
  - drag-and-drop ordering deferred.
- **Ports:** use `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`. The public E2E checks the sitemap URL in `robots.txt` against the address under test.

## Exact next action
1. If the user asks, push `main` and record the CI run in the Phase 11 file.
2. Start Phase 12 (`phases/phase-12-customer-booking.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Discovery"`
   - `cd tests/E2E && E2E_BASE_URL=http://localhost:3300 npx playwright test flows/public-discovery.spec.ts`
3. Phase 12 builds on this:
   - the booking wizard at the existing links (`/shops/{slug}/book?service|package|pro|date|time`);
   - the review command (`IBookingReviewSource` + `RatingBook` exist; the reviews table and aggregates are ready);
   - favorites; the shop header heart (DV-C05).
   Use Barber House or new shops in the booking E2E (the schedule flow pauses Al Asala).

## Files intentionally left modified
- None.
