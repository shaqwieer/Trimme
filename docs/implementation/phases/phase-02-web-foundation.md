# Phase 02 — Web foundation, i18n/RTL, tokens, fonts, logo, app shells

**Status:** [~] · **Score:** 95/100 · **Session:** 2 (2026-09-25). Only item 2.12 is open: the acceptance criterion "CI web job is green" awaits the first GitHub Actions run.

## Goal and user-visible outcome
The Next.js 16 app runs at `/ar` (RTL) and `/en` (LTR) with TRIMME's real design tokens, fonts and logo. It has three layout shells: a public/customer mobile-first shell with bottom nav, a shop dashboard shell and an admin dashboard shell (desktop sidebar with a mobile drawer). A typed API client is generated from OpenAPI, and the test harnesses (Vitest and Playwright) are running in CI and Docker Compose.

## Prerequisites
Phase 01 complete (OpenAPI document exists, compose file exists).

## In scope
- pnpm workspace (`pnpm-workspace.yaml`), `apps/web` with Next.js 16 App Router, strict TypeScript, ESLint (flat config) and Prettier.
- Tailwind v4 configured **only** from CSS variables generated from design tokens (`apps/web/src/styles/tokens.css`), with no default palette leakage. Tokens are taken from `design/analysis/01-design-system-and-docs.md` §1. AA-corrected text tokens (D-021) have their contrast computed and recorded.
- **Reference screenshots**: render each design screen at 390/768/1440 through the Claude Design preview (a fresh `render_preview`; never persist the preview URL) and save them to `design/reference/<screen>-<width>.png`. These are the visual baseline for later phases.
- Fonts: Tajawal and Inter via `next/font/google`, with every weight the design uses (D-022). Numeral rule per locale (D-029, DV-T03).
- `next-intl`:
  - `[locale]` segment, middleware/proxy locale negotiation (default `ar`), `dir`/`lang` on `<html>`.
  - Message catalogs `messages/ar.json` and `messages/en.json`, with a key-parity test.
  - Formatters for date/time/number/currency (SAR, `Asia/Riyadh`).
  - Bidi helpers (`<bdi>`, LTR isolation for phones, times and prices).
- Logo component using `next/image` from the optimised `trimme-logo.png`. It must not be distorted. Favicon/app icons are derived without altering the mark.
- Layout shells: `(public)`, `(customer)`, `shop`, `admin`.
  - Navigation items come from config and are permission-aware later (placeholders now).
  - Mobile drawer.
  - Skip link.
- API client:
  - `openapi-typescript` (or equivalent) generation from `apps/api/openapi/v1.json`, with a drift check.
  - A fetch wrapper for server (forwards cookies) and client (credentials include, CSRF header placeholder).
  - A problem-details parser.
- TanStack Query provider (client islands only).
- Vitest + Testing Library setup.
- Playwright harness with an RTL/LTR smoke test and a 3-viewport screenshot helper.
- `apps/web/Dockerfile` (standalone output, non-root), plus a `web` service in compose; Nginx dev example optional.
- CI web job: lint, typecheck, test, build, Playwright smoke against compose.
- `robots` noindex default for non-public segments (full SEO work is in Phase 11).

## Explicitly out of scope
The component library beyond the shells (Phase 03), auth, and real pages.

## Checklist (100 points)
- [x] 2.1 (5) Re-validate against repo state; check Next.js 16 / next-intl / Tailwind v4 docs for current APIs; refine checklist.
- [x] 2.2 (8) Reference screenshots of all design screens in `design/reference/`. They are captured at the prototype's 1440 canvas, which already contains the 390px phone frames; see D-046.
- [x] 2.3 (8) Workspace + Next.js app + strict TS + ESLint/Prettier; `pnpm build` passes.
- [x] 2.4 (14) Token CSS variables + Tailwind theme mapping + contrast audit table (DV-T01) + lint guard against raw hex in components.
- [x] 2.5 (8) Fonts + numerals rule + formatters + bidi helpers with tests (R-WEB-06).
- [x] 2.6 (12) next-intl routing `/ar` and `/en`, `dir`/`lang`, catalogs, key-parity test (R-WEB-04/05).
- [x] 2.7 (6) Logo component + icons/metadata images (R-WEB-03).
- [x] 2.8 (14) Public/customer, shop and admin shells (sidebar + mobile drawer + bottom nav) using logical CSS properties (DV-T04), keyboard accessible.
- [x] 2.9 (8) OpenAPI → TS types + fetch wrappers + problem-details parser + drift check (R-FND-05).
- [x] 2.10 (6) Vitest + Playwright harness; RTL/LTR smoke; 3-viewport screenshot helper.
- [x] 2.11 (6) Web Dockerfile + compose `web` service; `docker compose up --build` serves `/ar`.
- [~] 2.12 (5) CI web job; README updates; control files; commit.

