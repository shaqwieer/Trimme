# Phase 17 — Localization completion, SEO, accessibility, security hardening, observability, performance

**Status:** [ ] · **Score:** 0/100

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
- [ ] 17.1 (5) Re-validate; refine checklist.
- [ ] 17.2 (14) English/RTL/bidi audit + fixes.
- [ ] 17.3 (8) SEO completion + validation.
- [ ] 17.4 (16) Accessibility audit + fixes (automated + manual).
- [ ] 17.5 (18) Security hardening + scans + phone-leak sweep.
- [ ] 17.6 (12) Observability (OTel, health, dashboards documented).
- [ ] 17.7 (12) Performance review + fixes + budgets.
- [ ] 17.8 (10) Visual review vs reference screenshots.
- [ ] 17.9 (5) Gates, control files, commit.

## Files/modules expected to change
Cross-cutting.

## Data model and migration impact
Index-only migrations, if any.

## API contracts and UI routes
No new contracts.

## Security, tenancy, privacy, RTL, a11y, responsive
This entire phase is dedicated to these concerns.

## Tests and verification commands
The full suites, plus:
- `pnpm exec playwright test --project=a11y`
- the ZAP baseline
- `k6 run`
- Lighthouse CI

## Acceptance criteria
- No known critical or high security issues.
- Axe reports 0 serious findings.
- The performance budgets are met or explicitly documented.

## Rollback / recovery
Revert the commit(s).

## Completion evidence
_(fill)_

## Remaining risks → next phase
Next: Phase 18 — Regression and handover.
