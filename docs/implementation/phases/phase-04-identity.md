# Phase 04 — Identity, sessions, roles & permissions

**Status:** [x] · **Score:** 100/100

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
- [x] 4.1 (5) Re-validate; confirm D-005 outcome; refine checklist. — Phase 03 gates re-run green at session start (backend 73/56/27, web 147, compose smoke, E2E 30/30). D-005/D-037 confirmed: passwordless OTP for customers, email + password for staff.
- [x] 4.2 (12) Identity schema, user types, roles/permissions entities, migrations. — Identity on the shared context (D-050), migration `20260926080806_Identity` (identity schema + `infra.data_protection_keys`), no pending model changes.
- [x] 4.3 (12) Customer OTP flow (request/verify/complete), limits, lockout, fake sender + tests (R-AUTH-01/07). — D-054; `OtpSignInTests` (7 cases).
- [x] 4.4 (10) Staff sign-in, forgot/reset, invitations + tests (R-AUTH-02/06). — D-055; `StaffAuthTests` (shop-user invitation itself needs `Shop`: Phase 05).
- [x] 4.5 (14) Cookie sessions, refresh rotation + reuse detection, sign-out, list/revoke/revoke-all + tests (R-AUTH-04/05). — D-052; `SessionTests` (6 cases, fake clock).
- [x] 4.6 (8) CSRF + CORS credential config + tests (R-AUTH-08). — D-053; `CsrfTests` (4 cases).
- [x] 4.7 (10) Permission catalogue, seed roles, policy handler, endpoint matrix test, `docs/permissions-matrix.md` (R-AUTH-09). — D-051; matrix test proven non-vacuous.
- [x] 4.8 (5) Dev admin bootstrap + local demo credentials file (R-AUTH-03, R-DOC-05). — `BootstrapAdminSeeder`, compose `seed` service, `docs/local/DEMO_CREDENTIALS.local.md` (git-ignored).
- [x] 4.9 (11) Web auth pages, `/me` context, guards, expired-session handling, security page, permission-aware nav, no web storage (R-NEG-07).
- [x] 4.10 (8) E2E auth flow + control files + commit.
- [x] 4.11 (5) `IEmailSender` + Mailpit dev fake in compose; invite and reset emails delivered to Mailpit in integration tests.

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
Session 3, 2026-09-26. All commands run on the Windows dev host (Docker Desktop 27.2, .NET SDK 10.0.112, Node 22.18).

**What was built**
- Backend (Identity module + building blocks):
  - ASP.NET Core Identity on the shared context: 7 Identity tables plus `permissions`, `role_permissions`, `user_sessions`, `refresh_tokens`, `otp_challenges`, `invitations` in schema `identity`; `infra.data_protection_keys`.
  - Customer OTP: `POST /auth/otp/request|verify`, `POST /auth/profile/complete`. Staff: `POST /auth/staff/sign-in`, `/auth/password/forgot|reset`, `/auth/invitations/accept`. Sessions: `POST /auth/refresh`, `/auth/sign-out`, `GET /auth/sessions`, `DELETE /auth/sessions/{id}`, `POST /auth/sessions/revoke-all`, `GET /me`, `GET /auth/csrf`. Admin: `GET /admin/permissions`, `GET /admin/roles`, `POST /admin/staff/invitations`. Dev only: `GET /dev/otp-inbox/latest` (not in OpenAPI).
  - Default-deny `/api/v1` group, permission and user-type policies, CSRF middleware, per-request session validation, user-partitioned rate limits, `Cache-Control: no-store` default, strict JSON numbers.
  - `IReferenceDataSynchronizer` run by `migrate` (permission catalogue + seed roles); bootstrap SuperAdmin seeder; MailKit SMTP sender + localized emails; `IPersonalDataProtector` (Data Protection + HMAC lookup).
