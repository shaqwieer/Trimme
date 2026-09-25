# Phase 11 — Public discovery, shop pages & SEO base

**Status:** [ ] · **Score:** 0/100

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
- [ ] 11.1 (5) Re-validate; refine checklist.
- [ ] 11.2 (14) Spatial search API + filters + sorting + tests.
- [ ] 11.3 (8) Shop/professional public detail APIs + popular categories aggregates.
- [ ] 11.4 (10) Landing + `/shops` listing (RSC).
- [ ] 11.5 (14) Discover + search list/map + filter drawer + location onboarding/sheet.
- [ ] 11.6 (16) Shop page (all sections) + professional profile.
- [ ] 11.7 (12) SEO: metadata, canonical, hreflang, OG, JSON-LD, robots, sitemap + tests.
- [ ] 11.8 (6) Caching + invalidation (public only).
- [ ] 11.9 (5) Legal placeholder pages.
- [ ] 11.10 (10) E2E + axe + 3-viewport screenshots vs reference, control files, commit.

## Files/modules expected to change
- Shops module query side
- Reviews module (read side)
- `apps/web/src/app/[locale]/(public)/**`

## Data model and migration impact
- Possibly a `RatingAggregate` table/columns on shop and professional (created here, updated in Phase 12).
- Search normalisation columns (Arabic-normalised name) + index.
- Migration `0009_DiscoveryIndexes`.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- Public DTOs exclude every phone except the shop's own public contact.
- The user's location is sent only on request and is never stored server-side without consent.
- The map has a list alternative.

## Tests and verification commands
```
dotnet test --filter Category=Discovery
pnpm exec playwright test public
pnpm -C apps/web build   # check RSC/client bundle report
```

## Acceptance criteria
- Nearby search returns distance-sorted real shops.
- Shop pages are indexable and valid JSON-LD.
- Axe reports nothing serious.

## Rollback / recovery
Revert the commit.

## Completion evidence
_(fill)_

## Remaining risks → next phase
- Production map tiles (D-007).
- Next: Phase 12 — Customer booking & account.
