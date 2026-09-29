# TRIMME

TRIMME is an Arabic-first (RTL), mobile-responsive marketplace for salons and barbers. Customers find nearby shops and book real, server-validated appointments. Shops run their day from a dashboard. Platform admins operate the marketplace.

> **Status:** under phased implementation. See [`docs/implementation/STATUS.md`](docs/implementation/STATUS.md) for current progress and [`docs/implementation/MASTER_PLAN.md`](docs/implementation/MASTER_PLAN.md) for the full plan. The product specification is [`TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md`](TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md).

## Architecture

| Part | Technology | Location |
|---|---|---|
| API | ASP.NET Core 10, modular monolith, EF Core 10 + Npgsql + NetTopologySuite | `apps/api/Trimme.Api`, `src/` |
| Database | PostgreSQL 17 + PostGIS 3.5 (`btree_gist`) | `infra/docker-compose.yml` |
| Web | Next.js 16 (App Router, RSC-first), next-intl (`/ar` RTL default, `/en` LTR), Tailwind v4 with TRIMME design tokens | `apps/web` |
| E2E | Playwright + axe | `tests/E2E` |

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
- Node.js 22 (22.18 or later) and pnpm 9.9 (`corepack enable`), for the web app and E2E tests.

## Run locally

Full stack in Docker (PostGIS, a one-shot migration, a one-shot development seed, Mailpit, the API, then the web app):

```bash
docker compose -f infra/docker-compose.yml up --build
# or, with a readiness check:
bash infra/scripts/compose-smoke.sh
```

- API: http://localhost:8080. Health: `/health/live` and `/health/ready`. Metadata: `/api/v1/meta`.
- The OpenAPI document (`/openapi/v1.json`) and the API reference UI (`/scalar`) are served only in Development.
- Web: http://localhost:3000/ar (Arabic) and `/en` (English). The web app proxies `/api/*` to the API on the same origin.
- PostgreSQL is published on host port **5434**, because 5432/5433 are often taken by a local install.
- Override the ports (`TRIMME_WEB_PORT`, `TRIMME_API_PORT`, `TRIMME_DB_PORT`) in `infra/.env`; copy `infra/.env.example` to start. For example, set `TRIMME_WEB_PORT=3300` when port 3000 is busy.
- Local email (password resets, staff invitations) goes to **Mailpit**: http://localhost:8025 (`TRIMME_MAILPIT_PORT`).
- The `seed` service creates the bootstrap SuperAdmin once (`TRIMME_BOOTSTRAP_ADMIN_EMAIL` / `_PASSWORD`; local defaults `admin@trimme.local` / `trimme local admin`). Staff sign in at `/ar/auth/staff/sign-in`.
- The seed also creates two demo shops (Al Asala, Barber House), each with an owner and a staff account (`owner@al-asala.trimme.local`, `staff@al-asala.trimme.local`, …). Their password is `TRIMME_DEMO_PASSWORD` (local default `trimme local demo`).
- Since Phase 06 the demo shops have a public profile and a map location in Riyadh (Al Malqa, Hittin), and separate professionals with fake WhatsApp numbers (`+966 50 010 01xx`). Seeding never sends messages.
- Since Phase 07 the seed adds service categories, different services, prices and durations per demo shop, a package each, and professional–service assignments.
- Since Phase 08 the seed adds three subscription plans (monthly, semi-annual, annual; the annual one has two price versions) and one subscription per status: Al Asala Active, Barber House Expiring soon, and two extra shops without accounts, Lamsat Al Rajul (Expired) and Al Madina (Suspended). Subscription dates are relative to the day the seed runs, so in a long-lived database the statuses drift; re-create the volume (`docker compose down -v`) to refresh them. The platform settings row is written by `migrate` with the defaults (D-076).
- Since Phase 09 the seed adds opening hours (Al Asala as designed: Sun–Wed 9 AM–11 PM, Thu and Fri until midnight, closed Saturday; Barber House open past midnight on Thursday), Sultan's own hours, prayer and lunch breaks, a National Day closure and time off (Majed now, Rakan in ten days). Try `GET /api/v1/public/shops/al-asala/availability/dates?serviceId=0199a0de-5a10-7000-8000-000000000401`; the shop edits all of it at `/ar/shop/schedule` (see `docs/availability-and-booking.md`).
- Since Phase 10 the seed adds two demo customers (Noura `+966500100301`, Khalid `+966500100302`; sign in with the development OTP inbox) and sample appointments: completed, no-show, cancelled and two upcoming ones placed on real free slots. Bookings go through `POST /api/v1/bookings` with an `Idempotency-Key` header (see the Bookings section of `docs/availability-and-booking.md`).
- Since Phase 11 the public site is live: the landing page `/ar`, the shop listing `/ar/shops`, `/ar/discover` and `/ar/search` (list, map and filters; choose a district at `/ar/onboarding/location`, kept only in a cookie on your device), the shop pages (`/ar/shops/barber-house`) and professional pages (`/ar/shops/barber-house/professionals/omar`), `/ar/terms`, `/ar/privacy`, `robots.txt` and `sitemap.xml`. The seed adds six completed visits with reviews and rating totals. Public API reads are cached in the API and evicted on every change to public content (D-093). Set `TRIMME_SITE_URL` to the address you browse (for example `http://localhost:3300`) so canonical URLs and the sitemap use it.
- **Images are stored in PostgreSQL** (`media.media_files`, D-064): shop logo, cover and gallery, and professional photos. They are served from `/api/v1/media/{id}`, and a database reset removes them. There is no upload directory.
- **Maps:** the location picker uses MapLibre with OpenStreetMap tiles (development only; set `TRIMME_MAP_TILE_URL` / `NEXT_PUBLIC_MAP_TILE_URL` for another host). Geocoding goes through the API: compose uses the built-in Riyadh gazetteer (`TRIMME_GEOCODER=Fake`), and `dotnet run` in Development uses Nominatim at one request per second. Set `TRIMME_GEOCODER=Nominatim` in `infra/.env` for real lookups in compose.
- Customers sign in with a mobile number and a 6-digit code. Locally the code is not sent anywhere: read it from `GET /api/v1/dev/otp-inbox/latest?phone=%2B9665XXXXXXXX` (Development/Testing only).
- Development-only preview routes (`/ar/dev/shells/shop|admin|customer`) are enabled in the local stack through `TRIMME_ENABLE_DEV_ROUTES=true`. Never set this in production.

