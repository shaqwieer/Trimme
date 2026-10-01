# TRIMME Session Handoff

- **Updated:** 2026-10-01 (Session 13: Phase 17)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Pushed: everything up to `3330ec0` (the D-116 plan change).
  - Local only: the Phase 17 commit `41412ee` and the `docs:` commit that records its hash (run `git log --oneline -3`).
- **HEAD commit:** the `docs:` commit recording the Phase 17 hash and the gitleaks `git` result, on top of `41412ee` (feat: phase 17 hardening).
- **Working tree:** clean after the commit.
- **Local stack: running, all in compose,** on the volume from this session's fresh `down -v` + `up --build`. After that, the API was rebuilt with the hub fix.
  - Ports: web 3300, API 8080, DB 5434, Mailpit 8325.
  - Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
  - Optional telemetry: `TRIMME_OTLP_ENDPOINT=http://host.docker.internal:4317` with a local collector (`docs/observability.md` §5).
  - This volume has had five full E2E runs and the Linux Chromium run.
- **Current phase:** 17 is complete; it is the last phase (D-116).
- **Phase score:** 98 / 100 (Phase 17). 2 points are held back for the NVDA pass, which has not been run.
- **Last fully completed phase:** 17, localization, SEO, accessibility, security, observability and performance.

## Completed this session
Phase 17 (`phases/phase-17-hardening.md`, D-117 to D-123).

**Security** (`docs/security.md`):
- A per-request nonce CSP on every page (`proxy.ts`). Every page renders per request.
  - Files and 404s get a script-less policy; COOP/CORP `same-origin`; HSTS for an HTTPS site.
  - Every E2E test fails on a CSP violation. On its first run that guard found the API CSP blocking the Hangfire dashboard's inner pages, now fixed.
- Uploads are re-encoded from their pixels with SkiaSharp (D-119): no metadata or appended payload survives, the EXIF orientation is applied, and the longer side is at most 2,560 px. Checked on Linux through compose.
- The Data Protection key ring was stored unencrypted next to the numbers it protects. It is now wrapped with a mounted certificate, required outside Development and Testing, and certificate rotation is supported (D-120).
- Rate limits: a 429 test for each of the nine policies, and a matrix test that pins which endpoints carry which policy.
- Every JavaScript-handled form posts. ZAP had found credentials in the URL on a submit before hydration.
- Scans: `pnpm audit` and the NuGet audit clean (both now in CI); gitleaks clean; ZAP 0 FAIL.
- Phone-leak sweep: logs, spans, Hangfire, outbox and audit, all 0.

**Observability** (`docs/observability.md`, D-118):
- OpenTelemetry for ASP.NET Core, HttpClient, Npgsql/EF, runtime, Hangfire jobs (own filter) and outbox spans and metrics.
- OTLP export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. A sampler drops parentless polling spans.
- `/health/ready` reports `jobs` and `outbox`, Degraded at worst.

**Localization, SEO, accessibility** (D-122, `docs/accessibility.md`):
- `tests/E2E/audit` is the Playwright project `a11y`. The route audit covers 81 routes × 2 locales, each as the right role. Its first runs found:
  - raw permission and audit keys;
  - a hydration error on `/admin/qr`;
  - missing `lang` on parts in the other language;
  - MapLibre's English labels;
  - three inverted chevrons.

  All are fixed.
- The keyboard pass found dialogs dropping focus on close, now fixed in the shared overlays, and touch targets under 44 px, which now grow under `pointer: coarse`.
- SEO: sitemap index plus `/sitemaps/ar.xml` and `/sitemaps/en.xml`; a default Open Graph image; Open Graph on the legal pages; JSON-LD validated by the audit.
- Plural review counts.

**Performance** (`docs/performance.md`, D-123):
- `QueryCountTests` found an N+1 in discovery's availability probe. It is batched (`ProbeManyAsync`, six queries for any number of shops).
- Three indexes (migration `PerformanceIndexes`).
- EF query-shape warnings throw in Testing.
- Bundle budgets (`pnpm bundle:check`): form pages went from 412 to 320 KB gzip. The Zod import pulled every locale pack, and the validation module imported the whole catalogue.
- The k6 smoke passes.
- Lighthouse: a11y, best practices and SEO 100; LCP 3.5–3.8 s on simulated slow 4G (documented gap).
- Public cache headers (D-121).

