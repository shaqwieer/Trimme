# Phase 05 — Tenancy, privacy & audit core

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
Every later shop-owned feature inherits proven isolation.
- A platform admin can create a shop and invite its owner.
- The shop owner signs in and can see only their own shop.
- Cross-shop and IDOR attempts fail.
- Sensitive phone data is encrypted, masked and redacted.
- Admin actions are audited.

## Prerequisites
Phase 04 complete.

## In scope
- **Shops module (minimal):** `Shop` (id, slug, localized name, status Draft/Active/Suspended, time zone, `RequireManualConfirmation` placeholder per D-006) and `ShopUser` (user ↔ shop, ShopOwner/ShopStaff).
- **Tenancy:**
  - `ICurrentTenant` resolved from the claims of shop users. It never reads a shopId from the route, body or query.
  - `IShopOwned` marker interface.
  - An EF model convention that applies a global query filter to every `IShopOwned` entity.
  - A SaveChanges interceptor that stamps and validates `ShopId`, and rejects changing `ShopId` on update.
  - A composite-key pattern (`(ShopId, Id)` alternate keys + composite FKs), demonstrated on `ShopUser` and a test-only owned entity.
- **Admin bypass:** an explicit `IAdminDataScope` (a separate DbContext factory or a scoped flag) that is usable only from the Administration module's application layer. An architecture test enforces that `IgnoreQueryFilters` appears only there (R-TEN-05).
- **Sensitive data (D-026):**
  - A `PhoneNumber` value object with E.164 normalisation (libphonenumber) and masking.
  - Encrypted storage via a value converter plus a deterministic HMAC lookup column.
  - Keys come from configuration/Data Protection, and a dev key is generated locally.
  - A redaction enricher for phone numbers, tokens and codes (R-FND-06, R-TEN-07).
- **Audit:** an `AuditEntry` (actor, actor type, action, entity type/id, shopId, before/after summary without PII, reason, correlation id, timestamp) and an `IAuditLog` service. Admin commands must write entries.
- **Admin shop endpoints (initial):**
  - `POST /api/v1/admin/shops`, `GET /api/v1/admin/shops`, `GET /api/v1/admin/shops/{id}`.
  - `POST /api/v1/admin/shops/{id}/users/invite`.
  - `POST /api/v1/admin/shops/{id}/activate|suspend`.
- **Shop endpoint:** `GET /api/v1/shop/me`.
- **Isolation test harness:** a reusable fixture that creates two shops with owners and staff. Parameterised `CrossShop_*` tests cover read, update, delete and enumeration (returns 404 with no existence leak).
- **Phone-absence test:** a DTO contract test framework (`ShopFacingContracts_DoNotContainCustomerPhone`). It uses reflection over types in `*.Contracts.Shop` namespaces plus JSON snapshots, and is extended by each later phase.
- **Web:**
  - Admin shops list/create (minimal; the full UI is in Phase 6) and an invite flow.
  - Shop dashboard shell showing `/shop/me`.
  - A 404/permission-denied page for foreign IDs.
- Seed: two Riyadh shops with an owner and staff each (deterministic).

## Explicitly out of scope
Shop profile, location, professionals and services (Phase 6/7).

## Checklist (100 points)
Refined at 5.1 (D-059): shop membership lives on `identity.users.shop_id` instead of a `ShopUser` table, and the composite-key pattern is demonstrated on test-only shop-owned entities built through the real conventions. 5.6 was partly done in Phase 04 (encryption, HMAC lookup, masking); here it adds the libphonenumber value object and the hash-stability proof.
- [x] 5.1 (5) Re-validate; refine checklist. — Phase 04 decisive checks (integration suite, fresh-image compose + E2E 36/36) green at the start of the phase.
- [x] 5.2 (10) Shop + membership, migration, seed. — `Shop` (Draft/Active/Suspended), `users.shop_id` + `invitations.shop_id` with FKs, migration `ShopsTenancyAudit`, deterministic demo seed (2 shops × owner + staff).
- [x] 5.3 (15) Tenant resolution, query-filter convention, stamping, immutable ShopId + tests (R-TEN-01/02/03).
- [x] 5.4 (8) Composite FK pattern + DB-level cross-shop rejection test (R-TEN-04).
- [x] 5.5 (8) Admin (and system) bypass scope + architecture tests (R-TEN-05).
- [x] 5.6 (14) PhoneNumber VO, encryption + HMAC lookup, masking, redaction + tests (R-TEN-07, R-FND-06).
- [x] 5.7 (10) Audit entity/service + tests (R-TEN-08).
- [x] 5.8 (10) Admin shop endpoints + invite + activate/suspend + tests (R-SHP-01 partial).
- [x] 5.9 (12) Isolation harness + IDOR suite + phone-absence contract framework (R-TEN-06, R-NEG-04).
- [x] 5.10 (8) Minimal admin/shop web screens + E2E "shop cannot open another shop's URL" + control files + commit.

