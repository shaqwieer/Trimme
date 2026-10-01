# Performance

Budgets, how they are checked, and the Phase 17 measurements (local compose stack, demo data, 2026-10-01). Decision: D-123.

## 1. Budgets and their checks

| Budget | Limit | Check |
|---|---|---|
| Database commands per list page | No command per extra row (page of 1 vs page of 50, every paged endpoint in the OpenAPI document) | `QueryCountTests.ListEndpoints_RunTheSameNumberOfCommands_ForOneRowAndForFifty` |
| Database commands per hot read | Search 20, shop page 12, shop status 19, availability dates 16, shop overview 15, shop calendar 15, admin overview 19 | `QueryCountTests.HotReadPaths_StayWithinTheirCommandBudget` |
| Discovery probe | Same commands for one shop as for many | `QueryCountTests.BatchedProbes_ReadTheSameNumberOfCommands_ForOneShopOrMany` |
| Query shapes | No cartesian collection includes, no `Take`/`First` without an order | EF Core warnings throw in the Testing environment (every integration test) |
| First-load JavaScript per route (gzip) | Public pages 220 KB; sign-in, booking and discover 260 KB; map pages 330 KB; dashboards and account 330 KB | `pnpm bundle:check` after `pnpm build` |
| Availability API | p95 < 500 ms, < 1 % errors | `tests/load/smoke.js` (k6) |
| Booking API, uncontended | p95 < 1 s | `tests/load/smoke.js` (k6) |
| Lighthouse (mobile) | Accessibility, best practices and SEO 100 on indexable pages; performance recorded | Manual run (§4) |

## 2. Database

- **N+1 found and fixed.** Discovery ran the availability engine once per shop, with about six queries each: 23 commands for one shop, 35 for two. The shop status page did the same once per offer. `ISlotProbe.ProbeManyAsync` now loads every requested shop's hours, closures, working hours, breaks, time off and bookings in six queries. Search went from 23 to 17 commands, status from 22 to 16.
- **Indexes.** `EXPLAIN` was run with sequential scans disabled, so a plan that still scans has no usable index. It found three platform-wide reads without one, added by migration `20261001082134_PerformanceIndexes`:
  - `bookings (starts_at)`: the admin bookings list, newest first, and the overview's date ranges;
  - `users (user_type, created_at)`: the customers directory, newest first, and the new-customers KPI;
  - `reviews (status, created_at)`: the moderation list.

  Plans after the migration:

  | Query | Plan |
  |---|---|
  | Overview bookings in a range | Bitmap index scan on `ix_bookings_starts_at` |
  | Admin bookings, newest first | Backward index scan on `ix_bookings_starts_at` |
  | New customers in a range / directory | `ix_users_user_type_created_at` |
  | Moderation list | Backward index scan on `ix_reviews_status_created_at` |
  | Customer's bookings | `ix_bookings_customer_id_starts_at` |
  | Busy time (availability) | `ex_bookings_professional_overlap` (GiST) |
  | Dispatches list | Backward index scan on `ix_whatsapp_dispatches_created_at` |

  Shop-scoped reads use the `(shop_id, …)` indexes from earlier phases. Nearby search has the GiST index on `shops.location`. On four demo shops the planner prefers the status index, and the GiST index takes over as the table grows.
- **Two concurrent bookings of overlapping times for one barber.** The exclusion constraint makes each insert wait for the other's uncommitted row. PostgreSQL breaks the deadlock after `deadlock_timeout` (1 s), one request wins, and the other answers 409 (D-089).
  - Measured with k6: about 1.0 s for both requests in such a race, against 27 ms uncontended.
  - The fix, if this race becomes common, is a per-barber `pg_advisory_xact_lock` taken before the recheck in every booking write path (create, reschedule, walk-in, admin moves, "any professional"). Racing requests would then queue for a few milliseconds instead of a second. It is not in v1, because the race needs two customers on one barber's overlapping times within the same few milliseconds.

## 3. Web bundles

`pnpm bundle:check` reads the build's manifests and sums each route's first-load JavaScript, gzipped. Next 16 no longer prints it.

| Change | Effect |
|---|---|
| `lib/forms/validation.ts` imported the whole Arabic catalogue to read its validation keys. It now has its own key list, which a test keeps equal to the catalogue | −33 KB on every form page |
| `import { z } from 'zod'` brought all of Zod, including every locale pack and JSON-Schema support. `import * as z from 'zod'` lets the bundler drop what is unused | −60 KB on every form page |
| Total on the largest form pages | 412 KB → 320 KB |

After the changes:

| Route group | Largest route | Size |
|---|---|---|
| Public pages | Shop page | 213 KB |
| Sign-in, booking, discover | Staff and customer sign-in | 251 KB |
| Map pages | Admin shop page | 320 KB |
| Dashboards and account | Shop settings | 317 KB |

## 4. Lighthouse (mobile, simulated slow 4G, Lighthouse 12.8)

Median of runs against the local stack (the server is not a production one, TTFB about 460 ms):

| Page | Perf | A11y | Best practices | SEO | LCP | TBT | CLS |
|---|---|---|---|---|---|---|---|
| `/ar` landing (3 runs) | 88 | 100 | 100 | 100 | 3.8 s | 70 ms | 0 |
| `/en` landing | 88 | 100 | 100 | 100 | 3.8 s | 100 ms | 0 |
| `/ar/shops` | 91 | 100 | 100 | 100 | 3.6 s | 40 ms | 0 |
| `/ar/shops/barber-house` | 88 | 100 | 100 | 100 | 3.8 s | 40 ms | 0 |
| Barber page | 88 | 100 | 100 | 100 | 3.6 s | 80 ms | 0 |
| QR landing | 92 | 100 | 100 | 69 (`noindex` by design) | 3.5 s | 70 ms | 0 |

Fixed during the review:
- The landing hero is fetched with high priority (Next 16 replaced `priority` with `loading`/`fetchPriority`): performance 82 → 88, LCP 4.2 s → 3.8 s.
- Inter is no longer preloaded. Nine font files had been preloaded, all competing with the page's largest paint.
- Anonymous visitors no longer ask for favorites. The request could only answer 401, which put an error in the console of every shop page; best practices 96 → 100.

**Not met: LCP under 2.5 s on simulated slow 4G.** The remaining delay is render delay (about 2–3 s): web fonts (Tajawal in four weights for two scripts) and hydration. Next steps:
- fewer Tajawal weights, or `display: optional` for the heavier ones;
- a production server close to its users, since local TTFB is 460 ms;
- a CDN for `/_next/static`, the media files and `/brand`.

## 5. Caching

D-121: pages are rendered per request (CSP nonce); anonymous public API reads may be kept for 60 s; media is immutable; static files are immutable; brand images are kept for a day.

## 6. Running the checks

```bash
pnpm build && pnpm bundle:check                       # bundle budgets
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*QueryCountTests"
docker run --rm -i -e BASE_URL=http://host.docker.internal:3300 grafana/k6 run - < tests/load/smoke.js
CHROME_PATH=... npx lighthouse@12.8.2 http://localhost:3300/ar --chrome-flags="--headless=new"
```
