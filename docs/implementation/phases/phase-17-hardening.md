# Phase 17 — Localization completion, SEO, accessibility, security hardening, observability, performance

**Status:** [x] · **Score:** 98/100

## Goal and user-visible outcome
The whole platform reaches production quality:
- Every screen is complete and reviewed in English as well as Arabic.
- SEO is finished.
- Every route passes WCAG AA automated checks and a manual keyboard pass.
- Security is hardened and audited.
- The platform is observable, and performance budgets are met.

## Prerequisites
Phase 16 complete.

## In scope
- **Localization:**
  - Audit every route in `/en`: completeness, mirrored icons, bidi, and date/number formats.
  - Validation messages translated.
  - No literal strings (lint gate at zero).
- **SEO:**
  - Final sitemap: index plus per-locale files.
  - Canonical URLs and hreflang across every public route.
  - OG images.
  - Structured-data validation.
  - noindex audit of private routes (R-WEB-10/11, R-NEG-10).
- **Accessibility:**
  - Axe on every route; target 0 serious/critical findings.
  - A manual keyboard and screen-reader smoke pass (NVDA) on the key flows.
  - Contrast re-check.
  - Reduced-motion support.
  - Touch targets (R-WEB-09).
- **Security:**
  - CSP with nonces for Next.js, and final security headers.
  - Rate-limit tuning and tests for every named policy.
  - Upload validation: magic bytes, size, and re-encoding.
  - Dependency audit (`pnpm audit`, `dotnet list package --vulnerable`), secret scan, and a basic OWASP ZAP baseline scan against compose.
  - Threat-model notes.
  - Encryption key rotation documentation (D-026).
  - A final phone-leak sweep: grep the logs and all shop payloads.
- **Observability:**
  - OpenTelemetry traces and metrics (ASP.NET, EF, Hangfire) with an OTLP exporter configurable per environment.
  - Health checks for Hangfire and the outbox lag.
  - Structured-log field conventions documented.
- **Performance:**
  - Check the EF query plans for the hot paths: search, availability, shop dashboard, admin lists.
  - N+1 detection via interceptors in tests.
  - Index review.
  - Next.js bundle budgets and Lighthouse on the public pages (mobile targets are recorded).
  - Public cache headers.
  - A `k6` smoke test for availability and booking.
- **Visual review** of every implemented screen against `design/reference` at 390/768/1440. Differences are either fixed or recorded in `design-deviations.md`.
- The production map provider is configured per D-007 (environment variables only).

## Explicitly out of scope
New features.

## Checklist (100 points)
- [x] 17.1 (5) Re-validate; refine checklist.
  - Phase 16 still holds (Session 13, 2026-10-01): `QrTests` 3/3 (integration, Testcontainers) and `flows/qr.spec.ts` 3/3 against the running compose stack.
  - Tools: k6 and OWASP ZAP run from their Docker images; Lighthouse runs through `npx` with the local Chrome; gitleaks v8.30.1 image is present.
  - Refinements:
    - Nginx: D-116 removed `infra/nginx`, so the forwarding requirement goes into the security notes, not a config file.
    - NVDA cannot be run by the agent: a scripted keyboard pass and accessibility-tree checks replace it here, with an NVDA checklist for the user (sub-item `[-]` unless the user runs it).
    - One route inventory (every page, each role, both locales, seeded ids) drives axe, the noindex audit, CSP violations and the English audit.
    - A nonce CSP makes every page dynamic, so "public cache headers" covers the anonymous API reads, media, QR files and static assets.
