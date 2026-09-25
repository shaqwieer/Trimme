# Phase 00 — Discovery, design import, traceability and master plan

**Status:** [x] · **Score:** 100/100 · **Session:** 1 (2026-09-25)

## Goal and user-visible outcome
A fresh session (or person) can pick up TRIMME from repository state alone. The design is imported and analysed. Every spec requirement is traceable to a phase and a named verification. The work is split into session-sized phases.

## Prerequisites
None.

## In scope
Repository/tooling inspection, Claude Design import, design analysis, requirement inventory, phase plan, decision log, design deviations, durable control files, git initialisation, and the phase commit.

## Explicitly out of scope
Any application scaffolding (`dotnet new`, `create-next-app`), dependency installation, code, migrations.

## Checklist (100 points)
- [x] 0.1 (10) Inspect repository, git state, existing instructions, installed tooling.
- [x] 0.2 (15) Import the Claude Design project; keep a canonical local copy in `design/source/`; record provenance in `design/README.md`.
- [x] 0.3 (20) Analyse every design screen: `design/analysis/01…03`.
- [x] 0.4 (15) Build the requirement inventory: `TRACEABILITY.md`, covering screens, API, data, roles, permissions, integrations, tests and negative requirements.
- [x] 0.5 (15) Write the phase plan (`MASTER_PLAN.md`) and all phase files `phase-00` … `phase-18`.
- [x] 0.6 (10) Write `DECISIONS.md` (D-001…D-036) and `docs/design-deviations.md`.
- [x] 0.7 (10) Write `STATUS.md`, `SESSION_HANDOFF.md`, and the `CLAUDE.md` "How to resume work" section.
- [x] 0.8 (5) `git init` on `main` with `.gitignore`/`.gitattributes`; commit Phase 0 artifacts only.

## Files/modules changed
`CLAUDE.md`, `.gitignore`, `.gitattributes`, `design/README.md`, `design/source/*`, `design/analysis/*`, `docs/design-deviations.md`, `docs/implementation/**`. Pre-existing and preserved: `TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md`, `logo.png`.

## Data model / migration impact
None.

## API contracts and UI routes affected
None implemented. The planned route map is in `MASTER_PLAN.md` §5.

## Security, tenancy, privacy, RTL, accessibility, responsive considerations
- Captured as constraints (`MASTER_PLAN.md` §2) and negative requirements (`TRACEABILITY.md` §1).
- The design preview URL token was not persisted. The only `claudeusercontent` hits are policy text (`CLAUDE.md` and this file); a grep for `t=<hex token>` found nothing.

## Tests and verification commands
No application code exists yet, so no build or test commands apply. Checks run:
- `git status`: not a git repository before this session.
- Tool version commands (see evidence).
- Docker daemon query.
- npm and NuGet registry lookups.
- Design file listing, and byte/line counts.

## Acceptance criteria
- All required control files exist.
- Every spec section (§1–§23) maps to at least one requirement ID or phase.
- Every design screen maps to a route or is documented as non-product.
- Open decisions are listed with recommended defaults and the phases they block.
- No code has been written.

## Rollback / recovery
Documentation only. Revert the Phase 0 commit to undo.

## Completion evidence (observed 2026-09-25)
**Repository before this session:** it held only `TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md` (44,480 bytes, 735 lines) and `logo.png` (54,177 bytes). It was not a git repository and had no `CLAUDE.md`, `README` or `AGENTS.md`.

**Tooling:**

| Tool | Version | Notes |
|---|---|---|
| Node | v22.18.0 | |
| npm | 10.9.3 | |
| pnpm | 9.9.0 | |
| git | 2.45.2.windows.1 | |
| psql client | 16.3 | |
| .NET SDKs | 6.0.428, 7.0.410, 8.0.319, 9.0.318, **10.0.112**, 10.0.300-preview.0.26177.108 | `dotnet --version` resolves to the **preview**. Pin required (D-002). |
| `dotnet new` templates | `webapi`, `xunit` | Both available. |
| Docker | 27.2.0 | Daemon running (`docker info` → OSType linux). |
| Docker Compose | v2.29.2-desktop.2 | |

**Registries reachable:**
- npm: next 16.3.6, next-intl 4.14.7, @tanstack/react-query 5.103.2, tailwindcss 4.3.3, @playwright/test 1.63.0.
- NuGet: Npgsql.EFCore.PG 10.0.3, EF Core 10.0.12, Hangfire.PostgreSql 1.21.1, Hangfire.AspNetCore 1.8.25, MediatR 14.2.0, FluentValidation 12.1.1, Testcontainers.PostgreSql 4.15.0, Npgsql NTS 10.0.3.
- MediatR licensing was checked on mediatr.io: v13+ is commercial, with a free Community tier (see D-004).

**Design import:**
- Project "واجهة تريمي التفاعلية" (id `af2d08aa-…43d52be321f3`) contains `TRIMME.dc.html` (441,593 B), `support.js` (69,150 B), `trimme-logo.png` (54,177 B), `uploads/logo.png` (54,177 B) and `.thumbnail`. There are no comments.
- The files were downloaded through the project preview server. The injected preview harness was then stripped, leaving `design/source/TRIMME.dc.html` at 441,593 B and 4,656 lines. The byte count matches the project listing, and the first 12 lines match `read_file`.
- `support.js` is 69,150 B and is the generic dc-runtime.
- The logo copy in `design/source/` is the repo-root `logo.png`. It has the same byte size as the project file; the preview server re-encoded the PNG it served (59,947 B), so that copy was discarded.
- **No rendered screenshots were captured this session.** Reference screenshots at 390/768/1440 are Phase 2 item 2.2.

**Design analysis:** `01-design-system-and-docs.md`, `02-customer-screens.md` and `03-shop-admin-screens.md` were produced by three parallel read-only analysis agents. No prompt-injection text was found in the design.

**Commit:** recorded in `SESSION_HANDOFF.md` and the `MASTER_PLAN.md` table after it was created.

## Remaining risks
- Open decisions D-004 (MediatR), D-005 (customer auth), D-006 (booking confirmation mode) and D-007 (map provider).
- Several design gaps (DV-A items) require composing new screens.

## Exact next phase
Phase 01 — Backend & infrastructure foundation (`phase-01-backend-foundation.md`), once the user approves the plan and answers D-004. Phase 1 can proceed under the recommended default if the user prefers.
