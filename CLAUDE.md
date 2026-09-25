# TRIMME — Claude Code instructions

TRIMME is an Arabic-first (RTL) salon and barber booking marketplace. It has three parts:
- a Next.js 16 web app (`apps/web`)
- an ASP.NET Core 10 modular-monolith API (`apps/api`, `src/`)
- a PostgreSQL/PostGIS database

The full product specification is `TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md` at the repo root. It is authoritative; do not duplicate it here.

## How to resume work

The work is split into phases, one per session. Durable state lives in `docs/implementation/`, not in chat history.

1. Read, in order:
   - this file
   - the spec (repo root)
   - `docs/implementation/MASTER_PLAN.md`
   - `STATUS.md`
   - `SESSION_HANDOFF.md`
   - `DECISIONS.md`
   - `TRACEABILITY.md`
   - the current `docs/implementation/phases/phase-XX-*.md`
2. Inspect `git status`, `git log --oneline -15`, the migration history, and the files changed by the last session.
3. Do not trust the handoff blindly. Re-run the previous phase's smallest decisive verification (listed in its phase file) and reconcile any discrepancy first.
4. Work only on the first approved phase that is not `[x]`. Restate its goal, its score, the completed and remaining items, and the verification plan before coding.
5. While working:
   - keep the phase checklist updated;
   - add numbered entries to `DECISIONS.md`;
   - update `TRACEABILITY.md`;
   - record design deviations in `docs/design-deviations.md`.
6. At the end of the session:
   - run the phase gates;
   - record the **actual** evidence in the phase file;
   - update `MASTER_PLAN.md` (progress table) and `STATUS.md`;
   - rewrite `SESSION_HANDOFF.md` using its template;
   - commit only if the phase is fully verified, then stop. Do not start the next phase automatically.

Status marks:
- `[ ]` not started
- `[~]` in progress
- `[x]` completed **and verified with evidence**
- `[!]` blocked
- `[-]` deferred

## Design source

- The design is imported in `design/source/` (see `design/README.md`). It is the visual source of truth, not production code. `support.js` is the generic Claude Design runtime and is never shipped.
- Analyses in `design/analysis/01…03` contain the tokens, components, every screen's fields, routes and APIs, and the conflicts. Their line citations are +2 relative to the canonical HTML.
- To re-inspect the live project, use the `claude_design` MCP (project `af2d08aa-185e-42e6-be25-43d52be321f3`). Never persist preview (`claudeusercontent.com`) URLs.

## Non-negotiables (quick reference; details in spec §7 and MASTER_PLAN §2)

- Tenant isolation is enforced in the API and data layer. The shop is resolved from claims only.
- The customer phone number never reaches any shop-facing payload or log.
- No barber transfer, in any form. `Professional.ShopId` is immutable.
- Services are shop-owned, with shop-set prices and durations. Subscription plan pricing is managed by SuperAdmin only and is versioned.
- Availability is computed on the server. Double booking is prevented by a transactional recheck, an exclusion constraint and idempotency.
- No payment UI in v1.
- WhatsApp goes through the provider abstraction; use the fake locally.
- Never run production migrations, deploy, create paid resources, or use real credentials.
- Tokens are kept in HttpOnly cookies only, never in web storage.

## Repository

- GitHub: https://github.com/shaqwieer/Trimme.git (`origin`). Main branch: `main`.
- Commit at the end of each verified phase. Push only when the user asks.

## Environment notes

- Windows host. The Bash tool is Git Bash; PowerShell is also available.
- The default `dotnet` is a **preview** SDK. The repo pins stable 10.0.112 via `global.json` (from Phase 01).
- Docker Desktop is required for Testcontainers and compose.
- The web app is a pnpm workspace (`apps/web`, `tests/E2E`). Root scripts: `pnpm lint|typecheck|format:check|test|openapi:check|build|e2e`. The machine has Node 22.18, so avoid packages that need a newer Node (D-043).
- Busy host ports on this machine: 3000–3002 and 5432/5433. Use `TRIMME_WEB_PORT=3300`; compose publishes the DB on 5434 by default.
- `dotnet test` uses Microsoft Testing Platform syntax: `dotnet test --project <path>`.
