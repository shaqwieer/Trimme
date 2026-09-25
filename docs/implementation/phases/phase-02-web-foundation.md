# Phase 02 — Web foundation, i18n/RTL, tokens, fonts, logo, app shells

**Status:** [ ] · **Score:** 0/100

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
- [ ] 2.1 (5) Re-validate against repo state; check Next.js 16 / next-intl / Tailwind v4 docs for current APIs; refine checklist.
- [ ] 2.2 (8) Reference screenshots of all design screens at 390/768/1440 in `design/reference/`.
- [ ] 2.3 (8) Workspace + Next.js app + strict TS + ESLint/Prettier; `pnpm build` passes.
- [ ] 2.4 (14) Token CSS variables + Tailwind theme mapping + contrast audit table (DV-T01) + lint guard against raw hex in components.
- [ ] 2.5 (8) Fonts + numerals rule + formatters + bidi helpers with tests (R-WEB-06).
- [ ] 2.6 (12) next-intl routing `/ar` and `/en`, `dir`/`lang`, catalogs, key-parity test (R-WEB-04/05).
- [ ] 2.7 (6) Logo component + icons/metadata images (R-WEB-03).
- [ ] 2.8 (14) Public/customer, shop and admin shells (sidebar + mobile drawer + bottom nav) using logical CSS properties (DV-T04), keyboard accessible.
- [ ] 2.9 (8) OpenAPI → TS types + fetch wrappers + problem-details parser + drift check (R-FND-05).
- [ ] 2.10 (6) Vitest + Playwright harness; RTL/LTR smoke; 3-viewport screenshot helper.
- [ ] 2.11 (6) Web Dockerfile + compose `web` service; `docker compose up --build` serves `/ar`.
- [ ] 2.12 (5) CI web job; README updates; control files; commit.

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
pnpm -C apps/web lint && pnpm -C apps/web typecheck && pnpm -C apps/web test && pnpm -C apps/web build
pnpm -C apps/web openapi:check
pnpm exec playwright test --project=smoke
docker compose -f infra/docker-compose.yml up --build -d
```

## Acceptance criteria
- `/ar` renders RTL and `/en` renders LTR.
- Tokens visibly match the reference screenshots for the shell chrome.
- The key-parity test fails when a key is removed.
- CI web job is green.

## Rollback / recovery
Revert the phase commit. The web app is independent of the API database.

## Completion evidence
_(fill)_

## Remaining risks → next phase
Digit convention (D-029) confirmation. Next: Phase 03 — Design-system component library.