- [x] 17.2 (14) English/RTL/bidi audit + fixes.
  - [x] Route inventory crawl in `/en` and `/ar` (`tests/E2E/audit/`, Playwright project `a11y`): 81 routes × 2 locales as anonymous, customer, owner and admin. Run 1 found: raw keys (`permissions.*.Jobs`, `qr.created`/`deactivated` audit labels), English audit summaries without `lang`, Arabic data fallbacks without `lang`, MapLibre's English "Map" in `/ar`, a hydration error on `/admin/qr` (a `<div>` inside the mobile card's `<p>`), and a spec hang (`getAttribute` waits for absent elements). All fixed; run 3: **8/8 passed** (3.2 min).
  - [x] Validation messages: Zod issues carry catalogue keys and unknown ones fall back to the translated `generic`; API problems map by code (`apiErrors`), never by their English `detail`. Code coverage reviewed: `invitation.role_invalid` gained its own message; the rest without one are technical (session refresh, 405/415) or handled in place (`otp.incorrect`).
  - [x] Mirrored directional icons, bidi, formats. Directional-icon audit found three inverted chevrons (shop calendar previous/next swapped, account rows and the "new booking" card pointing back); fixed and checked on screenshots in both locales. Language of parts (WCAG 3.1.2): language names, Arabic-only descriptions in English, template bodies and audit summaries carry `lang`. No Arabic-Indic digits in English pages (route audit).
  - [x] `react/jsx-no-literals` gate at zero (`pnpm lint`, 0 warnings).
- [x] 17.3 (8) SEO completion + validation.
  - [x] Sitemap index (`/sitemap.xml`) plus `/sitemaps/ar.xml` and `/sitemaps/en.xml` with ar/en/x-default alternates; robots points at the index. W `seo.test.ts`; E `public-discovery` follows the index.
  - [x] Canonical + hreflang on every public route; OG image `brand/trimme-og.png` (1200×630, logo on navy, no text) as the layout default, Twitter `summary_large_image`; legal pages gained Open Graph. Route audit checks canonical, hreflang and `og:image` on all 7 indexable routes in both locales.
  - [x] JSON-LD validated by the route audit on every indexable page (parses, schema.org context and type, `AggregateRating` only with reviews and a value in 1–5): Organization/WebSite on the landing, HairSalon with rating, hours and breadcrumbs on the shop page, Person on the barber page; W `jsonld_aggregateRating_absent_without_reviews`.
  - [x] noindex audit of every private route (R-WEB-10/11, R-NEG-10): every non-indexable route in the audit carries `noindex` (74 routes × 2 locales).
- [x] 17.4 (16 → **14**) Accessibility audit + fixes (automated + manual). 2 points held back: the NVDA pass below has not been run.
  - [x] Playwright `a11y` project: axe (WCAG 2.0/2.1/2.2 A and AA, incl. `target-size`) on every route, each role, both locales; **0 serious/critical** (run 3).
  - [x] Scripted keyboard pass of the key flows (`tests/E2E/audit/keyboard.spec.ts`, 5/5): skip link → `#main`; every stop on `/ar`, `/en`, the shop page and sign-in shows a focus indicator and Tab cycles without a trap; staff sign-in by keyboard alone; the admin "New code" dialog keeps focus and closes on Escape. Found and fixed: dialogs opened by a plain button (not the Radix trigger) dropped focus to the body on close; `useReturnFocus` in the shared overlays now returns it (W test added).
  - [x] Contrast re-check (axe `color-contrast` on every route in both locales: 0 serious), reduced motion (transitions 0 with `reduce`, running otherwise), touch targets (R-WEB-09): on a touch screen, 44 px at least on 5 key pages. Found and fixed: `xs` buttons (36 px), `sm` icon buttons, breadcrumb links (20 px), header logo links (38 px) and the staff sign-in link now reach 44 px under `pointer: coarse`.
  - [-] NVDA screen-reader pass: needs a person; the 13-step checklist is in `docs/accessibility.md` §3 (not run by the agent).