API on the host, with the database from compose:

```bash
docker compose -f infra/docker-compose.yml up -d postgres
dotnet run --project apps/api/Trimme.Api -- migrate      # apply migrations explicitly
dotnet run --project apps/api/Trimme.Api                 # http://localhost:8080 (Development)
```

Web app on the host (with the API running from compose or `dotnet run`):

```bash
pnpm install
pnpm dev                                                 # http://localhost:3000 -> /ar
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

With `TRIMME_BOOTSTRAP_ADMIN_EMAIL` and `TRIMME_BOOTSTRAP_ADMIN_PASSWORD` set, the seed creates the first SuperAdmin (only while none exists). Production admins are invited, never seeded. Seeders are added phase by phase. Local demo credentials are listed in `docs/local/DEMO_CREDENTIALS.local.md`, which is git-ignored and never committed.

Outside Development and Testing the API refuses to start without `PersonalData__LookupKey` (base64, 32+ bytes) and `Email__Smtp__Host` (see `.env.example`).

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

Web and end-to-end:

```bash
pnpm lint && pnpm typecheck && pnpm format:check      # ESLint (no warnings), strict TS, Prettier
pnpm test                                              # Vitest: formatters, message key parity, token contrast, shells
pnpm openapi:check                                     # generated API types match apps/api/openapi/v1.json
pnpm build                                             # Next.js production build
pnpm --filter @trimme/e2e install-browsers             # once
E2E_BASE_URL=http://localhost:3000 pnpm e2e            # Playwright smoke against a running stack
```

After an API contract change, regenerate the web types with `pnpm openapi:generate`.

CI (`.github/workflows/ci.yml`) runs four jobs:
- **backend:** build, all .NET suites and migration validation;
- **web:** lint, typecheck, format, unit tests, OpenAPI client drift and build;
- **secret-scan:** gitleaks;
- **stack:** Docker Compose up, then the Playwright smoke tests.

## Troubleshooting

- **`Failed executing DbCommand … __ef_migrations_history` logged once during the first `migrate`.** On a brand-new database, the provider probes the migrations history table before creating it. EF logs that probe as an error, then applies the migrations normally. This is expected. Check the final line: `Database is up to date (N migrations applied in total)`.
- **The port is already in use.** Change `TRIMME_WEB_PORT`, `TRIMME_DB_PORT` or `TRIMME_API_PORT` in `infra/.env`.
- **`pnpm install` fails with an engine error.** The web toolchain needs Node 22.18 or later (see D-043).

## Deployment

The production topology is a single domain behind Nginx, with the API under `/api` and `/hubs` and the web app for everything else. Migrations run as an explicit release step. The deployment and backup guides are completed in Phase 18 (`docs/deployment.md`, `docs/backup-restore.md`).
