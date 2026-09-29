# Phase 11 — Public discovery, shop pages & SEO base

**Status:** [x] · **Score:** 100/100 (Session 6)

## Goal and user-visible outcome
Visitors can reach the landing page, set their location, find nearby shops in a list or on a map, filter and sort the results, and open fast, indexable shop and professional pages built from real data.

## Prerequisites
Phase 10 complete.

## In scope
- **Public search API:**
  - `GET /public/shops/search?lat&lng&radiusKm&q&categoryId&openNow&verified&bookableToday&minPrice&maxPrice&sort=distance|rating|earliest&page`.
  - Uses the PostGIS `ST_DWithin` + `ST_Distance` spatial index.
  - Bookability and pause visibility follow D-013/D-014.
  - "earliest" and "bookable today" use a bounded availability probe.
  - Rate limited (R-CUS-03/04).
- **Other public endpoints:**
  - `GET /public/categories`.
  - `GET /public/categories/popular?lat&lng` returns "from X" aggregates (DV-S11).
  - `GET /public/shops/{slug}`: gallery, description, rating aggregate, address, coordinates, open status, services, packages, professionals, and a reviews page.
  - `GET /public/shops/{slug}/professionals/{proSlug}`.
- **Pages** (Server Components first):
  - Landing `/` (c-landing).
  - `/shops`: indexable listing by city (DV-A27).
  - `/discover` (c-home, noindex).
  - `/search?view=list|map` with a filter drawer (c-map) that uses the map adapter (price pins, selected-shop sheet).
  - `/onboarding/location` plus the location sheet, with manual district/city selection (DV-A21).
  - `/shops/[slug]` (c-shop) with tabs as a small client island; the gallery, mini-map, packages section and description are added (DV-A23).
  - `/shops/[slug]/professionals/[proSlug]`.
  - Legal pages: terms and privacy, with placeholder content marked for legal review (DV-A28).
- **Reviews display:** read-only list with first name + initial (D-017). Review creation comes in Phase 12.
- **SEO base:**
  - Localized metadata, canonical URLs, hreflang, Open Graph (logo/cover).
  - JSON-LD: Organization, LocalBusiness, BreadcrumbList, and AggregateRating only when review count > 0.
  - `robots.txt`, and `sitemap.xml` containing the landing, the listing and every active shop and professional page.
  - noindex on personalised/private routes (R-WEB-10/11, R-NEG-10).
- **Caching:** public read endpoints and pages use revalidation tags that are invalidated on shop, service or professional changes. Authorization-sensitive data is never cached.
- **Tests:**
  - Spatial nearby query ordering.
  - Filter combinations.
  - JSON-LD presence and absence.
  - E2E: discover → shop page in RTL and LTR at 3 viewports; axe on public pages.

## Explicitly out of scope
The booking wizard (Phase 12) and QR landing (Phase 16).

