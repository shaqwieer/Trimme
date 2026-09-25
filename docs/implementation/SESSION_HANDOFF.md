# TRIMME Session Handoff

- Updated at: 2026-09-25 (end of Session 2, after Phase 02)
- Branch: `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git)
- HEAD commit: the docs commit recording the hash, on top of Phase 02 commit `9c1fcd2`. Run `git log --oneline -8`.
- Working tree status: clean after those commits. **Phases 01 and 02 are not pushed**; pushing happens only when the user asks. The local `docker compose` stack has been **stopped** (`down`).
- Current phase: 02 is implemented. Items 1.12 and 2.12 are `[~]` until the first green GitHub Actions run. Phase 03 has not started.
- Phase score: 95 / 100 (Phase 02) and 94 / 100 (Phase 01)
- Last fully completed phase: 00. Phases 01 and 02 pass every local gate and are waiting only on remote CI.

## Completed this session
- **Phase 01, backend foundation.** See its phase file. Commits `37500ce`, `f252e01`, `4779098` and `14b46f2`.
- **Phase 02, web foundation:**
  - **Workspace and tooling:** a pnpm workspace (`apps/web`, `tests/E2E`) with Next.js 16.3.6, React 19.3, strict TypeScript 6.0.3, ESLint 10 (guards against raw hex, JSX literals and Web Storage), Prettier and Tailwind v4.
  - **Design tokens:** `src/styles/tokens.css` is a Tailwind `@theme` with the defaults reset. The text tokens are AA-corrected (D-039). Breakpoints are 390/768/1200/1440 (D-041).
  - **Internationalisation:**
    - next-intl serves `/ar` (RTL, the default) and `/en` (LTR) through `proxy.ts` and `next/root-params`.
    - The message catalogs have a key-parity test, and 404 pages are localized.
    - The numeral rule, Gregorian calendar and Asia/Riyadh time zone are set (D-040).
    - There is an `<Ltr>` bidi helper, and the font stack is Inter → Tajawal (D-047).
  - **Logo:** only the transparent margin was cropped. It is rendered with `next/image`, with an on-navy treatment (D-042). The app icon is the design's "T" mark (`app/icon.svg`).
  - **Shells:** PublicShell, CustomerShell (bottom bar) and DashboardShell. The dashboard has a 264px navy sidebar at 1200px and above, and a focus-trapped drawer below that. The navigation config carries permission metadata and has no transfer entry.
  - **API layer:**
    - `schema.d.ts` is generated from `apps/api/openapi/v1.json`, with a drift check.
    - `browserApi` sends credentials and the CSRF header.
    - `getServerApi()` forwards cookies and uses `no-store`.
    - `ApiError` handles problem details.
    - Also added: `QueryProvider`, and `/api` + `/hubs` rewrites (D-044).
  - **Dev-only routes:** `/[locale]/dev/shells/{customer,shop,admin}` return 404 in production unless `TRIMME_ENABLE_DEV_ROUTES=true` (D-045).
  - **Tests:** 49 Vitest tests and 20 Playwright tests (including axe), plus the `captureViewports` helper for 390/768/1440.
  - **Docker:** `apps/web/Dockerfile` builds a standalone image that runs as non-root with a HEALTHCHECK. Compose has a new `web` service, and `compose-smoke.sh` now checks the web app and its API proxy.
  - **CI:** new `web` and `stack` jobs (compose + Playwright). `.gitleaks.toml` now ignores build paths.
  - **Design references:** 33 screenshots in `design/reference/1440/`, with a README (D-046).
  - **Docs:**
    - README and `docs/architecture.md` §3;
    - decisions D-039 to D-047;
    - design deviations: DV-T01/T02/T03 applied, DV-T04/T05 partially applied;
    - TRACEABILITY updated.

## Verification evidence
- Command: `pnpm lint`, `pnpm typecheck`, `pnpm format:check`
  Result: PASS (0 warnings)
- Command: `pnpm test`
  Result: PASS, 53/53 across 9 files (including dev-route gating in production)
- Command: `pnpm openapi:check`
  Result: PASS, types are up to date
- Command: `pnpm build`
  Result: PASS; `/ar` and `/en` are statically generated
- Command: `dotnet build -c Release` + architecture tests
  Result: PASS, 0 warnings, 56/56
- Command: `TRIMME_WEB_PORT=3300 bash infra/scripts/compose-smoke.sh` (clean volume)
  Result: PASS. The API was ready, web `/ar` returned 200, and the web-origin `/api/v1/meta` returned the API metadata. The web container was healthy.
- Command: `E2E_BASE_URL=http://localhost:3300 pnpm e2e`
  Result: PASS, 20/20
- Command: gitleaks `dir` scan
  Result: PASS, no leaks (build output excluded)
- Command: `actionlint`
  Result: PASS
- Command: clean `git clone` of `a8413e4`, run in Linux containers (`node:22`, `dotnet/sdk:10.0`)
  Result: PASS for all of: `pnpm install --frozen-lockfile`, lint, typecheck, format, 49/49 web tests, openapi check, E2E `tsc`; `dotnet build -c Release` with 0 warnings; 73/73 unit, 56/56 architecture and 27/27 integration tests.
- Key-parity negative test (a key removed, then restored): failed as expected.
- Lint-guard probe: all 3 rules fired.

## Database and migrations
- No new migrations. `Initial` from Phase 01 was applied to the local compose database, which was recreated with a clean volume during the smoke runs.

## Decisions added
- D-039: AA text-colour values
- D-040: numerals and the Gregorian calendar
- D-041: breakpoints, with desktop at 1200
- D-042: logo transparent-margin crop and on-navy filter
- D-043: web toolchain versions (TypeScript 6.0.3, jsdom 29, ESLint 10 with an explicit React version)
- D-044: same-origin API
- D-045: dev-route gating
- D-046: reference screenshots at the 1440 canvas
- D-047: Inter → Tajawal font stack

## Known issues or blockers
- **GitHub Actions have never run**: nothing has been pushed since `eb24f19`. Push when the user asks, then check the `backend`, `web`, `secret-scan` and `stack` jobs.
- **Port conflicts on this machine:** web ports 3000–3002 are used by other local projects, so verification used `TRIMME_WEB_PORT=3300`. Database ports 5432/5433 are also taken, so compose defaults to 5434.
- **Node version:** the dev machine runs Node 22.18.0, so the web toolchain avoids packages that need 22.22 or later (D-043). The Docker image uses 22.23.
- **Awaiting client confirmation:** the digit convention (D-040), and the logo minimum-width rule versus the design's header sizes (D-042).

## Exact next action
1. If the user approves, push `main` and check the four GitHub Actions jobs. When they are green, set items 1.12 and 2.12 to `[x]` and record the run URL.
2. Start Phase 03 (`phases/phase-03-design-system.md`). Re-validate first:
   - `git status`
   - `pnpm test`
   - `bash infra/scripts/compose-smoke.sh` (add `TRIMME_WEB_PORT=3300` if port 3000 is busy)
   - `E2E_BASE_URL=http://localhost:<port> pnpm e2e`
3. Build the component library from `design/analysis/01-design-system-and-docs.md` §2, using `design/reference/1440/ds-components.jpg` and `ds-foundations.jpg`. Add the dev-only gallery at `/[locale]/dev/components`.

## Files intentionally left modified
- None.