## Files/modules expected to change
`src/Modules/Shops/**`, `src/Modules/Administration/**`, `src/BuildingBlocks/**` (tenancy, audit, crypto), `tests/**`, `apps/web/src/app/[locale]/admin/shops/**`, `apps/web/src/app/[locale]/shop/**`.

## Data model and migration impact
- Schemas `shops` and `audit`.
- Migration `0003_ShopsTenancyAudit`.

## API contracts and UI routes
As listed. Routes: `/admin/shops`, `/admin/shops/new`, `/shop`.

## Security, tenancy, privacy, RTL, a11y, responsive
This is the core of the spec §7 technical enforcement. Foreign IDs return 404, not 403, so resources cannot be enumerated.

## Tests and verification commands
```
dotnet test --filter "Category=Tenancy|Category=Privacy"
dotnet test
pnpm exec playwright test tenancy
```

## Acceptance criteria
- All R-TEN tests pass.
- The architecture test fails when a new `IShopOwned` entity lacks a filter; verify by temporarily adding one.

## Rollback / recovery
Revert the commit, then reset the local DB.

## Completion evidence
Session 3, 2026-09-26 (same session as Phase 04, at the user's request).

**What was built**
- BuildingBlocks: `ShopId`, `IShopOwned`, `ITenantRoot`, `ITenantMember`, `ICurrentTenant` (claims + per-request shop status), tenant filter / stamping / immutability / tenant-root FK conventions in `TrimmeDbContext`, composite-key helpers, model-cache key per contributor set, `IAdminDataScope`, `ISystemDataScope`, `IShopDirectory`, `IAuditLog`, `PageRequest`/`PagedResponse`, `PhoneNumber` value object (libphonenumber 9.0.40), `DemoData`.
- Shops module: `Shop` aggregate; admin endpoints `POST/GET /admin/shops`, `GET /admin/shops/{id}`, `POST /admin/shops/{id}/activate|suspend`; `GET /shop/me`; demo shops seeder.
- Identity: shop membership (`users.shop_id`, `shop_id` claim), shop-user invitations `POST /admin/shops/{id}/users/invitations`, per-request shop operability, demo shop users seeder.
- Administration: `AuditEntry` + `AuditLog`.
- Web: `/admin/shops`, `/admin/shops/new`, `/admin/shops/[id]` (status actions, invitation), `/shop` from `/shop/me` (draft/suspended states), `AdminFrame` guard.

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings |
| Unit / architecture / integration tests | PASS — 124 / 61 / 81 |
| PostgreSQL spike (`TenantIsolationTests`) | Filter SQL contains `shop_id`; tenant sees own rows, no tenant sees none, bypass sees all and closes on dispose |
| Probes built into the architecture tests | The IL scan finds a probe `IgnoreQueryFilters` call; the scope rules flag a probe type in the wrong namespace; the ShopId rule flags a probe entity without a tenant interface; the filter rule sees the probe shop-owned entity — all asserted on every run |
| Probe: removed `Produces<ShopProfileResponse>` from `/shop/me` | `ShopFacingContracts_DoNotContainCustomerPhone` FAILED ("declares no response type…"); reverted |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes after `ShopsTenancyAudit` |
| `pnpm lint` / `typecheck` (web + E2E) / `format:check` (web + E2E) / `test` / `openapi:check` / `build` | PASS — 170 web tests |
| gitleaks `dir` + `git` | PASS — no leaks |
| `docker compose down -v`, then compose smoke (`TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325`) | PASS — seed created the SuperAdmin, 2 demo shops, 4 demo shop users |
| `pnpm e2e` ×2 | PASS — 38/38 both runs (incl. the tenancy flow and `shop_user_cannot_open_another_shop`, axe 0 serious on admin shops list/detail and `/shop`) |
| Visual: `/ar/admin/shops` at 1440, `/en/admin/shops` at 390 | RTL table with the sidebar on the inline start; cards below 768px |

**Commit:** `6a65a9d` (plus a docs follow-up).

**Database and migrations:** `ShopsTenancyAudit` (schemas `shops`, `administration`; `users.shop_id`, `invitations.shop_id` with FKs). Applied locally only.

**Smallest decisive re-verification for the next session:** `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Tenancy"` and `dotnet test --project tests/Trimme.ArchitectureTests -c Release`, then `pnpm e2e` against the stack.

## Remaining risks → next phase
- Encryption key management for production is documented in Phase 17.
- No production `IShopOwned` entity exists yet; Phase 06 (professionals) is the first real consumer of the filter, stamping, composite keys and the admin scope — add a `CrossShop_*` suite per endpoint with `ShopTestData.CreateTwoShopsAsync`.
- Public (customer) reads of shop-owned data will need an explicit per-shop read scope (Phase 11): the filter is deny-all for non-shop callers by design.
- Next: Phase 06 — Shops, locations & professionals.