- Web: `/auth/sign-up`, `/auth/sign-in`, `/auth/verify`, `/auth/complete-profile`, `/auth/staff/sign-in`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/accept-invite`, `/auth/session`, `/account`, `/account/security`, `/admin`, `/shop`; server guard `requireUser`; browser client with lazy CSRF and refresh-and-retry; `returnTo` open-redirect guard; permission-aware admin navigation from real `/me` permissions.
- Compose: `mailpit` (v1.31.2) and one-shot `seed` services.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| `dotnet test --project tests/Trimme.UnitTests -c Release` | PASS — 99/99 |
| `dotnet test --project tests/Trimme.ArchitectureTests -c Release` | PASS — 56/56 |
| `dotnet test --project tests/Trimme.IntegrationTests -c Release` (Testcontainers PostGIS + Mailpit) | PASS — 66/66 (39 new identity tests) |
| Negative probe: removed `RequirePermission` from `GET /admin/roles` | `Endpoint_WithoutPermission_Returns403` FAILED as expected ("GET /api/v1/admin/roles needs a permission…"); change reverted |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes since the last migration |
| `pnpm lint` / `pnpm typecheck` (web + E2E) / `pnpm format:check` (web + E2E) | PASS |
| `pnpm test` | PASS — 165/165 in 15 files (18 new: returnTo guard, session fetch refresh/retry/expiry, CSRF) |
| `pnpm openapi:check` / `pnpm build` | PASS |
| `docker compose down -v` then `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 bash infra/scripts/compose-smoke.sh` | PASS — API ready, `/ar` 200, proxy OK; seed created the bootstrap SuperAdmin |
| Cookie spike through the Next.js rewrite (curl) | PASS — `trimme-access`, `trimme-refresh`, `trimme-csrf` arrive as three separate `Set-Cookie` headers with the intended attributes; `/me` works from the jar |
| `E2E_BASE_URL=http://localhost:3300 E2E_MAILPIT_URL=http://localhost:8325 pnpm e2e` ×2 | PASS — 36/36 both runs (30 smoke + 6 auth flows, incl. axe 0 serious on sign-up, verify, security, staff sign-in) |
| gitleaks v8.30.1 `dir` and `git` | PASS — no leaks (the dev lookup key is derived at runtime, not a literal) |
| Visual: `/ar/auth/sign-up` at 390 and `/en/auth/staff/sign-in` at 1440 vs `design/reference/1440/c-auth.jpg` | Matches the c-auth frame (logo, centred heading, fixed LTR +966 field, terms, navy button, sign-in link); deviations DV-A21/A22/A28, DV-C01/C02 |

**Found and fixed during the phase**
- The resend cooldown made two tests sign the same number in twice within 30 s → tests advance a fake clock.
- Sign-in forms validated on blur; the error under the autofocused empty email field shifted the "forgot password" link and swallowed the first click (E2E) → sign-in style forms validate on submit (D-058).
- ASP.NET web JSON defaults typed numbers as `integer | string` in OpenAPI → strict number handling (D-057).
- A refresh re-issued the CSRF token, which would break the retried request → only sign-in rotates it.

**Follow-up after review (same session)**
- Forgot-password answered 500 for a known address when SMTP was down (an existence oracle) → the handler logs the delivery failure and always answers 202. `ForgotPassword_KnownEmail_Returns202_EvenWhenEmailDeliveryFails` (SMTP pointed at a closed port) failed before the fix and passes after it. The remaining timing difference is recorded as accepted in D-055.
- The compose `seed` service had no `build:` block and could try to pull `trimme-api:dev` on a fresh runner → it now builds like `migrate`. Verified by deleting both local images and volumes, then `compose-smoke.sh` (PASS, bootstrap admin created) and E2E 36/36.
- Commit: `54cf79a` (phase) plus the follow-up fix commit.

**Database and migrations:** `20260926080806_Identity` created and applied locally (Testcontainers and compose). Not applied anywhere else.

**Smallest decisive re-verification for the next session:** `dotnet test --project tests/Trimme.IntegrationTests -c Release` (identity + matrix), then `pnpm e2e` against the stack.

## Remaining risks → next phase
- OTP delivery in production depends on Meta authentication template approval (Phase 15); until then production answers 503 `otp.delivery_unavailable`.
- Data Protection keys are stored unencrypted at rest (Phase 17 hardening).
- `Admin_InvitesShopUser` (R-AUTH-02 remainder) needs `Shop` — Phase 05 reuses `InvitationIssuer` with a `ShopId`.
- The admin can invite staff only through the API in this phase; the admin UI arrives in Phase 14.
- Next: Phase 05 — Tenancy, privacy & audit core.