## Files/modules expected to change
`pnpm-workspace.yaml`, `package.json`, `apps/web/**`, `infra/docker-compose.yml`, `.github/workflows/ci.yml`, `design/reference/**`, `README.md`.

## Data model and migration impact
None.

## API contracts and UI routes
- Routes `/[locale]` (placeholder home) and shell demo routes under `/[locale]/dev/*` (development only).

## Security, tenancy, privacy, RTL, a11y, responsive
- No tokens in web storage.
- Server fetch forwards only the cookies the API needs.
- RTL mirroring uses logical properties.
- Visible focus rings come from tokens.
- Touch targets are ≥44px.

## Tests and verification commands
```
pnpm install --frozen-lockfile
pnpm lint && pnpm typecheck && pnpm format:check && pnpm test && pnpm openapi:check && pnpm build
bash infra/scripts/compose-smoke.sh                  # add TRIMME_WEB_PORT=3300 when port 3000 is busy
E2E_BASE_URL=http://localhost:3000 pnpm e2e          # Playwright smoke (needs the stack; dev routes enabled)
```
**Smallest decisive re-verification for the next session:** `pnpm test`, then `bash infra/scripts/compose-smoke.sh` followed by `pnpm e2e`.

## Acceptance criteria
- `/ar` renders RTL and `/en` renders LTR.
- Tokens visibly match the reference screenshots for the shell chrome.
- The key-parity test fails when a key is removed.
- CI web job is green.

## Rollback / recovery
Revert the phase commit. The web app is independent of the API database. `docker compose down` removes the `web` container.

## Completion evidence (observed 2026-09-25, Session 2)

**2.1 Re-validation.** `main` was at `14b46f2`, 4 commits ahead of `origin` (not pushed).
- Phase 01's decisive checks re-passed: 56/56 architecture tests, and `compose-smoke.sh --down` was healthy.
- Package versions were confirmed from npm.
- The Next.js 16 changes were read from the bundled docs in `node_modules/next/dist/docs` and the upgrade guide: `proxy.ts`, async request APIs, `next lint` removed, Turbopack by default, and `next/root-params`.
- next-intl's `next/root-params` setup was confirmed.
- Version constraints found along the way:
  - TypeScript 7.0 is not supported by typescript-eslint, so TypeScript is pinned to 6.0.3.
  - jsdom 30 needs Node 22.22.2 or later, so jsdom 29.1.1 is used.
  - ESLint 10 breaks eslint-plugin-react's version detection, so the React version is set explicitly.

All three are recorded in D-043.

**Final gate run:**

| Command | Result |
|---|---|
| `pnpm lint` (ESLint 10, `--max-warnings 0`) | PASS |
| `pnpm typecheck` (`next typegen` + `tsc --noEmit`, strict + `noUncheckedIndexedAccess`) | PASS |
| `pnpm format:check` (Prettier + tailwind plugin) | PASS |
| `pnpm test` (Vitest) | PASS, 53/53 in 9 files (49 at the first gate run, plus 4 `devRoutes.test.ts` tests proving dev routes return 404 in production unless explicitly enabled): formatters ar/en (Gregorian Arabic, Riyadh time zone, numeral rule), message key parity and ICU placeholders, token identity and WCAG AA contrast for every text and status token, navigation (longest-prefix, permission filter, no transfer entry), Logo aspect ratio, DashboardShell (aria-current, drawer dialog, focus trap, Escape returns focus, permission hiding), CustomerNav, problem-details parsing |
| `pnpm openapi:check` | PASS: "OpenAPI client types are up to date." |
| `pnpm build` (Next 16.3.6, Turbopack, standalone) | PASS: `/ar` and `/en` prerendered (SSG); dev and catch-all routes dynamic; proxy active |
| `dotnet build -c Release` + architecture tests (backend unchanged) | PASS: 0 warnings; 56/56 |
| `TRIMME_WEB_PORT=3300 bash infra/scripts/compose-smoke.sh` (clean volume: postgis → migrate → api → web) | PASS: API ready, web `/ar` 200, and the web-origin proxy `/api/v1/meta` returned `{"name":"TRIMME API","version":"v1",…}`; web container `healthy` |
| `E2E_BASE_URL=http://localhost:3300 pnpm e2e` (Playwright against the Docker stack) | PASS, 20/20 |
| gitleaks v8.30.1 `dir` scan | PASS: no leaks, after ignoring git-ignored `.next`/`node_modules`/`bin`/`obj`. The 9 raw findings were all Next's per-build keys in `apps/web/.next` |
| `actionlint .github/workflows/ci.yml` | PASS, exit 0 |