- [x] 17.5 (18) Security hardening + scans + phone-leak sweep.
  - [x] Nonce CSP for Next.js and final headers; E2E fails on any CSP violation (D-117). The guard found a real defect at once: the Hangfire dashboard's inner pages got the API's `default-src 'none'` (fixed; the test now checks the exact policy on `/recurring`). Files and 404s get a fixed script-less policy; COOP/CORP `same-origin`; HSTS when the site is HTTPS.
  - [x] A test for every named rate-limit policy, and the endpoint matrix asserts which endpoints carry which policy. I `RateLimitPolicyTests` 9/9 (limit + 1 → 429, `Retry-After`, `rate_limited`, a second user unaffected); I `RateLimitPolicies_AreAttachedToExactlyTheReviewedEndpoints` (29 endpoints, all 9 policies in use).
  - [x] Uploads: magic bytes, size, pixel cap, re-encoding (D-119, SkiaSharp). U `ImageReencoderTests` 9; I shop media test (polyglot, hollow JPEG, EXIF orientation). Compose (Linux): a 4000×3000 JPEG with orientation 6, GPS EXIF and an appended `<script>` was stored as 1920×2560, upright, without EXIF, GPS or payload.
  - [x] `pnpm audit` (no known vulnerabilities), `dotnet list package --vulnerable --include-transitive` (none, all 21 projects), gitleaks `dir` (no leaks), ZAP baseline against compose: 0 FAIL. The first run found credentials in the URL on a pre-hydration form submit, missing CSP on files and 404s, and missing COOP/CORP; all fixed (`forms.test.ts`). The remaining warnings are accepted with reasons in `docs/security.md` §5. Both audits now run in CI.
  - [x] Threat-model notes, proxy forwarding requirement, key rotation (D-026): `docs/security.md`. Found and fixed: the Data Protection key ring was stored unwrapped next to the numbers it encrypts; it is now wrapped with a mounted certificate, required outside Development/Testing (D-120, I `KeyEncryptionTests`, production startup test).
  - [x] Phone-leak sweep on the fresh stack after two full E2E runs: 0 in 10,171 API log lines and 161 web log lines, 89 Hangfire jobs, 239 job states, 36 outbox payloads, 90 audit entries, 50,601 exported spans. SignalR payloads and shop pages are covered by the existing contract tests (R-NEG-02, R-SD-10) and the shop-facing DTO tests.
- [x] 17.6 (12) Observability (OTel, health, dashboards documented).
  - [x] OTel traces and metrics (ASP.NET Core, HTTP, Npgsql/EF, Hangfire jobs, outbox), OTLP only when configured (D-118). I `Requests_AreTracedWithTheirDatabaseCommands_AndNoSpanCarriesAPhoneNumber`. Compose with a local collector: traces, `Trimme` job/outbox spans and metrics received; parentless polling spans dropped by the sampler (598 → 0); 3,838 spans from the auth, QR and WhatsApp flows, no phone number in any attribute.
  - [x] Health checks for Hangfire and outbox lag (degraded, never failing readiness). I `Readiness_ReportsJobsAndOutboxLag_AsDegradedWithoutFailing`; compose `/health/ready` reports `database`, `jobs`, `outbox` Healthy.
  - [x] Structured-log field conventions documented: `docs/observability.md`.
