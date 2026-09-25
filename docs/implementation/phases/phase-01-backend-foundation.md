# Phase 01 — Backend & infrastructure foundation

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
A runnable, tested ASP.NET Core 10 API skeleton backed by PostgreSQL/PostGIS and started by Docker Compose.
- Health endpoints, problem details, OpenAPI, structured logging with correlation IDs, and strict CORS/security headers are in place.
- Module and architecture boundaries are enforced.
- CI builds and tests the backend.

No product features yet.

## Prerequisites
- Phase 00 approved.
- D-004 answered, or the recommended default accepted.
- Docker Desktop running.

## In scope
- `global.json` pin (D-002).
- `Directory.Build.props`: nullable, warnings-as-errors, analyzers, `LangVersion` latest, deterministic builds.
- `Directory.Packages.props`: central package management.
- Solution `Trimme.slnx` (or `.sln`) with:
  - `apps/api` (host);
  - `src/BuildingBlocks/{Domain,Application,Infrastructure}`;
  - empty module projects (`Domain/Application/Infrastructure/Api` or a folder-per-layer convention, whichever D-001's addendum chooses) for all 12 modules;
  - `tests/{UnitTests,IntegrationTests,ArchitectureTests}`.
- BuildingBlocks:
  - Domain: strongly typed ID pattern, `Entity`/`AggregateRoot` with domain events, `IClock`, `Result`/`Error` with stable codes, `IConcurrencyVersioned`.
  - Application: command/query dispatcher per D-004, validation pipeline (FluentValidation), `ICurrentUser` abstraction.
  - Infrastructure: EF base configuration, UTC conventions.
- Persistence:
  - Npgsql + NetTopologySuite; PostGIS and `btree_gist` extensions enabled in the initial migration.
  - Schema-per-module convention.
  - `Migrations_ApplyToEmptyDatabase` integration test.
- API host:
  - `/api/v1` route group convention.
  - RFC 7807 problem details with an `errorCode` extension; exception handler that never leaks stack traces.
  - OpenAPI document at `/openapi/v1.json` (dev), plus a script to export it to `apps/api/openapi/v1.json`.
  - `/health/live` and `/health/ready` (the latter checks the DB).
  - Correlation-ID middleware (`X-Correlation-Id`).
  - Serilog (or `Microsoft.Extensions.Logging` + JSON console) with a redaction enricher skeleton.
  - Strict CORS from configuration.
  - Security headers middleware and request size limits.
  - Rate limiter registration with named policies (`auth`, `otp`, `search`, `availability`, `booking`, `review`, `qr`), stubs for now.
  - `CancellationToken` convention.
- Dev-only seed command: `dotnet run --project apps/api -- seed --dev`. It refuses to run unless the environment is `Development` and `TRIMME_ALLOW_DEV_SEED=true`. No data yet, framework only.
- `infra/docker-compose.yml`: `postgis/postgis:17-3.5` (or current stable), `api`. The `web` service is added in Phase 2.
- `apps/api/Dockerfile` (multi-stage, non-root) and `.dockerignore`.
- `.env.example`.
- `.github/workflows/ci.yml`, backend job: restore, build with warnings as errors, unit + architecture + integration tests via Testcontainers, `dotnet ef migrations` check for pending model changes, OpenAPI export diff.
- Architecture tests (NetArchTest or ArchUnitNET): domain has no infrastructure/EF/ASP.NET references; modules reference each other only via contracts; endpoints take `CancellationToken`.
- Root `README.md` (initial) and `docs/architecture.md` (initial, with Mermaid deployed-topology diagram).

## Explicitly out of scope
Identity/auth, any domain entities, the web app, Hangfire, SignalR hubs, and WhatsApp. Hangfire and SignalR packages are not added until their phases.

## Checklist (100 points)
- [ ] 1.1 (5) Re-validate this phase against repo state and D-004; refine this checklist.
- [ ] 1.2 (8) `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`; solution with all projects; `dotnet build -warnaserror` passes.
- [ ] 1.3 (12) BuildingBlocks primitives + unit tests (strong IDs, Result/Error, clock, entity events).
- [ ] 1.4 (8) Dispatcher/pipeline per D-004 + validation behaviour + tests.
- [ ] 1.5 (10) EF/Npgsql/NTS setup, schema convention, initial migration enabling `postgis` and `btree_gist`; `Migrations_ApplyToEmptyDatabase` passes on Testcontainers.
- [ ] 1.6 (12) API host: `/api/v1` group, problem details + stable error codes, exception handling, health live/ready, correlation IDs, CORS, security headers, size limits, rate-limit policy stubs; integration tests R-FND-03/04/06/14/15.
- [ ] 1.7 (6) OpenAPI generation + committed `openapi/v1.json` + drift check script.
- [ ] 1.8 (6) Logging with a redaction enricher skeleton (phone/token patterns) + unit test.
- [ ] 1.9 (8) Architecture tests (R-FND-01, R-FND-12).
- [ ] 1.10 (5) Dev seed command framework with environment guards + test.
- [ ] 1.11 (8) Dockerfile + compose (postgis + api); `docker compose up --build` reaches healthy `/health/ready`.
- [ ] 1.12 (6) CI workflow (backend job) + gitleaks secret scan step.
- [ ] 1.13 (6) README (prerequisites, run, test), `docs/architecture.md` initial with topology Mermaid, `.env.example`; update control files, commit.

## Files/modules expected to change
`global.json`, `Directory.*.props`, `Trimme.slnx`, `apps/api/**`, `src/BuildingBlocks/**`, `src/Modules/*/` (empty projects), `tests/**`, `infra/docker-compose.yml`, `.github/workflows/ci.yml`, `README.md`, `docs/architecture.md`, `.env.example`.

## Data model and migration impact
`0001_Initial`: extensions `postgis` and `btree_gist` only; per-module schemas are created lazily by later migrations.

## API contracts and UI routes
- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json` (development only)

## Security, tenancy, privacy, RTL, a11y, responsive
- No secrets in the repo.
- CORS allowlist comes from configuration.
- Headers: `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`/frame-ancestors, and `Permissions-Policy`.
- Problem details never include exception details outside Development.
- The redaction enricher exists from day one.

## Tests and verification commands
```
dotnet build Trimme.slnx -warnaserror
dotnet test tests/UnitTests tests/ArchitectureTests
dotnet test tests/IntegrationTests          # requires Docker (Testcontainers)
dotnet ef migrations has-pending-model-changes --project <infra project> --startup-project apps/api
docker compose -f infra/docker-compose.yml up --build -d && curl -f http://localhost:8080/health/ready
```

## Acceptance criteria
- All commands above pass.
- Architecture tests fail when a forbidden reference is introduced; verify once with a deliberate violation, then revert it.
- `docker compose up --build` gives a healthy API.

## Rollback / recovery
Revert the phase commit. Remove local Docker volumes with `docker compose down -v`.

## Completion evidence
_(fill with actual command outputs)_

## Remaining risks → next phase
EF context split decision (D-001 addendum). Next: Phase 02 — Web foundation.
