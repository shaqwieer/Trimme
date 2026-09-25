# Phase 01 — Backend & infrastructure foundation

**Status:** [x] · **Score:** 100/100 · **Session:** 2 (2026-09-25)

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
- [x] 1.1 (5) Re-validate this phase against repo state and D-004; refine this checklist.
- [x] 1.2 (8) `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`; solution with all projects; `dotnet build -warnaserror` passes.
- [x] 1.3 (12) BuildingBlocks primitives + unit tests (strong IDs, Result/Error, clock, entity events).
- [x] 1.4 (8) In-house dispatcher/pipeline (D-037) + validation behaviour + tests.
- [x] 1.5 (10) EF/Npgsql/NTS setup, schema convention, initial migration enabling `postgis` and `btree_gist`; `Migrations_ApplyToEmptyDatabase` passes on Testcontainers.
- [x] 1.6 (12) API host: `/api/v1` group, problem details + stable error codes, exception handling, health live/ready, correlation IDs, CORS, security headers, size limits, rate-limit policy stubs; integration tests R-FND-03/04/06/14/15.
- [x] 1.7 (6) OpenAPI generation + committed `openapi/v1.json` + drift check script.
- [x] 1.8 (6) Logging with a redaction enricher skeleton (phone/token patterns) + unit test.
- [x] 1.9 (8) Architecture tests (R-FND-01, R-FND-12).
- [x] 1.10 (5) Dev seed command framework with environment guards + test.
- [x] 1.11 (8) Dockerfile + compose (postgis + api); `docker compose up --build` reaches healthy `/health/ready`.
- [x] 1.12 (6) CI workflow (backend job) + gitleaks secret scan step.
- [x] 1.13 (6) README (prerequisites, run, test), `docs/architecture.md` initial with topology Mermaid, `.env.example`; update control files, commit.

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
dotnet build Trimme.slnx -c Release                         # TreatWarningsAsErrors via Directory.Build.props
dotnet test --project tests/Trimme.UnitTests --no-build -c Release
dotnet test --project tests/Trimme.ArchitectureTests --no-build -c Release
dotnet test --project tests/Trimme.IntegrationTests --no-build -c Release   # requires Docker (Testcontainers)
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Trimme.Migrations --startup-project apps/api/Trimme.Api --no-build --configuration Release
bash infra/scripts/compose-smoke.sh --down                  # docker compose up --build, waits for /health/ready
```
**Smallest decisive re-verification for the next session:** `dotnet test --project tests/Trimme.ArchitectureTests` plus `bash infra/scripts/compose-smoke.sh --down`.

## Acceptance criteria
- All commands above pass.
- Architecture tests fail when a forbidden reference is introduced; verify once with a deliberate violation, then revert it.
- `docker compose up --build` gives a healthy API.

## Rollback / recovery
Revert the phase commit. Remove local Docker volumes with `docker compose down -v`.

## Completion evidence (observed 2026-09-25, Session 2)

**1.1 Re-validation.** `git status` was clean and `main` was in sync with `origin/main` at `eb24f19`. `docker info` returned `27.2.0 linux`. `dotnet --list-sdks` showed 10.0.112 stable plus 10.0.300-preview. D-004 had been resolved as D-037 (in-house dispatcher). Package versions were resolved from NuGet (D-003 addendum), and the chosen test image is `postgis/postgis:17-3.5`. The layout decision was recorded as D-038 before scaffolding.

**Final gate run (Release configuration, same commands as CI):**

| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release` (TreatWarningsAsErrors, analyzers `Recommended`, code style enforced) | PASS: 0 warnings, 0 errors |
| `dotnet test --project tests/Trimme.UnitTests --no-build -c Release` | PASS: 73/73 |
| `dotnet test --project tests/Trimme.ArchitectureTests --no-build -c Release` | PASS: 56/56 |
| `dotnet test --project tests/Trimme.IntegrationTests --no-build -c Release` (Testcontainers PostGIS 17-3.5) | PASS: 24/24 |
| `dotnet ef migrations has-pending-model-changes --project src/Trimme.Migrations --startup-project apps/api/Trimme.Api --no-build --configuration Release` | PASS: "No changes have been made to the model since the last migration." |
| gitleaks v8.30.1, `dir` scan of the working tree and `git` scan of history (`.gitleaks.toml`) | PASS: no leaks found |
| `infra/scripts/compose-smoke.sh --down` from a clean volume (`docker compose down -v` first) | PASS. Postgres healthy → `migrate` exited 0 → API `/health/ready` returned `{"status":"Healthy","checks":[{"name":"database","status":"Healthy"}]}`, and `/api/v1/meta` responded. |