## Checklist (100 points)
- [x] 11.1 (5) Re-validate; refine checklist. Phase 10: Bookings integration 14/14 and the concurrency suite 4/4 in each of 3 runs (2026-09-29). Design review settled: multi-shop public scope (D-090), discovery pipeline and bounded probe (D-091), reviews read side and aggregates (D-092), API output cache with save-time eviction instead of Next tag revalidation (D-093), forwarded client IP for server-rendered calls (D-094).
- [x] 11.2 (14) Spatial search API (`GET /public/shops/search`) with text, category, open-now, verified, bookable-today and price filters, and distance, rating or earliest sort, paged (D-091). Integration tests: `NearbySearch_IsNearestFirst…` and `Search_Filters…` (including an absent professional's shortest service); unit tests for `SearchText` and `OpenStatus`.
- [x] 11.3 (8) Public detail APIs:
  - shop detail extended with rating, prices, hours and policy, plus live `/status`;
  - professional detail and next slots; ratings on the professional list;
  - popular categories, stats, areas, sitemap and top professionals;
  - the Reviews read side with aggregates and demo reviews (D-092).

  Covered by `PublicPages_…_CarryNoContactData`.
- [x] 11.4 (10) Landing (hero search, real non-zero figures, value cards, top rated, partner band) and the indexable `/shops` listing (cities, paging, breadcrumb); marketing shell with nav and footer. E2E `landing_renders_ar_en`.
- [x] 11.5 (14) Discover and search:
  - `/discover`: location sheet, categories, near you, popular "from X" tiles and top barbers;
  - `/search`: URL-driven chips, result cards and pages, a MapLibre map with price pins and the selected-shop card, and a filter drawer with a live count;
  - `/onboarding/location`: allow on press or pick a district, kept in a cookie (D-095).

  Covered by web tests and E2E (location, filters, map).
- [x] 11.6 (16) Shop page:
  - cover, logo, verified mark, rating, browser-side distance, live open status with the paused notice, and address with directions;
  - gallery; tabs with every panel in the HTML (services + packages, barbers with next times, reviews with summary and paging, about with hours, policy, amenities, phone and mini-map);
  - sticky book bar.

  Professional page: profile, next free times, services, latest reviews and a book CTA. E2E `shop_page_sections` and professional profile.
- [x] 11.7 (12) SEO:
  - localized metadata, canonical, hreflang (x-default ar) and Open Graph from the runtime `TRIMME_SITE_URL`;
  - JSON-LD: Organization + WebSite, HairSalon with AggregateRating only from stored reviews, BreadcrumbList, Person (escaped against `</script>`);
  - dynamic `robots.txt` and `sitemap.xml`; noindex on personalised pages.

  Unit tests and E2E `private_routes_emit_noindex, robots.txt and sitemap_lists_shops`.
- [x] 11.8 (6) Caching (D-093): the API output-caches anonymous public reads, evicted by a save interceptor on `IPublicContent`. The web uses a cookie-less, `no-store` public client that forwards the client address (D-094); live parts sit outside cached payloads. Integration test `PublicResponses_AreCached…_AndEvicted…`.
- [x] 11.9 (5) `/terms` and `/privacy`: placeholder text that states the real rules and is marked for legal review. The sign-up terms checkbox links to them in a new tab (DV-A28).
- [x] 11.10 (10) E2E `flows/public-discovery.spec.ts` (8 tests), with axe on every public page (nothing serious), no horizontal overflow at 390/768/1440, 3-viewport screenshots in ar and en, and a visual check against `design/reference`. Control files and commit.

## Files/modules expected to change
- **Building blocks:** `SearchText`, `IPublicContent`, the multi-shop public scope (D-090), the discovery and booking-review contracts, `PublicContentInterceptor`, `PublicCache`, demo visits.
- **Shops:** discovery pipeline and queries, public shop detail and status, the reserved slug.
- **Services:** `ShopOfferReader`.
- **Availability:** `AvailabilityEngine.OpenStatus`, `ShopOpeningReader`, `SlotProbe`.
- **Professionals:** public detail and next slots; ratings on the public list.
- **Reviews:** domain, persistence, rating reader, public reviews and demo seed.
- **Bookings:** `BookingReviewSource`; seeded visit times.
- **Subscriptions:** the price-version check fix.
- **API host:** output cache and trusted proxy networks.
- **Web:**
  - `apps/web/src/app/[locale]/{page,shops,shops/[slug],shops/[slug]/professionals/[proSlug],discover,search,onboarding/location,terms,privacy}`, plus `app/robots.ts` and `app/sitemap.ts`;
  - `components/{discovery,shops/public,legal,seo}` and `lib/{api/public,discovery,seo}`;
  - the public shell and the message catalogues.

## Data model and migration impact
- Migration `Reviews` (`20260929…_Reviews`; the plan named it `0009_DiscoveryIndexes`). It adds `reviews.reviews` and `reviews.rating_aggregates`, plus the same-shop review→booking FK by SQL.
- **Deviation:** there are no normalized search columns. Text search runs in memory over the spatial candidate set (D-091). The existing GiST index on `shops.location` serves the spatial query.

## API contracts and UI routes
- **API** (all anonymous; the reviewed allow-list covers them):
  - `GET /public/shops/search`, `/public/categories/popular`, `/public/professionals/top`, `/public/stats`, `/public/areas` and `/public/sitemap`;
  - `GET /public/shops/{slug}`, now extended with rating, prices, hours and policy, plus `/status` and `/reviews`;
  - `GET /public/shops/{slug}/professionals/{professionalSlug}` and `/next-slots`.
- **UI:** `/`, `/shops`, `/discover`, `/search`, `/onboarding/location`, `/shops/[slug]`, `/shops/[slug]/professionals/[proSlug]`, `/terms`, `/privacy`, `robots.txt` and `sitemap.xml`.

## Security, tenancy, privacy, RTL, a11y, responsive
- Public DTOs carry no customer or professional phone; only the shop's own business number appears. This is asserted on every public endpoint and in the rendered HTML.
- **Location** stays on the device: a first-party cookie with rounded coordinates, and geolocation is asked for only on press (D-095).
- **Cache safety.** The public cache serves anonymous requests only (D-093). Server-rendered calls forward the client address (D-094).
- **Map.** It has a list alternative, and its pins are buttons.
- **Right-to-left.** Direction is mirrored through logical properties and the directional icons. Reviews use `dir="auto"`.
- **Accessibility and layout.** axe reports nothing serious or critical on any public page. No page overflows horizontally at 390, 768 or 1440 px.

## Tests and verification commands
Smallest decisive re-validation (for the next session):
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Discovery"
dotnet test --project tests/Trimme.UnitTests -c Release --filter-namespace "*Discovery"
pnpm -C apps/web vitest run src/lib/discovery src/lib/seo src/components/discovery
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 npx playwright test flows/public-discovery.spec.ts
```

## Acceptance criteria
- Nearby search returns distance-sorted real shops. ✔
- Shop pages are indexable and have valid JSON-LD. ✔
- axe reports nothing serious. ✔

## Rollback / recovery
Revert the commit, then drop and recreate the volume. The `Reviews` migration is additive, and its SQL FK is dropped in `Down`.

## Completion evidence
Session 6, 2026-09-29.

**Re-validation of Phase 10 (before any change).** Bookings integration 14/14; the concurrency suite 4/4 in each of 3 runs.

**What was built.** See the checklist and D-090 … D-095.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration | PASS — **373 / 63 / 138** (Phase 10 end: 349 / 63 / 133). New: 24 unit (`Discovery/DiscoveryDomainTests`), 5 integration (`Discovery/DiscoveryTests`); the full integration suite was run alone |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes (migration `20260929065351_Reviews`) |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — **257** web tests (28 new). The build shows every public data page rendered per request, and `robots.txt` and `sitemap.xml` dynamic |
| Fresh `down -v` + `up --build` (10 seeders), then `pnpm e2e` | **Runs 1–3: 54/55 each.** The one failure was the new robots test's own negative regex, which also matched the `Disallow: /ar/shops/*/book` line added on review. The assertion was changed to exact lines. **Runs 4–6 on the same fresh stack: 55/55, 55/55, 55/55.** 0 HTTP 429 in the API log |
| No-transfer and R-NEG-08 grep gates; gitleaks `dir` | PASS — hits only in excluded tests, seeders and the geocoder throttle; no leaks |

**Found and fixed while verifying**
- **Earliest slot hidden by an absent professional.** The first probe used each shop's single shortest offer. At Barber House that belongs to Majed, who is on time off, so the shop showed no time at all while Omar was free. The probe now checks each professional's shortest bookable offer (D-091). Regression assertion: `Search_Filters…`, where the shortest service's only professional is on time off.
- **Stale plan-price version accepted (Phase 08 latent bug).** Adding a price changes only `UpdatedAt`. When the new value equals the stored microseconds, EF sends no UPDATE and skips the version check. This happened in about 1 in 10 test runs with a frozen clock. The handler now always writes the plan row. `SubscriptionTests` starts its clock on a whole microsecond, so the path runs every time; the probe fails without the fix.
- **Integration timeouts under host load.** Two full runs had 8–13 Npgsql connection-open timeouts, each at 30 s. Each run overlapped a build, with about 10 other containers running on the host. Every failed class passed alone, and the next quiet run passed 138/138 (test PostgreSQL peaked at about 400% CPU, 9 GB and 164 connections). Watch item: run the suite on a quiet machine.
- **Review fixes before the gates:**
  - `robots.txt` was generated at build time with the default host, so it is now dynamic;
  - the results map created a WebGL context on every render, so the check now runs once;
  - a failed `/status` could break the shop page, so it now degrades to `null`;
  - `aria-pressed` on a link became `aria-current`;
  - the map fit leaves room for the selected-shop card;
  - review text gets `dir="auto"` on English pages;
  - the partner link is a plain anchor;
  - seeded visits record arrival and completion at the visit times.
- **Message parity test.** It read English plural-branch text as argument names. It now counts real arguments only; the probe (removing `{km}`) fails.

**Commit:** `8832a12`. gitleaks `git` on the committed code: 39 commits, no leaks. Pushed (with the docs commit `4f441de`) at the start of Phase 12, as the user asked. **CI:** GitHub Actions run 36543760813 on `4f441de` — completed, conclusion **success**.

## Remaining risks → next phase
- **Production configuration:**
  - map tiles and geocoder host (D-007);
  - `TRIMME_SITE_URL`;
  - Nginx must overwrite `X-Forwarded-For` to both the web and the API, and the web network goes in `ReverseProxy:KnownNetworks` (D-094). Otherwise all public search shares one rate-limit bucket;
  - the web container must be reachable only through Nginx;
  - the Nginx access logs will record the browser's `lat`/`lng` search parameters (Phase 17).
- **Booking links** (`/shops/{slug}/book?…`) answer 404 until Phase 12 builds the wizard; `robots.txt` disallows them. (Resolved in Phase 12.)
- **Scale limits (D-091):**
  - text search runs in memory over at most 200 nearest candidates;
  - the earliest-slot probe covers the first 24 shops;
  - the cache is per API instance.
- **Next: Phase 12 — Customer booking & account.** It adds the review command (`RatingBook` is ready), favorites and the wizard at the booking links above.
