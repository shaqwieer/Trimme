# Phase 07 — Services, categories & packages

**Status:** [x] · **Score:** 100/100

## Goal and user-visible outcome
- Each shop creates, edits, orders, activates/deactivates and archives its **own** services and packages, setting its own prices and durations.
- Admins manage categories, moderate or override shop services with an audit trail, and assign professionals to their own shop's services.

## Prerequisites
Phase 06 complete.

## In scope
- **`ServiceCategory`** (platform-owned):
  - Localized name, icon, order, active.
  - Admin CRUD.
- **`ShopService`** (`IShopOwned`):
  - Localized name/description, category, price (decimal, SAR), duration (5-minute multiples), active, archived, display order, booking rules (e.g. online bookable).
  - Moderation state (Visible/HiddenByAdmin with reason).
  - Concurrency version.
  - Physical delete is forbidden once referenced; archive only (R-SVC-02). The check is done via a reference count, and bookings arrive in Phase 10. Deletion is allowed only for never-used drafts.
- **`ServicePackage` + `ServicePackageItem`** (D-020):
  - Composite FKs to the same shop's services.
  - Explicit price and total duration.
  - Active/archived.
- **`ProfessionalService`:**
  - Composite FK `(ShopId, ProfessionalId)` + `(ShopId, ServiceId)`, so assignment is guaranteed same-shop.
  - Admin-only (R-NEG-06).
- **Shop API:**
  - `GET/POST /shop/services`, `GET/PUT /shop/services/{id}`.
  - `POST /shop/services/{id}/activate|deactivate|archive`.
  - `PUT /shop/services/order`.
  - The same operations for `/shop/packages`.
- **Admin API:**
  - `/admin/service-categories` CRUD.
  - `GET /admin/services`: platform-wide, filter by shop/category/status.
  - `POST /admin/services/{id}/moderate` (hide/unhide + reason, audited).
  - `PUT /admin/services/{id}/override`: explicit permission + reason, audited, with a before/after summary.
  - `PUT /admin/professionals/{id}/services`: assign from the same shop only.
- **Public read:** `GET /public/shops/{slug}/services` and `/packages`. Returns only active, visible, non-archived items.
- **Web:**
  - Shop `/shop/services` list with a toggle, drag/keyboard reorder, edit and archive; `/new` and `/[id]` form (DV-A01, DV-S03). Prices and durations are formatted per locale.
  - Admin `/admin/services`, `/admin/services/categories`, `/admin/packages`, and a professional's services tab showing that shop's prices (DV-S02, DV-S04).
- **Seed:** different services, prices and durations per shop, packages, and assignments.
- **Tests:**
  - R-SVC-01..05 and R-NEG-06.
  - Cross-shop isolation on every service/package mutation.
  - The service part of E2 (create/edit price and duration).

## Explicitly out of scope
Booking snapshots (Phase 10) and public shop page UI (Phase 11).

## Checklist (100 points)
Refined at 7.1:
- The assignment table lives in `services.professional_services` (D-073), not in the professionals schema.
- English is optional for services and packages and required for categories (D-070).
- Packages with an unavailable item stay but are not published (D-072).
- Packages have no delete; archive is enough until bookings exist.
- [x] 7.1 (5) Re-validate; refine checklist. — Phase 06 checks were green (16/16 Shops + Professionals integration), and Phase 06 CI run 36316322648 passed 4/4.
- [x] 7.2 (8) Categories entity/API/admin UI. — Admin CRUD with on/off, public active list, service counts; `/admin/services/categories`.
- [x] 7.3 (15) ShopService aggregate, rules, concurrency, archive semantics + unit/integration tests. — Price and duration rules (and DB checks), final archive, delete-if-unused seam, full-set reorder, suspended shop → 404.
- [x] 7.4 (10) Packages + items (same-shop composite FKs) + tests. — Key-safe item replacement, items-only concurrency, availability-aware publishing.
- [x] 7.5 (10) ProfessionalService assignment (admin only, same shop enforced by DB) + tests. — Composite FKs, including the cross-module one by entity-type name, with a model test; a raw-SQL cross-shop insert is rejected.
- [x] 7.6 (10) Admin moderation + audited override + tests (R-SVC-04, R-TEN-08). — Hide needs a reason; the override is audited before → after, with a reason and concurrency.
- [x] 7.7 (14) Shop services & packages UI (list, reorder, form, archive guard). — `/shop/services` (tabs: services, packages), `/new`, `/[id]`, `/shop/packages/new|[id]`; keyboard reorder with an announcement and rollback. Drag-and-drop is deferred (D-071).
- [x] 7.8 (10) Admin services/categories/packages UIs + professional services tab. — `/admin/services`, `/[id]` (moderation, override), `/admin/services/categories`, `/admin/packages`, and the assigned-services card on `/admin/professionals/[id]`.
- [x] 7.9 (8) Cross-shop isolation tests for all service/package endpoints (R-SVC-03). — Every verb on another shop's service or package returns 404 and the rows are unchanged; foreign ids in package items and order are refused.
- [x] 7.10 (10) Seed, E2E E2 (service part), public read endpoints, control files, commit. — See the evidence below.