The 20 E2E tests cover:
- RTL and LTR attributes;
- redirects for a French or Arabic browser to `/ar` and an English browser to `/en`;
- the language switch and the localized 404;
- empty web storage;
- the `/api` proxy and security headers;
- axe with 0 serious or critical violations on `/ar`, `/en` and all three shells;
- the dashboard sidebar on the right edge in RTL and the left in LTR, with a drawer below 1200px that opens and closes with Escape;
- the customer bottom bar on phones versus the header nav on desktop;
- `bdi` LTR isolation of the phone number;
- 3-viewport captures at 390, 768 and 1440 attached to the report.

**Clean-clone Linux verification (mirrors the CI `web` job):**
- A `git clone` of `a8413e4` was run in `node:22` (Linux, case-sensitive).
- These all PASSED: `pnpm install --frozen-lockfile`, `pnpm lint`, `pnpm typecheck` (`next-env.d.ts` and route types regenerate on a fresh checkout), `pnpm format:check`, `pnpm test` (49/49 at that commit), `pnpm openapi:check`, and `tsc -p tests/E2E`.
- The E2E TypeScript is now type-checked in CI too: `pnpm --filter @trimme/e2e typecheck`.

**Acceptance proofs:**
- **Key parity:** removing `nav.shop.walkIn` from `en.json` made `messages_have_key_parity` fail with `missingInEn: ["nav.shop.walkIn"]`. The file was restored, and 3/3 tests pass again.
- **Lint guards:** a probe component using `localStorage`, a raw `#10283d` and Arabic JSX text produced exactly 3 errors: `no-restricted-globals`, `no-restricted-syntax` and `react/jsx-no-literals`.
- **Production server checks** (`next start`):
  - `/` redirected with 307 to `/ar`, or to `/en` for `Accept-Language: en-US`;
  - `<html lang="ar" dir="rtl">` and `<html lang="en" dir="ltr">`;
  - `/ar/nope` returned 404;
  - `/ar/dev/shells/shop` returned 404 in production and 200 with `TRIMME_ENABLE_DEV_ROUTES=true`;
  - the security headers were present.
- **Visual review:** screenshots of the shells at 390/768/1440 were compared with `design/reference/1440/s-overview.jpg` and `c-home.jpg`. Matching elements: the navy 264px sidebar with a lightened logo and a `BUSINESS` eyebrow, active item `rgba(255,255,255,.1)` in bold white, glass header, bottom bar with 5 items (active in navy and bold), token radii and shadows, Tajawal for Arabic, and Inter for Latin digits.
- **Design screenshots:** 33 captured into `design/reference/1440` (3.5 MB) with a README.

**Deviations and notes:**
- Reference screenshots were captured only at the 1440 canvas (D-046).
- Port 3000 on this machine is taken by an unrelated Node process (other projects also use 3001 and 3002). The compose default is still 3000; verification ran with `TRIMME_WEB_PORT=3300`.
- **CI:** the `web` and `stack` jobs pass `actionlint`, and every step was run locally (including on a clean Linux clone). **No GitHub run has happened yet, because nothing has been pushed.** Item 2.12 stays `[~]` until it runs green.
- Placeholder colour changed during the phase: the token test caught `#687888` at 4.30:1 on the page background, so it is now `#5F6F80` (D-039).

**Commit:** `9c1fcd2`.

## Remaining risks → next phase
- The digit convention (D-040) follows the design's own rule; ask the client to confirm it.
- The logo's minimum-width rule is not met at the design's own header sizes (D-042).
- Next: Phase 03, the design-system component library.
