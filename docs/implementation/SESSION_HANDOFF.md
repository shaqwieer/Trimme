# TRIMME Session Handoff

- Updated at: 2026-09-26 (Session 3, after Phase 04)
- Branch: `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). Phase 04 is committed locally and **not pushed** (push only when the user asks).
- HEAD commit: `54cf79a` (Phase 04) plus a follow-up fix commit (enumeration-safe forgot-password on SMTP failure; `seed` builds its image). Run `git log --oneline -3`.
- Working tree status: clean after the commit. The local Docker stack is **running** (web 3300, API 8080, DB 5434, Mailpit UI 8325).
- Current phase: 04 is complete. The user asked to continue with Phase 05 in the same session.
- Phase score: 100 / 100 (Phase 04)
- Last fully completed phase: 04, identity, sessions, roles and permissions

## Completed this session
- Re-validated Phase 03 (backend 73/56/27, web 147, compose smoke, E2E 30/30).
- **Phase 04** (details and evidence in `phases/phase-04-identity.md`):
  - ASP.NET Core Identity on the shared context (schema `identity`), one migration `20260926080806_Identity` (also `infra.data_protection_keys`).
  - Customer passwordless sign-in (6-digit OTP, dev inbox), profile completion; staff email + password sign-in with lockout, forgot/reset by email, admin invitations.
  - Cookie sessions: 15-min access cookie, rotating refresh cookie scoped to `/api/v1/auth`, reuse detection, per-request session validation, session list / revoke / revoke-others / sign-out.
  - CSRF double-submit on every unsafe `/api/v1` request; default-deny `/api/v1`; permission catalogue (56 codes) + seed roles synchronised by `migrate`; endpoint matrix test.
  - Mailpit + one-shot `seed` (bootstrap SuperAdmin) in compose.
  - Web auth pages, `/account`, `/account/security`, `/admin`, `/shop`, server guards, silent refresh, permission-aware admin navigation.
  - Docs: `docs/permissions-matrix.md`, architecture auth section, README, design deviations, D-050…D-058, TRACEABILITY.

## Verification evidence
- Command: `dotnet build Trimme.slnx -c Release --no-incremental`
  Result: PASS, 0 warnings
- Command: unit / architecture / integration tests
  Result: PASS, 99 / 56 / 66 (integration uses Testcontainers PostGIS + Mailpit)
- Command: negative probe on the endpoint matrix test
  Result: fails as expected when a permission is removed; reverted
- Command: `dotnet ef migrations has-pending-model-changes`
  Result: PASS, no changes
- Command: `pnpm lint`, `pnpm typecheck` (+ E2E), `pnpm format:check` (+ E2E), `pnpm test`, `pnpm openapi:check`, `pnpm build`
  Result: PASS (165 web tests)
- Command: clean-volume `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 bash infra/scripts/compose-smoke.sh`
  Result: PASS
- Command: `E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 pnpm e2e`, run twice
  Result: PASS, 36/36 both times
- Command: fresh-machine check (images and volumes deleted) → compose smoke + E2E
  Result: PASS, 36/36
- Command: gitleaks `dir` + `git`
  Result: PASS, no leaks

## Database and migrations
- Created: `20260926080806_Identity`. Applied locally only (Testcontainers databases and the local compose volume).

## Decisions added
- D-050 Identity on the shared context; customer name/locale/terms and encrypted mobile on the Identity user (CustomerProfile moves to Phase 12).
- D-051 Permission catalogue, managed vs editable seed roles, default deny, per-request permission resolution.
- D-052 Cookie sessions, refresh rotation, reuse detection, immediate revocation, DP keys in the database, web session restore.
- D-053 CSRF on every unsafe request.
- D-054 OTP rules. D-055 Staff passwords, lockout, reset, invitations.
- D-056 Email channel and production-only secrets. D-057 Strict JSON numbers. D-058 Local stack additions (seed, Mailpit, dev rate limits, submit-time validation on sign-in forms).

## Known issues or blockers
- CI has not run on the Phase 04 commit (not pushed). The CI stack job uses Mailpit on its default port 8025, which matches the E2E default.
- This machine has port 8025 taken: use `TRIMME_MAILPIT_PORT=8325` and `E2E_MAILPIT_URL=http://localhost:8325` (recorded in CLAUDE.md).
- The `revoking other devices` E2E waits 31 s for the per-number resend cooldown.

## Exact next action
1. Start Phase 05 (`phases/phase-05-tenancy-privacy.md`). Re-validate with `dotnet test --project tests/Trimme.IntegrationTests -c Release` and `pnpm e2e` against the stack.
2. Build on Phase 04: shop users get `ShopId` claims (`ICurrentUser` gains `ShopId`), `InvitationIssuer` issues shop-user invitations (`Admin_InvitesShopUser`), and the phone helper `MobileNumber` is replaced by the platform phone value object (keep stored values stable).

## Files intentionally left modified
- None.