## Files/modules expected to change
`src/Modules/Services/**`, `src/Modules/Professionals/**` (assignment), `src/Modules/Administration/**`, `apps/web/src/app/[locale]/shop/services/**`, `apps/web/src/app/[locale]/admin/{services,packages}/**`.

## Data model and migration impact
Adds `services.service_categories`, `services.shop_services`, `services.service_packages`, `services.service_package_items` and `professionals.professional_services`. Migration `0005_ServicesPackages`.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- Tenant filters and composite FKs apply to all tables.
- Admin override is gated by a permission and audited.
- Reordering works by keyboard.
- Localized fields are validated in both languages; Arabic is required and English is optional with a fallback (confirm in this phase).

## Tests and verification commands
```
dotnet test
pnpm exec playwright test shop-services admin-services
```

## Acceptance criteria
- A shop changes its price or duration, and only its own record changes.
- A foreign service ID returns 404 for every verb.
- An admin override leaves an audit entry.

## Rollback / recovery
Revert the commit and reset the DB.

## Completion evidence
Session 4, 2026-09-27 (same session as Phase 06, at the user's request).

**What was built**
- **Services module.**
  - Domain: `ServiceCategory` (platform), `ShopService` (shop-owned; price, duration and currency rules; final archive; moderation), `ServicePackage` + `ServicePackageItem` (composite same-shop keys, key-safe reorder, `ExpandItems`), `ProfessionalServiceAssignment` (composite FKs, including the cross-module one by entity-type name).
  - Use cases: shop self-service (tenant first, full-set reorder, delete through `IShopServiceUsage`), admin (categories, platform-wide lists, moderation, audited override, assignment through `IProfessionalDirectory`), and public published lists inside `IPublicDataScope`.
  - Demo seed: 5 categories, 9 services with different prices per shop, 2 packages, assignments.
- **Building blocks.** `ProfessionalId` moved to the Domain building block; `HasShopScopedReference(entityTypeName, …)`; `IProfessionalDirectory` and `IShopServiceUsage` contracts.
- **Migration.** `ServicesPackages`: 5 tables, CHECK constraints on price and duration, composite FKs.
- **Web.**
  - Shop: `/shop/services` (tabs: services, packages; switch; keyboard move up/down with an announcement and rollback; archive; delete-if-unused), `/shop/services/new|[id]`, `/shop/packages/new|[id]`.
  - Admin: `/admin/services` and `/[id]` (moderation, support override with a reason), `/admin/services/categories`, `/admin/packages`, and the assigned-services card on `/admin/professionals/[id]`.
  - Helpers: `localizedName`, and `parsePrice` (Arabic-Indic digits and decimal separator).
- **Web image.** `KEEP_ALIVE_TIMEOUT=65000`: the Node default of 5 s closed idle sockets that clients were reusing, which caused intermittent E2E "socket hang up".

**Verification (actual results)**
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS — 0 warnings, 0 errors |
| Unit / architecture / integration tests | PASS — 171 / 63 / 106 (Phase 06 end: 153 / 62 / 98) |
| New integration suite `Services/CatalogTests` (8) | CRUD with own-record-only change, stale 409 and price/duration rules; in-use delete 409; every verb on another shop's service or package → 404, rows unchanged, foreign ids refused; items-only concurrent edit → 409; packages published only when every item is available; audited override (exact before → after summary) + moderation; same-shop assignment (API 400, DB FK violation, shop 403); suspended shop → 404; categories need both languages and inactive ones cannot be selected |
| Architecture `CrossModuleShopScopedReferences_ResolveToTheRealEntity` | PASS — one `Professional` entity (real CLR type); FK `(shop_id, professional_id)` → `(shop_id, id)` |
| `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Web `lint` / `typecheck` / `format:check` / `openapi:check` / `test` / `build` | PASS — 196 web tests (17 new: price parsing, name fallback, form validation, keyboard reorder + rollback; transfer-copy check covers the new catalogs) |
| Compose on the Phase 06 volume (upgrade) and `down -v` + `up --build` (clean) | PASS — migrations and 5 seeders ran |
| `pnpm e2e` on a clean stack | First clean run (`down -v` + `up --build`): **41/42 twice**, the admin services flow failing (see findings). After the fixes, a new `down -v` + `up --build` with no further changes: **PASS 42/42 twice**. The flows project also passed 5 consecutive runs in between |
| No-transfer grep gate (D-069) | PASS — no hits |
| gitleaks `dir` + `git` | PASS — no leaks |
| Visual: `/ar/shop/services`, packages tab and edit form at 390; `/ar/admin/services` at 390 and 1440; `/ar/admin/services/{id}`, `/en/admin/professionals/{id}` at 1440 | 0 px horizontal overflow at 390; RTL mirrored; Arabic-only names fall back in the English UI |

**Review follow-up (same session, before any push)**
- Row controls now name their item: "{name}: bookable" switch, and "Edit / Archive / Delete {name}" (spec §5 meaningful labels).
- The category icon picker shows translated names (`catalog.icons.*`) instead of raw keys.
- The admin E2E archives the service it creates, so the demo shop stays clean.
- D-075 (keep-alive timeout) and DV-D03 (drag-and-drop deferred) are recorded.
- Web: 196 tests, lint, typecheck (web + E2E) and format all green before the final clean-stack runs above.

**Findings during the gates**
- **Integration timeouts.** The first combined gate run had 4 integration failures (gallery, reveal, password reset, OTP), each timing out at about 31 s. The suite then passed 106/106 three times running alone. They were not reproduced and are recorded as a watch item.
- **Admin E2E, 409 conflict.** Under parallel load the admin flow submitted the support override before `router.refresh()` delivered the new version after unhiding, and got a correct 409. The test now waits for the refreshed page. A person sees the "reload and edit again" message.
- **Intermittent "socket hang up".** API requests through the web container hit the Node keep-alive race; fixed by the web image's `KEEP_ALIVE_TIMEOUT`.

**Commit and CI:** `9cb2593` + `22fadaf` (review follow-up), pushed; GitHub Actions [run 36323449600](https://github.com/shaqwieer/Trimme/actions/runs/36323449600) on `22fadaf` is green (backend incl. integration, web, secret scan, Docker stack + Playwright).

**Database and migrations:** `ServicesPackages` (`services.service_categories`, `shop_services`, `service_packages`, `service_package_items`, `professional_services`). Applied locally only: Testcontainers databases and the compose volume (upgraded, then recreated from empty).

**Smallest decisive re-verification for the next session:** `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Services"`, then `pnpm e2e` (`flows/services.spec.ts`).

## Remaining risks → next phase
- R-SVC-02 (delete guard) and R-SVC-05 (reporting expansion) complete with bookings in Phase 10: Bookings registers an `IShopServiceUsage` and snapshots name, price, currency and duration.
- Drag-and-drop reordering is deferred; keyboard move buttons cover the need (D-071).
- One unreproduced run with 4 integration timeouts (about 31 s): watch CI.
- The localized-fields policy is settled by D-070.
- Next: Phase 08 — Subscriptions & platform settings.