- [x] 17.7 (12) Performance review + fixes + budgets.
  - [x] Query plans for search, availability, shop dashboard, admin lists; index review. `EXPLAIN` with sequential scans off: three platform-wide admin reads had no usable index; migration `20261001082134_PerformanceIndexes` (index-only) adds `bookings(starts_at)`, `users(user_type, created_at)`, `reviews(status, created_at)`, and the plans now use them (`docs/performance.md` §2).
  - [x] N+1 detection by a command-counting interceptor (`QueryCountTests`, the paged endpoints read from OpenAPI). Found: discovery search ran ~12 commands per probed shop (23 for one shop, 35 for two) and the shop status one probe per offer; fixed with a batched `ProbeManyAsync`, 6 schedule queries for any number of shops (search 23 → 17, status 22 → 16). Per-path budgets guard the hot reads. The two EF warnings are fixed and now throw in Testing (199 integration tests ran with the throw on).
  - [x] Bundle budgets (`pnpm bundle:check`: 79 routes within budget) and Lighthouse mobile recorded. Form pages 412 → 320 KB gzip (the catalogue import and Zod's locale packs). Lighthouse: a11y, best practices and SEO 100 on indexable pages; performance 88–92; LCP 3.5–3.8 s (2.5 s target not met; documented with next steps in `docs/performance.md` §4).
  - [x] Public cache headers (D-121: anonymous public reads 60 s, also on output-cache hits; brand images 1 day; media immutable; pages per request) and k6 smoke (`tests/load/smoke.js`): availability p95 34 ms, 0 errors; uncontended booking p95 29 ms; 320/320 checks. A contended race on one barber costs ~1 s through the deadlock path (D-089, documented in D-123).
- [x] 17.8 (10) Visual review vs reference screenshots (per design screen, 390 and 1440).
  - All 25 product screens were captured from the stack: customer screens at 390 and 1440, dashboards at 1440, signed in as the right role, full height.
  - An independent reviewer compared them with `design/reference/1440`. The first capture batch was flawed (shared sessions, single viewport) and was redone. Every reported chevron and clipping finding was re-checked against the DOM and zoomed screenshots; the landing CTA chevron and the calendar clipping were false.
  - Fixed:
    - the landing search bar collapsed at 390 px;
    - the booking back chevrons;
    - the empty step bar on step 1;
    - the map credits hidden under the card;
    - Arabic and English plural forms for review counts;
    - two admin search-button styles;
    - suspended-shop badge colour.
  - The rest is recorded in `docs/design-deviations.md` (DV-C11, DV-T14; DV-S09 corrected to applied).
- [x] 17.9 (5) Gates, control files, commit.

## Files/modules changed
- **Web:**
  - `proxy.ts`, `lib/security/csp.ts` (nonce CSP) and `next.config.ts` (file CSP, COOP/CORP, brand caching);
  - the locale layout renders per request and carries the default Open Graph image;
  - `app/sitemap.xml` and `app/sitemaps/[file]`, with `lib/seo/sitemap*.ts`; `public/brand/trimme-og.png`;
  - `lib/i18n/localized.ts` (`localizedText`, `langIfOther`), `lib/auth/session.server.ts`, `lib/forms/validation.ts` (key list), Zod namespace imports;
  - `components/ui/overlays.tsx` (`useReturnFocus`), `Button` (touch sizes), `data.tsx` (card primary block, breadcrumb targets), `booking.tsx` (step fill);
  - fixes in the shop calendar, account rows, booking cards, booking wizard, landing search bar, maps (MapLibre locale, credits on top), favorites (no anonymous probe), audit page, WhatsApp template excerpts, admin search buttons, shop status badge and the `method="post"` forms;
  - messages: audit, permission, map and API error labels, and plural review counts;
  - `scripts/bundle-budget.mjs`.
- **API:**
  - `Telemetry.cs`, `JobTelemetryFilter.cs`, `OperationsHealthChecks.cs`, outbox metrics and span;
  - `KeyEncryption.cs` and the Data Protection setup;
  - `ImageReencoder.cs` (SkiaSharp) and `MediaStore`;
  - `PublicCache` and `SecurityHeadersMiddleware` (cache header, dashboard CSP path);
  - `ISlotProbe.ProbeManyAsync`, `ScheduleLoader` batch, multi-shop `IBookedTimeReader`, `Discovery`, `PublicShops`;
  - `AsSplitQuery` and ordered sweeps; EF warnings throw in Testing;
  - the hubs ignore a client abort during the handshake;
  - indexes on bookings, users and reviews.
- **Tests:**
  - integration `ObservabilityTests`, `RateLimitPolicyTests`, the rate-limit matrix, `KeyEncryptionTests`, `QueryCountTests` and the production-startup case;
  - unit `ImageReencoderTests`;
  - web `csp`, `forms`, `permissions`, `seo`, map locale, dialog focus return, step fill and favorites;
  - E2E `support/fixtures.ts` (CSP guard), `audit/routes.spec.ts`, `audit/keyboard.spec.ts`, the sitemap index and the axe title wait;
  - `tests/load/smoke.js`.
- **Infra:** compose `OTEL_EXPORTER_OTLP_ENDPOINT`; CI bundle budgets, `pnpm audit`, NuGet audit, all Playwright projects, 45-minute stack job.
- **Docs:**
  - `security.md`, `observability.md`, `performance.md`, `accessibility.md`;
  - README, architecture, design deviations (DV-C11, DV-T14, DV-S09);
  - D-117 to D-123; traceability.

## Data model and migration impact
Migration `20261001082134_PerformanceIndexes`, index-only: `ix_bookings_starts_at`, `ix_users_user_type_created_at`, `ix_reviews_status_created_at`. Its `Down` drops them. No data changes.

## API contracts and UI routes
- No new API operations, and the OpenAPI document is unchanged (`pnpm openapi:check`).
- Web routes added: `/sitemaps/{ar|en}.xml`; `/sitemap.xml` is now an index.
- Response headers changed: anonymous public reads carry `Cache-Control: public, max-age=60, stale-while-revalidate=60`.

## Security, tenancy, privacy, RTL, a11y, responsive
This whole phase is about these concerns; see the checklist, `docs/security.md` and `docs/accessibility.md`.

## Tests and verification commands
```
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*QueryCountTests"
dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-class "*RateLimitPolicyTests"
pnpm build && pnpm bundle:check
cd tests/E2E && E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 npx playwright test --project=a11y
docker run --rm -t ghcr.io/zaproxy/zaproxy:stable zap-baseline.py -t http://host.docker.internal:3300/ar -m 3 -I
docker run --rm -i -e BASE_URL=http://host.docker.internal:3300 grafana/k6 run - < tests/load/smoke.js
```
Lighthouse runs by hand (`docs/performance.md` §6); D-123 explains why it is not gated.

## Acceptance criteria
- No known critical or high security issues: ZAP 0 FAIL; both dependency audits clean; gitleaks clean.
- axe reports 0 serious findings: route audit, 81 routes × 2 locales × the right role.
- The performance budgets are met or explicitly documented: query, bundle and k6 budgets met; Lighthouse LCP documented as not met on simulated slow 4G.

## Rollback / recovery
- Revert the commit. The migration's `Down` drops the three indexes.
- Operational switches:
  - unset `OTEL_EXPORTER_OTLP_ENDPOINT` to stop exporting;
  - production must keep `DataProtection:CertificatePath`, because keys written while it was set cannot be read without it (`docs/security.md` §4).

## Completion evidence
Session 13 (2026-10-01).

| Check | Result |
|---|---|
| Re-validation of Phase 16 | `QrTests` 3/3, `qr.spec.ts` 3/3 |
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 429 / 65 / 199 (420 → 429, 175 → 199). EF query-shape warnings now throw in Testing |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| `pnpm audit`; `dotnet list package --vulnerable --include-transitive` | No known vulnerabilities; no vulnerable packages |
| OpenAPI | `pnpm openapi:check` PASS (unchanged) |
| Web `format:check`, `lint`, `typecheck`, `test`, `build`, `bundle:check` | PASS: 385 tests (366 → 385); 79 routes within budget, largest 320 KB gzip |
| E2E `tsc`, `prettier --check` | PASS |
| Fresh `down -v` + `up --build`, full suite `CI=1 --workers=2 --retries=0` | Runs 1 and 2: **85/85** each (72 → 85: route audit 8, keyboard pass 5). API log: 0 × 5xx, 0 × 429. ERR lines: E6's expected exclusion-constraint loss, and one SignalR handshake cancelled by a navigating client, now handled in both hubs. Run 3 on the hub fix: 84/85; the miss was axe catching a soft refresh between two `<title>` elements (fixed: the axe helpers wait for the title). Run 4: 83/85; the route audit found `/admin/whatsapp/dispatches` 20 px wider than a 390 px phone, because the dispatch log on the long-lived volume had reached six pages and the shared pagination showed every page cell (fixed: below `md` only previous, current and next; D-122). Run 5 on the fix: **85/85**; API log 0 × 5xx |
| Linux Chromium (`playwright:v1.63.0-noble`): route audit, keyboard pass, `public-discovery`, `customer-booking` | **26/26**. Route audit 8, keyboard pass 5 and `public-discovery` passed on the first pass (23/26). The 3 `customer-booking` misses were the OTP limit working: the spec signs in with three fixed seeded numbers, and runs 4 and 5 had used their 5 codes per hour (6 × 429 on `/auth/otp/request`, the only 429s). After the window, `customer-booking` passed 5/5 |
| Route audit (`a11y`) | 81 routes × 2 locales; 0 serious/critical axe; no raw key, untranslated string, Arabic-Indic digit in English, wrong `noindex`, missing canonical/hreflang/og:image, invalid JSON-LD, page error, 390 px overflow or CSP violation |
| OWASP ZAP baseline (final, compose) | 0 FAIL, 57 PASS, 10 WARN types, all accepted in `docs/security.md` §5 |
| Phone-leak sweep (fresh stack after two full runs) | 0 in API/web logs, Hangfire jobs and states, outbox, audit and 50,601 spans |
| k6 smoke | availability p95 34 ms, 0 errors; uncontended booking p95 29 ms; 320/320 checks |
| Lighthouse mobile (`docs/performance.md` §4) | a11y/BP/SEO 100 on indexable pages; perf 88–92; LCP 3.5–3.8 s |
| No-transfer and R-NEG-08 grep gates | PASS: R-NEG-08 hits only in tests, seeders and the excluded geocoder |
| gitleaks `dir` | PASS, no leaks; `git` (v8.30.1, 67 commits, after the phase commit `41412ee`): no leaks |

**Found and fixed during the phase (beyond the planned work):**
- the Hangfire dashboard's inner pages blocked by the API CSP;
- the Data Protection key ring unencrypted next to the numbers it protects;
- discovery's per-shop probe queries (an N+1);
- three platform-wide reads without an index;
- the whole Arabic catalogue and every Zod locale pack shipped with the forms;
- credentials in the URL on a pre-hydration submit;
- a hydration error on `/admin/qr`;
- missing permission and audit labels, shown as raw keys;
- inverted chevrons (shop calendar, account rows, new-booking card, booking back buttons);
- dialogs that dropped focus on close;
- touch targets below 44 px;
- the landing search bar collapsed on a 390 px phone;
- the empty step bar on step 1;
- hidden map credits;
- review-count plurals;
- an anonymous favorites probe that logged a console error on every shop page;
- pagination wider than a 390 px phone once a list reaches six pages.

## Remaining risks → next phase
Phase 17 is the last phase: Phase 18 (regression and handover) was removed at the user's request (D-116).
- **NVDA screen-reader pass:** a manual checklist (`docs/accessibility.md` §3), not run; 2 points held back.
- **LCP on simulated slow 4G is 3.5–3.8 s**, against a 2.5 s target (`docs/performance.md` §4).
- **A contended booking race costs about 1 s;** an advisory lock is recommended (D-123).
- **Native date and time inputs follow the browser's language** (DV-T14).
- **Production setup not covered by this repository:** no Nginx file (D-116); a shared rate-limit store before scaling out; staff MFA; a lookup-key re-keying tool (`docs/security.md` §6).
