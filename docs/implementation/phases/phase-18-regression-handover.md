# Phase 18 — Full regression, deployment documentation & handover

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
The platform is verified end to end and documented for deployment and operations. The final implementation report required by spec §23 is delivered.

## Prerequisites
Phase 17 complete.

## In scope
- **Full regression:**
  - All backend unit, integration and architecture tests.
  - All web unit tests.
  - **Playwright flows E1–E7** in both locales, at mobile and desktop sizes.
  - Concurrency suite ×20.
  - Migrations from an empty database.
  - Seed determinism.
  - `docker compose up --build` from a clean clone.
- **Definition-of-done audit:** walk through every item in `MASTER_PLAN.md` §11 and `TRACEABILITY.md` and record the evidence.
- **Deployment:**
  - `docs/deployment.md` covers the single-domain topology, Nginx reverse proxy example (`infra/nginx/trimme.conf`, HTTPS-ready, `/api` and `/hubs` WebSocket upgrade), environment variables, migrations run as an explicit release step, Hangfire, the file storage adapter, and scaling notes.
  - The production Docker Compose example.
  - Nothing is deployed externally.
- **`docs/backup-restore.md`:** `pg_dump`/`pg_restore` procedures, PITR notes, uploads backup, a restore drill performed locally with evidence, and key backup for encryption keys.
- **Docs completion:**
  - README, architecture, domain-model (with ERD Mermaid), permissions-matrix, availability-and-booking, whatsapp-integration and design-deviations are all final.
  - OpenAPI output committed.
- **Production readiness review:**
  - Configuration checklist: domain/DNS, TLS, SMTP (if used), maps provider, Meta WhatsApp credentials and templates, and encryption keys.
  - Backups.
  - Monitoring.
  - Admin bootstrap procedure for production (one-time, safe).
- **Final implementation report**, covering spec §23 items 1–7.

## Explicitly out of scope
Production deployment, real credentials, and paid resources.

## Checklist (100 points)
- [ ] 18.1 (5) Re-validate; refine checklist.
- [ ] 18.2 (25) Full regression incl. E1–E7 in ar/en and mobile/desktop; fix regressions.
- [ ] 18.3 (10) Clean-clone `docker compose up --build` + migrations + seed evidence.
- [ ] 18.4 (12) Deployment doc + Nginx example + production compose example.
- [ ] 18.5 (10) Backup/restore doc + local restore drill evidence.
- [ ] 18.6 (12) Docs completion + OpenAPI output.
- [ ] 18.7 (10) DoD + traceability audit with evidence; all requirement rows `[x]` or `[-]` with reasons.
- [ ] 18.8 (8) Production readiness checklist.
- [ ] 18.9 (8) Final implementation report; control files; final commit.

## Files/modules expected to change
`docs/**`, `infra/**`, `README.md`, plus fixes found during regression.

## Data model and migration impact
None expected.

## API contracts and UI routes
None expected.

## Security, tenancy, privacy, RTL, a11y, responsive
Re-verified through regression.

## Tests and verification commands
Every suite in every phase, plus the clean-clone compose run.

## Acceptance criteria
- Spec §23 definition of done is fully met with recorded evidence.
- Deferred items are clearly listed and kept separate from completed scope.

## Rollback / recovery
N/A (documentation and fixes).

## Completion evidence
_(fill)_

## Remaining risks → next phase
None. This is the handover.