**Container checks (compose, an earlier run in the same session):**
- `docker inspect` reported the api container as `healthy` via `dotnet Trimme.Api.dll healthcheck`.
- The response headers included `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, a strict CSP and `X-Correlation-Id`, with no `Server` header.
- `seed --dev` without `TRIMME_ALLOW_DEV_SEED` logged "Development seed refused: TRIMME_ALLOW_DEV_SEED must be set to 'true'". With the flag set, it logged "Development seed completed (0 seeders)".

**Architecture rules proven non-vacuous (1.9):** I temporarily added four violations and ran the architecture tests:
- `Trimme.Modules.Shops.Domain.Violation`, referencing EF `DbContext`;
- `Trimme.Modules.Shops.Domain.Things.ChildViolation`, a child namespace referencing ASP.NET `HttpContext`;
- a public `IQueryHandler` in `Shops.Application`;
- a Bookings → Shops project reference used by code.

Three tests failed as expected:
- `Module_domain_namespace_is_persistence_and_transport_ignorant(Shops)`, which listed both violating types;
- `Module_handlers_and_infrastructure_are_internal(Shops)`;
- `Modules_reference_other_modules_only_through_contracts(Bookings)`.

After reverting, an **unused** cross-module `ProjectReference` still passed, because the compiler drops unused references from metadata. I added `Module_project_files_reference_other_modules_only_through_contracts`, which parses the `.csproj` files. Re-testing with the unused reference in place failed it, listing `Trimme.Modules.Bookings.csproj -> ..\..\Shops\Trimme.Modules.Shops\Trimme.Modules.Shops.csproj`. All violations were then removed, and the tests went back to 56/56.

**Deviations from the plan text, and fixes made during the phase:**
- **Test runner:** tests run on Microsoft Testing Platform. The .NET 10 SDK no longer supports VSTest for xunit.v3 4.x, so `global.json` has `"test": { "runner": "Microsoft.Testing.Platform" }` and the syntax is `dotnet test --project …`.
- **OpenAPI drift check:** implemented as an integration test (`OpenApi_document_matches_committed_contract`) with `TRIMME_UPDATE_OPENAPI=1` to regenerate, rather than build-time generation (D-038).
- **Clock:** .NET `TimeProvider` is used instead of a custom `IClock` (D-038).
- **Host database port:** compose publishes PostgreSQL on host port **5434**, because 5432 and 5433 are occupied on the dev machine.
- **Npgsql GSSAPI noise:** Npgsql tried to load `libgssapi_krb5` in the container. The dev connection strings now set `Gss Encryption Mode=Disable`.
- **Expected first-run error:** on a brand-new database, EF logs one expected `Failed executing DbCommand … __ef_migrations_history` error during the history-table probe. It is documented in README troubleshooting.
- **Schema-per-module convention:** added to `ModuleBase.ConfigureModel`. `ModelConventionTests` verifies schema, snake_case, strong-ID→uuid, xmin concurrency and the declared extensions against a real Npgsql model.
- **`dotnet ef` flag:** `-c` means `--context` for `dotnet ef`, so CI uses `--configuration Release`.

**Not yet verified remotely:** the GitHub Actions workflow has not run on GitHub, because nothing had been pushed when this was recorded. Every step's command was executed locally with the results above. The first remote run is a follow-up in `SESSION_HANDOFF.md`.

**Commit:** `37500ce`.

## Remaining risks → next phase
EF context split decision (D-001 addendum). Next: Phase 02 — Web foundation.
