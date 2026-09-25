# TRIMME

TRIMME is an Arabic-first (RTL), mobile-responsive marketplace for salons and barbers. Customers find nearby shops and book real, server-validated appointments. Shops run their day from a dashboard. Platform admins operate the marketplace.

> **Status:** under phased implementation. See [`docs/implementation/STATUS.md`](docs/implementation/STATUS.md) for current progress and [`docs/implementation/MASTER_PLAN.md`](docs/implementation/MASTER_PLAN.md) for the full plan. The product specification is [`TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md`](TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md).

## Architecture

| Part | Technology | Location |
|---|---|---|
| API | ASP.NET Core 10, modular monolith, EF Core 10 + Npgsql + NetTopologySuite | `apps/api/Trimme.Api`, `src/` |
| Database | PostgreSQL 17 + PostGIS 3.5 (`btree_gist`) | `infra/docker-compose.yml` |
| Web | Next.js 16 (App Router), Arabic RTL `/ar` and English LTR `/en` | `apps/web` (Phase 02) |

The backend layout (decision D-038):

- `src/BuildingBlocks/`
  - `Domain`: strong IDs, entities, domain events, `Result`/`Error`.
  - `Application`: in-house dispatcher and validation pipeline.
  - `Infrastructure`: the shared `TrimmeDbContext`, conventions and PII redaction.
  - `Web`: module and endpoint abstractions, RFC 7807 problem details, correlation IDs, security headers, CORS and rate limits.
- `src/Modules/<Module>/`: one project per feature module, with `Domain`/`Application`/`Infrastructure`/`Api` namespaces and one PostgreSQL schema per module.
- `src/Trimme.Migrations/`: EF Core migrations for the single shared context.
- `tests/`: `Trimme.UnitTests`, `Trimme.ArchitectureTests` (boundary rules) and `Trimme.IntegrationTests` (real PostGIS via Testcontainers).

More detail: [`docs/architecture.md`](docs/architecture.md).

## Prerequisites

- .NET SDK **10.0.112 or later 10.0.x**. The repository pins this with `global.json`. Previews are not allowed, even if one is your machine default.
- Docker Desktop (or Docker Engine) with Compose v2. It is required for the local database and the integration tests.
- Node.js 22+ and pnpm 9+ (from Phase 02, for the web app).

## Run locally

Full stack in Docker (PostGIS, a one-shot migration, then the API):

```bash
docker compose -f infra/docker-compose.yml up --build
# or, with a readiness check:
bash infra/scripts/compose-smoke.sh
```

- API: http://localhost:8080. Health: `/health/live` and `/health/ready`. Metadata: `/api/v1/meta`.
- The OpenAPI document (`/openapi/v1.json`) and the API reference UI (`/scalar`) are served only in Development.
- PostgreSQL is published on host port **5434**, because 5432/5433 are often taken by a local install. Override the defaults in `infra/.env` (copy `infra/.env.example`).

API on the host, with the database from compose:

```bash
docker compose -f infra/docker-compose.yml up -d postgres
dotnet run --project apps/api/Trimme.Api -- migrate      # apply migrations explicitly
dotnet run --project apps/api/Trimme.Api                 # http://localhost:8080 (Development)
```

## Database migrations

The API **never migrates on startup**. Migrations are an explicit step:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Trimme.Migrations --startup-project apps/api/Trimme.Api
dotnet run --project apps/api/Trimme.Api -- migrate
dotnet ef migrations has-pending-model-changes --project src/Trimme.Migrations --startup-project apps/api/Trimme.Api
```

In Docker, the `migrate` service runs the same command before the API starts.

## Development seed data

Deterministic demo data is written only by an explicit command. It runs only in `Development` with an opt-in variable:

```bash
TRIMME_ALLOW_DEV_SEED=true dotnet run --project apps/api/Trimme.Api -- seed --dev
```

Seeders are added phase by phase. Demo credentials, once they exist, are written to `docs/local/DEMO_CREDENTIALS.local.md`, which is git-ignored and never committed.

## Tests

```bash
dotnet build Trimme.slnx                                   # warnings are errors
dotnet test --project tests/Trimme.UnitTests
dotnet test --project tests/Trimme.ArchitectureTests
dotnet test --project tests/Trimme.IntegrationTests        # needs Docker (Testcontainers)
```

- Tests run on Microsoft Testing Platform (xunit.v3); the `test` runner is set in `global.json`.
- The integration tests include `Migrations_ApplyToEmptyDatabase`, `Model_HasNoPendingChanges`, and a drift check of the committed OpenAPI contract `apps/api/openapi/v1.json`.
- After an intentional API change, regenerate the contract with `TRIMME_UPDATE_OPENAPI=1 dotnet test --project tests/Trimme.IntegrationTests` and commit it.

CI (`.github/workflows/ci.yml`) runs three jobs: build + all test suites + migration validation; a gitleaks secret scan; and a Docker Compose smoke test.

## Troubleshooting

- **`Failed executing DbCommand … __ef_migrations_history` logged once during the first `migrate`.** On a brand-new database, the provider probes the migrations history table before creating it. EF logs that probe as an error, then applies the migrations normally. This is expected. Check the final line: `Database is up to date (N migrations applied in total)`.
- **The port is already in use.** Change `TRIMME_DB_PORT` or `TRIMME_API_PORT` in `infra/.env`.

## Deployment

The production topology is a single domain behind Nginx, with the API under `/api` and `/hubs` and the web app for everything else. Migrations run as an explicit release step. The deployment and backup guides are completed in Phase 18 (`docs/deployment.md`, `docs/backup-restore.md`).