**Visual review** of 25 screens against `design/reference`. Fixed:
- the landing search bar collapsed at 390 px (`sm` is 390 px, D-041);
- the booking back chevrons;
- the step bar on step 1;
- hidden map credits;
- two admin button styles;
- the suspended-shop badge colour;
- pagination overflowing a phone once a list reaches six pages (found by audit run 4).

The rest is recorded in DV-C11 and DV-T14.

**Also:**
- the SignalR hubs ignore a client abort during the handshake;
- the axe helpers wait for the page title (a soft-refresh race);
- CI runs the bundle budgets, the dependency audits and all Playwright projects.

## Verification evidence
| Command | Result |
|---|---|
| Phase 16 re-validation | `QrTests` 3/3, `qr.spec.ts` 3/3 |
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 429 / 65 / 199 |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web `format:check`, `lint`, `typecheck`, `test`, `build`, `openapi:check`, `bundle:check` | PASS: 385 tests; 79 routes within budget |
| Fresh `down -v` + `up --build`, `CI=1 --workers=2 --retries=0` | 85/85 ×2; 0 × 5xx, 0 × 429. After the hub fix: run 3 84/85 (axe `<title>` race, fixed); run 4 83/85 (pagination overflow at 390 px on a six-page list, fixed); run 5 **85/85** |
| Linux Chromium: route audit, keyboard pass, `public-discovery`, `customer-booking` | 26/26 (`customer-booking` rerun after its seeded numbers' hourly OTP limit, which runs 4 and 5 had used) |
| ZAP baseline; `pnpm audit`; NuGet audit; gitleaks `dir`/`git` | 0 FAIL; clean; clean; no leaks (`git`: 67 commits) |
| Phone-leak sweep | 0 everywhere (logs, 50,601 spans, Hangfire, outbox, audit) |
| k6 smoke; Lighthouse | Passed thresholds; recorded in `docs/performance.md` |

## Database and migrations
- Created and applied locally: `20261001082134_PerformanceIndexes` (index-only), on the compose volume and from empty in the integration tests.
- No production migration was run.

## Decisions added
- D-117: nonce CSP.
- D-118: OpenTelemetry, health and logs.
- D-119: upload re-encoding.
- D-120: key ring encrypted at rest.
- D-121: caching.
- D-122: localization audit rules.
- D-123: performance budgets and the batched probe.
- Design deviations: DV-C11 and DV-T14 added; DV-S09 corrected to applied.

## Known issues or blockers
- **The NVDA pass is not run** (manual, `docs/accessibility.md` §3); 2 points are held back.
- **LCP on simulated slow 4G is 3.5–3.8 s** (target 2.5 s; next steps in `docs/performance.md` §4).
- **A contended booking race costs about 1 s** (deadlock path, D-089); a per-barber advisory lock is recommended (D-123).
- **Native date and time inputs** follow the browser's language (DV-T14).
- **Production setup is not in this repository** (D-116):
  - Nginx with `X-Forwarded-For` overwrite, `KnownProxies`/`KnownNetworks` and `client_max_body_size 6m`;
  - the certificate and the lookup key;
  - SMTP, WhatsApp and map hosts;
  - a shared rate-limit store before scaling out;
  - staff MFA.

  See `docs/security.md` §3 and §6.
- **Carried over:** a backplane for the hubs; a dead-letter replay screen; "any professional" does not retry; packages across professionals (D-020); grace days and limits (D-077).

## Exact next action
1. Push when the user asks: `git push origin main`. CI now runs about 85 Playwright tests, including the route audit, plus the bundle budgets and both audits; the stack job allows 45 minutes.
2. Run the NVDA checklist (`docs/accessibility.md` §3) and record it in the Phase 17 file. That restores the remaining 2 points.
3. All phases are complete. Any further work, such as production deployment or the items above, needs a new plan agreed with the user.

## Files intentionally left modified
- None.
