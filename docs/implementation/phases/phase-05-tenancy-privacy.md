# Phase 05 — Tenancy, privacy & audit core

**Status:** [ ] · **Score:** 0/100

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
- [ ] 5.1 (5) Re-validate; refine checklist.
- [ ] 5.2 (10) Shop + ShopUser entities, migration, seed.
- [ ] 5.3 (15) Tenant resolution, query-filter convention, stamping interceptor, immutable ShopId + tests (R-TEN-01/02/03).
- [ ] 5.4 (8) Composite FK pattern + DB-level cross-shop rejection test (R-TEN-04).
- [ ] 5.5 (8) Admin bypass scope + architecture test (R-TEN-05).
- [ ] 5.6 (14) PhoneNumber VO, encryption + HMAC lookup, masking, redaction + tests (R-TEN-07, R-FND-06).
- [ ] 5.7 (10) Audit entity/service + tests (R-TEN-08).
- [ ] 5.8 (10) Admin shop endpoints + invite + activate/suspend + tests (R-SHP-01 partial).
- [ ] 5.9 (12) Isolation harness + IDOR suite + phone-absence contract framework (R-TEN-06, R-NEG-04).
- [ ] 5.10 (8) Minimal admin/shop web screens + E2E "shop cannot open another shop's URL" + control files + commit.

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
_(fill)_

## Remaining risks → next phase
- Encryption key management for production is documented in Phase 17.
- Next: Phase 06 — Shops, locations & professionals.
