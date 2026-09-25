# Phase 04 — Identity, sessions, roles & permissions

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
- Customers can sign up and sign in using the designed mobile flow (per D-005) and manage their sessions.
- Staff (shop and admin) sign in with email + password and can reset a forgotten password.
- Roles and a data-driven permission catalogue exist, and the API enforces them.
- Navigation is permission-aware.

## Prerequisites
- Phase 03 complete.
- **D-005 decided.**

## In scope
- **Identity module:**
  - ASP.NET Core Identity on EF (schema `identity`) with user types Customer, ShopUser and PlatformAdmin.
  - `CustomerProfile`: name, preferred locale, and verified E.164 mobile (protected per D-026; the full crypto implementation is finalised in Phase 5, this phase uses its interface).
  - `Role`, `Permission`, `RolePermission`, `UserRole`.
  - `RefreshSession`: family, rotation, reuse detection, device label, IP hash, and a revoked flag.
- **Customer auth per D-005 (default):**
  - Endpoints: `POST /api/v1/auth/otp/request` and `POST /api/v1/auth/otp/verify` (returns `isNewUser`), and `POST /api/v1/auth/profile/complete`.
  - A 6-digit code with ≤5 min expiry and ≤3 attempts.
  - Rate limits per phone and per IP; lockout.
  - An `IOtpSender` adapter backed by a dev fake that writes to a dev inbox endpoint/log. The code is never logged in non-dev environments.
- **Staff auth:**
  - `POST /api/v1/auth/staff/sign-in`, `POST /api/v1/auth/password/forgot`, `POST /api/v1/auth/password/reset`.
  - Invite acceptance: `POST /api/v1/auth/invitations/accept`.
  - Lockout.
- **Sessions:**
  - Short access cookie plus a rotating refresh cookie (D-027).
  - `POST /auth/refresh`, `POST /auth/sign-out`, `GET /auth/sessions`, `DELETE /auth/sessions/{id}`, `POST /auth/sessions/revoke-all`.
  - CSRF token endpoint and validation filter.
  - `GET /api/v1/me` returns user type, roles, permissions and shopId (for shop users).
- **Authorization:**
  - Policy-based permission requirement handler.
  - Permission catalogue seeded (see `docs/permissions-matrix.md`, created in this phase) with roles per D-018/D-019. There is no transfer permission.
  - Endpoint-permission matrix test (R-AUTH-09).
- **Email channel:** an `IEmailSender` adapter for staff invitations and password resets. It uses a dev fake backed by a **Mailpit** container in compose (SMTP to `mailpit:1025`, web UI at `:8025`); the production SMTP provider is configured only through environment variables. Templates are localized (ar/en), and tokens are never logged.
- **Admin bootstrap:** development-only, from environment variables (`TRIMME_BOOTSTRAP_ADMIN_EMAIL`/`_PASSWORD`), one-time. Demo credentials are written to `docs/local/DEMO_CREDENTIALS.local.md`, which is git-ignored.
- **Web:**
  - Pages: `/auth/sign-up`, `/auth/sign-in`, `/auth/verify`, `/auth/complete-profile`, `/auth/staff/sign-in`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/accept-invite`, and `/account/security` (sessions list, revoke all).
  - Auth context from `/me`; route guards per surface (customer/shop/admin).
  - Expired-session handling: 401 → a silent refresh attempt → redirect to sign-in with `returnTo`.
  - Permission-aware nav (R-WEB-13).
- Playwright flow E1, auth portion.

## Explicitly out of scope
Shops' data (Phase 5/6), and the real WhatsApp OTP template (Phase 15 wires the Meta adapter).

## Checklist (100 points)
- [ ] 4.1 (5) Re-validate; confirm D-005 outcome; refine checklist.
- [ ] 4.2 (12) Identity schema, user types, roles/permissions entities, migrations.
- [ ] 4.3 (12) Customer OTP flow (request/verify/complete), limits, lockout, fake sender + tests (R-AUTH-01/07).
- [ ] 4.4 (10) Staff sign-in, forgot/reset, invitations + tests (R-AUTH-02/06).
- [ ] 4.5 (14) Cookie sessions, refresh rotation + reuse detection, sign-out, list/revoke/revoke-all + tests (R-AUTH-04/05).
- [ ] 4.6 (8) CSRF + CORS credential config + tests (R-AUTH-08).
- [ ] 4.7 (10) Permission catalogue, seed roles, policy handler, endpoint matrix test, `docs/permissions-matrix.md` (R-AUTH-09).
- [ ] 4.8 (5) Dev admin bootstrap + local demo credentials file (R-AUTH-03, R-DOC-05).
- [ ] 4.9 (11) Web auth pages, `/me` context, guards, expired-session handling, security page, permission-aware nav, no web storage (R-NEG-07).
- [ ] 4.10 (8) E2E auth flow + control files + commit.
- [ ] 4.11 (5) `IEmailSender` + Mailpit dev fake in compose; invite and reset emails delivered to Mailpit in integration tests.

## Files/modules expected to change
`src/Modules/Identity/**`, `apps/api/**` (auth wiring), `apps/web/src/app/[locale]/(auth)/**`, `apps/web/src/app/[locale]/account/security/**`, `apps/web/src/lib/auth/**`, `docs/permissions-matrix.md`, tests.

## Data model and migration impact
- Schema `identity`: users, roles, claims, permissions, role_permissions, refresh_sessions, otp_challenges, invitations.
- Migration `0002_Identity`.

## API contracts and UI routes
As listed in "In scope". All auth endpoints use the `auth`/`otp` rate-limit policies.

## Security, tenancy, privacy, RTL, a11y, responsive
- Codes and tokens are never logged.
- Phones are redacted in logs.
- Refresh cookies are path-scoped.
- Enumeration-safe responses on OTP request and password reset.
- The OTP input is LTR and uses `autocomplete="one-time-code"`.

## Tests and verification commands
```
dotnet test
pnpm -C apps/web test
pnpm exec playwright test auth
```

## Acceptance criteria
- Every R-AUTH item for this phase passes.
- The endpoint matrix test covers every endpoint.
- No tokens are found in storage (E2E).

## Rollback / recovery
Revert the commit, then drop the `identity` schema locally (`docker compose down -v`).

## Completion evidence
_(fill)_

## Remaining risks → next phase
- OTP delivery in production depends on Meta authentication template approval.
- Next: Phase 05 — Tenancy, privacy & audit core.
