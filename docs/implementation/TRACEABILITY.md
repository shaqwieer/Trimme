# TRIMME — Requirements Traceability

Each requirement has an ID, its spec source, design reference, owning phase, the **named** verification that proves it, and a status (`[ ]` `[~]` `[x]` `[!]` `[-]`).
Test names are the intended names; the phase that implements a requirement must create a test with that name (or update this table if renamed).
Test layers: **U** backend unit · **I** backend integration (Testcontainers PostGIS) · **A** architecture test · **W** web unit/component (Vitest) · **E** Playwright E2E · **C** CI/static check · **M** manual/visual check with recorded evidence.

## 1. Negative requirements (things that must NOT exist)

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-NEG-01 | No barber transfer: `Professional.ShopId` immutable; no update DTO carries `shopId`; no transfer route/permission/UI | §7, §14, §23 | 6 | U `Professional_ShopId_HasNoPublicSetter`; A `NoProfessionalUpdateContract_ContainsShopId`; I `OpenApi_HasNoTransferOperation`; I `PermissionCatalogue_HasNoTransferPermission`; E `admin_professional_page_has_no_transfer_action`; C `grep` gate for transfer terms in `apps/` and `src/` | [ ] |
| R-NEG-02 | No payment/checkout UI or charge flow | §2, §12, §23 | 12, 18 | E `booking_flow_has_no_payment_step`; C OpenAPI has no payment/charge operations; M review | [ ] |
| R-NEG-03 | No customer export for shops | §7, §13 | 13 | I `ShopApi_HasNoExportEndpoints`; E `shop_dashboard_has_no_export_action` | [ ] |
| R-NEG-04 | Customer phone never in shop DTOs/SignalR/HTML/logs | §7, §13 | 5, 13, 15 | I `ShopFacingContracts_DoNotContainCustomerPhone` (reflection over all shop DTO types + JSON snapshot of every shop endpoint); I `ShopHub_Messages_DoNotContainPhone`; I `Logs_RedactPhoneNumbers`; E `shop_pages_payload_has_no_customer_phone` (network capture) | [ ] |
| R-NEG-05 | No shared professional across shops | §7 | 6 | I `Professional_BelongsToExactlyOneShop` (DB constraint) | [ ] |
| R-NEG-06 | Shops cannot create/delete/move professionals or assign services | §7 | 6, 7 | I `Shop_CannotCreateProfessional`, `Shop_CannotAssignProfessionalService` (403) | [ ] |
| R-NEG-07 | Tokens never in `localStorage`/`sessionStorage` | §9 | 4 | C ESLint bans `localStorage`/`sessionStorage` (probe-verified) ✔; E `no_tokens_in_web_storage` baseline ✔; full auth check Phase 04 | [~] |
| R-NEG-08 | No hardcoded plan names/prices/durations/limits | §7, §15 | 8 | C grep gate; I plans served from DB only | [ ] |
| R-NEG-09 | No hardcoded final WhatsApp message text in jobs/handlers | §16 | 15 | A `Notifications_Handlers_DoNotContainMessageLiterals`; I dispatch renders from active template version | [ ] |
| R-NEG-10 | Private dashboards not indexed | §6 | 11, 17 | I/E `private_routes_emit_noindex`; robots.txt test | [ ] |

## 2. Foundation, quality and infrastructure

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-FND-01 | Monorepo layout + module boundaries/dependency direction | §4 | 1 | A `ArchitectureRules` (building-block direction, module Domain/Application namespace rules, cross-module refs via compiled metadata **and** `.csproj`, internal handlers) — proven non-vacuous (phase-01 evidence) | [x] |
| R-FND-02 | Nullable, warnings-as-errors, analyzers; strict TS, ESLint, formatting | §21 | 1, 2 | C `dotnet build -c Release` (TreatWarningsAsErrors, analyzers, code style) ✔ Phase 01; `pnpm lint` (0 warnings) / `pnpm typecheck` (strict) / `pnpm format:check` ✔ Phase 02 | [x] |
| R-FND-03 | `/api/v1` versioning, RFC 7807 problem details with stable error codes | §18 | 1 | I `UnknownRoute_Returns_ProblemDetails`, `MethodNotAllowed_Returns_ProblemDetails`, `ValidationError_HasStableCode_AndFieldErrors`, `UnexpectedError_DoesNotLeakExceptionDetails`; U `Status_codes_map_to_stable_error_codes`, `Domain_errors_become_problem_responses_with_their_code` | [x] |
| R-FND-04 | Health endpoints liveness/readiness | §3 | 1 | I `Health_Live_Returns200`, `Health_Ready_ChecksDatabase`, `Health_Ready_Returns503_WhenDatabaseIsUnreachable`; container `HEALTHCHECK` healthy | [x] |
| R-FND-05 | OpenAPI document + generated TS client, drift check | §18 | 1, 2 | I `OpenApi_document_matches_committed_contract` ✔ Phase 01; C `pnpm openapi:check` (generated `schema.d.ts` vs `v1.json`) ✔ Phase 02 | [x] |
| R-FND-06 | Structured logging, correlation IDs, PII redaction | §3, §21 | 1, 5 | I `Response_HasCorrelationId_GeneratedWhenMissing`, `Response_EchoesOnlyWellFormedCorrelationIds`; U `SensitiveDataRedactorTests` (phones incl. Arabic-Indic, JWT/Bearer, e-mail, sensitive names, enricher) | [x] |
| R-FND-07 | EF migrations from empty DB (PostGIS, btree_gist) | §19 | 1 → every data phase | I `Migrations_ApplyToEmptyDatabase`, `Migrations_AreIdempotent`, `Model_HasNoPendingChanges`; C `dotnet ef migrations has-pending-model-changes` — re-run by every data phase | [~] |
| R-FND-08 | Docker Compose (web, api, postgis) + Dockerfiles; `docker compose up --build` | §3, §21 | 1, 2 | C `infra/scripts/compose-smoke.sh`: postgis → migrate → api → web, web `/ar` 200 + web-origin `/api` proxy; containers healthy | [x] |
| R-FND-09 | CI: lint, typecheck, test, build, migration validation | §3 | 1, 2 | C `.github/workflows/ci.yml` jobs backend, web, secret-scan, stack (compose + Playwright) — **green on GitHub Actions run 36134142951** | [x] |
| R-FND-10 | `.env.example` files, no secrets committed | §3, §23 | 1 | C gitleaks v8.30.1 (`.gitleaks.toml`) working tree + history: no leaks; `.env.example`, `infra/.env.example` | [x] |
| R-FND-11 | Deterministic dev-only seed command (extended per phase) | §20 | 1 → 16 | U `Seed_IsDevelopmentOnly_and_requires_explicit_opt_in`, `Seed_is_allowed_with_development_flag_and_argument` ✔ Phase 01; determinism test when the first seeder lands (Phase 05) | [~] |
| R-FND-12 | Cancellation tokens end-to-end | §18 | 1+ | A `Endpoints_AcceptCancellationToken`, `Feature_endpoints_are_versioned_under_api_v1` (re-run every phase) | [x] |
| R-FND-13 | Pagination with safe max page size, filtering, sorting | §18, §21 | 5+ | U `PageRequest_ClampsPageSize`; A list endpoints return paged envelopes | [ ] |
| R-FND-14 | Security headers, request size limits, upload validation | §9 | 1, 17 | I `SecurityHeaders_Present`, `RequestBody_TooLarge_Returns413Problem` ✔ Phase 01; upload validation Phase 06/17 | [~] |
| R-FND-15 | Strict CORS | §9 | 1 | I `Cors_AllowsConfiguredOrigin_WithCredentials`, `Cors_RejectsUnknownOrigin`; wildcard origins rejected at startup | [x] |

## 3. Web, design system, i18n, SEO, a11y

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-WEB-01 | Design tokens extracted to CSS variables; Tailwind uses them (no default palette leakage) | §5 | 2 | W `tokens.test.ts` (identity hexes, Tailwind defaults reset, breakpoints); C ESLint `no-restricted-syntax` raw-hex guard (probe-verified) | [x] |
| R-WEB-02 | Reusable component library matching ds-components | §5 | 3 | W component tests; M gallery screenshots vs design at 390/768/1440 — Phase 03 | [ ] |
| R-WEB-03 | Logo used unmodified via `next/image` (nav, auth, public, metadata) | Op. rule 6 | 2 | W `Logo_renders_with_intrinsic_ratio`; lossless transparent-margin crop verified (D-042); used in public, customer and dashboard shells | [x] |
| R-WEB-04 | `/ar` RTL default + `/en` LTR, `dir`/`lang` correct | §6 | 2 | E `locale_ar_is_rtl`, `locale_en_is_ltr`, root redirects (fr/ar browser → /ar, en → /en), language switch | [x] |
| R-WEB-05 | No hardcoded UI strings; ar/en key parity | §6 | 2 → all | W `messages_have_key_parity` (keys, empties, ICU placeholders; failure proven); C `react/jsx-no-literals` (probe-verified) — continues every phase | [~] |
| R-WEB-06 | Locale-aware date/time/number/currency (SAR, Asia/Riyadh), bidi-safe phones/times/prices | §5, §6 | 2 | W `formatters_ar_en` (Gregorian Arabic, Asia/Riyadh, D-040 numerals, SAR, distance, rating, phone); E `bdi` LTR isolation | [x] |
| R-WEB-07 | Responsive at ~390/768/1440; dashboards sidebar + mobile drawer; tables → cards on small screens | §5 | 3, 13, 14 | E shells: RTL/LTR sidebar side, drawer <1200, bottom bar <1200, `captureViewports` 390/768/1440 ✔ Phase 02; tables→cards Phase 03/13/14 | [~] |
| R-WEB-08 | Loading, empty, error, permission-denied, expired-session states; optimistic rollback | §5 | 3 → all | W state components Phase 03; E `expired_session_redirects_to_sign_in` Phase 04 | [ ] |
| R-WEB-09 | WCAG AA contrast, keyboard nav, visible focus, labels, accessible dialogs, 44px targets | §5 | 3, 17 | W `tokens.test.ts` AA contrast; W drawer focus trap/Escape/return focus; E axe 0 serious/critical on /ar, /en and 3 shells ✔ Phase 02 — continues every phase | [~] |
| R-WEB-10 | Localized metadata, canonical, hreflang, OG, robots.txt, sitemap | §6 | 11, 17 | I/E `public_pages_have_hreflang_and_canonical`; `sitemap_lists_shops` | [ ] |
| R-WEB-11 | JSON-LD (Organization, LocalBusiness, Breadcrumb, AggregateRating only when real data) | §6 | 11 | W/E `jsonld_aggregateRating_absent_without_reviews` | [ ] |
| R-WEB-12 | RSC for public/read-heavy routes; client only where needed | §3 | 11 | M review + bundle report | [ ] |
| R-WEB-13 | Permission-aware navigation | §19 | 4, 13, 14 | W `nav_hides_items_without_permission`, DashboardShell permission test ✔ (config-level); wired to real permissions in Phase 04 | [~] |
| R-WEB-14 | Forms: RHF + Zod; API error mapping to fields | §3, §19 | 3+ | W `problemDetails_maps_to_field_errors` | [ ] |

## 4. Identity, sessions, authorization

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-AUTH-01 | Customer self-registration + sign-in per design flow with verified mobile (D-005) | §9, §12 | 4 | I `Customer_SignUp_VerifiesMobile`; E flow 1 (auth part) | [ ] |
| R-AUTH-02 | Shop accounts created/invited by admin only | §9 | 4, 5 | I `ShopAccount_CannotSelfRegister`; I `Admin_InvitesShopUser` | [ ] |
| R-AUTH-03 | Admin seeded only in dev via env vars / one-time bootstrap | §9 | 4 | I `AdminBootstrap_RequiresDevelopmentAndEnv` | [ ] |
| R-AUTH-04 | Short access + rotating refresh in Secure/HttpOnly/SameSite cookies; reuse detection | §9 | 4 | I `Refresh_Rotates`, `RefreshReuse_RevokesFamily`; I `Cookies_AreSecureHttpOnly` | [ ] |
| R-AUTH-05 | Sign-out, revoke-all-sessions, session list | §9 | 4 | I `RevokeAll_InvalidatesOtherSessions`; E account security page | [ ] |
| R-AUTH-06 | Password reset (staff) / verification per D-005 | §9 | 4 | I `Staff_PasswordReset_Flow` | [ ] |
| R-AUTH-07 | Rate limiting + lockout (auth, OTP) | §9, §18 | 4 | I `Otp_RateLimited`, `Lockout_AfterFailedAttempts` | [ ] |
| R-AUTH-08 | CSRF protection for cookie-authenticated unsafe requests | §9 | 4 | I `UnsafeRequest_WithoutCsrf_Rejected` | [ ] |
| R-AUTH-09 | Roles + data-driven permission catalogue; authorization enforced by API | §7 | 4 | I `Endpoint_WithoutPermission_Returns403` matrix test over all endpoints | [ ] |
| R-AUTH-10 | SuperAdmin-only plan/pricing & overrides | §7, §15 | 8 | I `NonSuperAdmin_CannotManagePlans`, `Shop_CannotManagePlans` | [ ] |

## 5. Tenancy and privacy

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-TEN-01 | Tenant resolved from claims, never client-supplied shop ID | §7 | 5 | I `ShopEndpoints_IgnoreClientSuppliedShopId` | [ ] |
| R-TEN-02 | Global query filters on every shop-owned entity | §7 | 5 → all | A `AllShopOwnedEntities_HaveTenantFilter` | [ ] |
| R-TEN-03 | Server-side tenant stamping on writes | §7 | 5 | I `Create_StampsTenantFromClaims` | [ ] |
| R-TEN-04 | Composite FKs prevent cross-shop references | §7 | 5 → all | I `CrossShopReference_RejectedByDatabase` | [ ] |
| R-TEN-05 | Explicit isolated admin bypass | §7 | 5 | A `IgnoreQueryFilters_OnlyInAdminScope` | [ ] |
| R-TEN-06 | Cross-shop read/update/delete/booking/SignalR/enumeration (IDOR) tests | §7, §19 | 5 → 13 | I `CrossShop_*` suite (returns 404, never 403-with-leak); I `ShopHub_DoesNotReceiveOtherShopEvents` | [ ] |
| R-TEN-07 | Phone numbers normalized E.164, encrypted, masked, redacted | §7, §8 | 5, 6 | U `PhoneNumber_NormalizesToE164`, `PhoneNumber_Masks`; I `Phone_StoredEncrypted` | [ ] |
| R-TEN-08 | Audit log for admin/sensitive actions | §7, §14 | 5 → all | I `AdminServiceOverride_IsAudited`, `PhoneReveal_IsAudited` | [ ] |

## 6. Domain: shops, professionals, services, subscriptions

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-SHP-01 | Admin shop CRUD, activate/suspend, account setup | §14 | 5, 6 | I `Admin_CreatesShop_WithOwnerInvite`; E flow 3 | [ ] |
| R-SHP-02 | Location via designed pin picker; stored as PostGIS geography with spatial index | §8 | 6 | I `ShopLocation_StoredAsGeography`; E flow 3 (pin drag) | [ ] |
| R-SHP-03 | Shop public-profile edit limited to admin-permitted fields | §13 | 6 | I `Shop_CannotEditLockedField` | [ ] |
| R-PRO-01 | Admin professional CRUD/disable, one shop, WhatsApp number masked, notifications toggle | §7, §14 | 6 | I `Admin_CreatesProfessional_WithMaskedWhatsApp`; E flow 3 | [ ] |
| R-PRO-02 | Professional number absent from public/customer/other-shop DTOs | §8 | 6 | I `PublicProfessionalDto_HasNoPhone` | [ ] |
| R-SVC-01 | Shop creates/edits/activates/deactivates/archives/orders own services (ar/en, price, duration) | §10 | 7 | I `Shop_ServiceCrud_Flow`; E flow 2 | [ ] |
| R-SVC-02 | Referenced services never physically deleted; archive only | §7 | 7 | I `Service_WithBookings_CannotBeDeleted` | [ ] |
| R-SVC-03 | Cross-shop service isolation on reads and every mutation | §19 | 7 | I `CrossShop_Service_*` | [ ] |
| R-SVC-04 | Admin categories, moderation, audited override; professional-service assignment | §14 | 7 | I `Admin_OverrideService_Audited`; I `Admin_AssignsProfessionalService_SameShopOnly` | [ ] |
| R-SVC-05 | Packages with items, price, duration; expand for reporting | §10 | 7 | U `Package_Duration_And_Items`; I reporting expansion | [ ] |
| R-SUB-01 | SuperAdmin plan catalogue (localized, price, currency, interval, features, limits, trial/grace, published, order) | §7, §15 | 8 | I `SuperAdmin_PlanCrud`; E flow 5 | [ ] |
| R-SUB-02 | Price versioning; no retroactive change | §15 | 8 | U `PlanPrice_Versioning`; I `ExistingSubscription_KeepsPriceSnapshot`; E flow 5 | [ ] |
| R-SUB-03 | Assign, renew, override (audited), history | §15 | 8 | I `Subscription_Assign_Renew_Override_History` | [ ] |
| R-SUB-04 | Statuses Active/ExpiringSoon/Expired/Suspended with configurable threshold | §15 | 8, 15 | U `SubscriptionStatus_Calculation` | [ ] |
| R-SUB-05 | Warnings to admin and shop; explicit enforcement; future bookings untouched | §15 | 8, 13, 15 | I `Expiry_DoesNotAlterFutureBookings`; I enforcement setting tests | [ ] |

## 7. Availability and booking

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-AVL-01 | Availability combines hours, closures, pause, pro hours, breaks, time off, duration, bookings, lead time, horizon, slot step (5-min capable) | §11 | 9 | U `Availability_*` suite (incl. midnight/DST-free tz boundaries) | [ ] |
| R-AVL-02 | Only bookable slots returned | §11 | 9 | U/I `Availability_ExcludesPast_Conflicts_Breaks_TimeOff` | [ ] |
| R-AVL-03 | Working hours, breaks, time off, pause/resume managed by shop | §13 | 9 | I `Shop_ManagesSchedule`; E flow 2 | [ ] |
| R-BKG-01 | Booking aggregate with service/price/duration snapshot | §10 | 10 | I `Booking_KeepsSnapshot_AfterServiceEdit` | [ ] |
| R-BKG-02 | State machine; invalid transitions rejected; history with actor/timestamp | §11 | 10 | U `BookingStateMachine_*`; I `InvalidTransition_Returns409` | [ ] |
| R-BKG-03 | Transactional recheck + PostgreSQL exclusion constraint | §11 | 10 | I `ExclusionConstraint_RejectsOverlap` | [ ] |
| R-BKG-04 | Concurrency: exactly one winner | §11, §19 | 10, 18 | I `ConcurrentBookings_ExactlyOneSucceeds`; E flow 6 | [ ] |
| R-BKG-05 | Idempotent create/reschedule (idempotency keys) | §11, §18 | 10 | I `CreateBooking_SameKey_ReturnsSameResult` | [ ] |
| R-BKG-06 | Typed conflict response | §11 | 10, 12 | I `SlotTaken_Returns409_WithCode`; E conflict UI | [ ] |
| R-BKG-07 | Walk-ins use the same collision checks | §11 | 10, 13 | I `WalkIn_CannotOverlap`; E flow 2 | [ ] |
| R-BKG-08 | Outbox message written in the booking transaction | §16 | 10 | I `BookingCreated_WritesOutbox_InSameTransaction`, `Rollback_WritesNoOutbox` | [ ] |
| R-BKG-09 | Customer cancel/reschedule per policy | §12 | 10, 12 | I policy tests; E flow 1 | [ ] |
| R-BKG-10 | Payment seam: neutral booking fields + documented abstraction | §2 | 10 | M doc `docs/availability-and-booking.md` §Payment seam | [ ] |

## 8. Customer experience

| ID | Requirement | Spec | Design | Phase | Verification | Status |
|---|---|---|---|---|---|---|
| R-CUS-01 | Landing page | §12 | c-landing | 11 | E `landing_renders_ar_en` | [ ] |
| R-CUS-02 | Location permission / manual location | §12 | c-auth (location) | 11 | E `manual_location_sets_search_origin` | [ ] |
| R-CUS-03 | Nearby shops + search; list/map; distance sort | §12 | c-home, c-map | 11 | I `NearbyShops_SortedByDistance` (spatial); E | [ ] |
| R-CUS-04 | Filters: service, open-now, price range, verified, bookable-today, rating (sort) | §12 | c-map | 11 | I filter tests | [ ] |
| R-CUS-05 | Shop page: gallery, description, rating, address, map, distance, open status, services, packages, professionals, reviews | §12 | c-shop | 11 | E `shop_page_sections` | [ ] |
| R-CUS-06 | Professional profile within shop | §12 | c-shop | 11 | E | [ ] |
| R-CUS-07 | Booking wizard service/package → professional → date → slot → review → confirmation | §12 | c-booking | 12 | W `bookingWizard_step_state`; E flow 1 | [ ] |
| R-CUS-08 | Upcoming/previous bookings, details, cancel, reschedule, .ics | §12 | c-appointments, c-rate | 12 | E flow 1 | [ ] |
| R-CUS-09 | Post-completion review once; foreign/incomplete rejected | §12, §17 | c-rate | 12 | I `Review_OnlyOnce_OnlyCompleted_OnlyOwner`; E flow 7 | [ ] |
| R-CUS-10 | Favorites (shops, professionals) — present in design | §12 | c-profile | 12 | I/E favorites toggle | [ ] |
| R-CUS-11 | Notifications center | §12 | c-profile | 15 | E mark-read | [ ] |
| R-CUS-12 | Profile, language, security/session settings | §12 | c-profile | 4, 12 | E | [ ] |
| R-CUS-13 | QR destination with attribution (shop and professional) | §12, §17 | c-qr | 16 | I `QrVisit_Recorded`, `Booking_AttributedToQr`; E | [ ] |

## 9. Shop dashboard

| ID | Requirement | Spec | Design | Phase | Verification | Status |
|---|---|---|---|---|---|---|
| R-SD-01 | Operational overview + today's appointments | §13 | s-overview | 13 | I KPI query tests; E flow 2 | [ ] |
| R-SD-02 | Day/week calendar | §13 | s-calendar | 13 | W calendar positioning; E | [ ] |
| R-SD-03 | Appointment list/details, valid status changes | §13 | s-appointments | 13 | E flow 2 | [ ] |
| R-SD-04 | Walk-in creation | §13 | s-walkin | 13 | E flow 2 | [ ] |
| R-SD-05 | Hours, professional schedule visibility, breaks, vacations, time off, pause/resume | §13 | s-hours | 9 | E | [ ] |
| R-SD-06 | Own services CRUD + archive | §13 | s-services (corrected) | 7 | E flow 2 | [ ] |
| R-SD-07 | Subscription status + expiry visibility | §13 | s-services | 8 | E | [ ] |
| R-SD-08 | Notifications | §13 | (absent) | 15 | E | [ ] |
| R-SD-09 | Profile editing within admin policy; location edit via pin | §13 | s-settings | 6 | E | [ ] |
| R-SD-10 | Scoped SignalR live updates | §17 | — | 13 | I `ShopHub_ScopedToTenant` | [ ] |

## 10. Admin dashboard

| ID | Requirement | Spec | Design | Phase | Verification | Status |
|---|---|---|---|---|---|---|
| R-AD-01 | Overview KPIs (today's appointments, completion rate, cancellations, no-shows, active shops, expiring subs, active pros, new customers, popular services, top shops) | §14, §17 | a-overview | 14 | I KPI query tests | [ ] |
| R-AD-02 | Shops management | §14 | a-shops | 5, 6 | E flow 3 | [ ] |
| R-AD-03 | Professionals management (no transfer) | §14 | a-pros | 6 | E flow 3 | [ ] |
| R-AD-04 | Services & packages platform-wide, categories, moderation, override, assignment | §14 | a-services | 7 | E | [ ] |
| R-AD-05 | Bookings global search/filters/details/history/intervention | §14 | a-appointments | 14 | I/E | [ ] |
| R-AD-06 | Customers list/profile with protected contact | §14 | a-appointments | 14 | I `PhoneReveal_RequiresPermission_AndAudits` | [ ] |
| R-AD-07 | Reviews moderation | §14 | a-reviews | 14 | I/E | [ ] |
| R-AD-08 | Subscription plans (SuperAdmin), assignment, renewal, overrides, history | §14 | a-subs | 8 | E flow 5 | [ ] |
| R-AD-09 | QR generation + analytics | §14 | a-reviews | 16 | E | [ ] |
| R-AD-10 | WhatsApp templates, dispatch log, retries, failures | §14 | a-reviews | 15 | E flow 4 | [ ] |
| R-AD-11 | Roles & permissions management | §14 | a-roles | 14 | I/E | [ ] |
| R-AD-12 | Audit activity | §14 | a-roles | 14 | E | [ ] |
| R-AD-13 | Platform settings (booking policy, locale, currency, tz, reminder offset, map defaults, thresholds, enforcement) | §14 | (absent) | 8, 14 | I settings tests | [ ] |

## 11. Notifications, WhatsApp, jobs, QR, analytics

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-NTF-01 | Provider abstraction + dev fake + Meta Cloud API adapter (no real creds) | §16 | 15 | U adapter contract tests; I fake records dispatch | [ ] |
| R-NTF-02 | Versioned ar/en templates per audience/event; preview, validate, activate, history; placeholder whitelist | §16 | 15 | U `TemplatePlaceholders_Validated`; U `CustomerAndProfessionalTemplates_RenderSeparately`; E flow 4 | [ ] |
| R-NTF-03 | Customer confirmation, update/cancel, 30-min reminder | §16 | 15 | I `CustomerDispatches_ForLifecycle` | [ ] |
| R-NTF-04 | Professional new/confirmed, update/cancel, 30-min reminder (no customer phone; only if enabled valid number) | §16 | 15 | I `ProfessionalDispatches_ForLifecycle`; U `Professional_NotificationEligibility` | [ ] |
| R-NTF-05 | Outbox + Hangfire; idempotent jobs; retries with backoff; failures recorded; no sensitive logging | §16 | 15 | I `Outbox_ProcessedOnce`; I `Reminder_Idempotent` | [ ] |
| R-NTF-06 | Reschedule/cancel removes obsolete reminders and schedules replacements (both audiences) | §16 | 15 | I `Reschedule_ReplacesReminderJobs` | [ ] |
| R-NTF-07 | Dispatch stores template version; later edits don't rewrite history | §16 | 15 | I `TemplateEdit_AffectsOnlyFutureDispatches` | [ ] |
| R-NTF-08 | Safe test-send to explicit test recipient only | §16 | 15 | I `TestSend_RequiresExplicitTestRecipient` | [ ] |
| R-NTF-09 | No messages for rolled-back transactions or seed data | §16 | 15 | I `Seed_DoesNotDispatch`; I `Rollback_NoDispatch` | [ ] |
| R-NTF-10 | In-app notifications + mark read; SignalR scoped | §17 | 15 | I/E | [ ] |
| R-QR-01 | Unique QR destinations for shops and professionals | §17 | 16 | I `QrCode_Unique` | [ ] |
| R-QR-02 | Visit tracking + booking attribution without invasive tracking | §17 | 16 | I attribution tests | [ ] |
| R-RVW-01 | Rating aggregates maintained transactionally or via reliable projection | §17 | 12 | I `RatingAggregate_UpdatedOnReview` | [ ] |

## 12. Documentation and delivery

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-DOC-01 | README (architecture, prerequisites, setup, migrations, seed, run, test, deploy) | §22 | 1 → 18 | M — initial README (Phase 01) | [~] |
| R-DOC-02 | architecture, domain-model, permissions-matrix, availability-and-booking, whatsapp-integration, deployment, backup-restore, design-deviations docs | §22 | per phase | M | [~] (design-deviations created Phase 0) |
| R-DOC-03 | Mermaid: deployed topology + booking/reminder lifecycle | §22 | 1, 15 | M — topology + backend structure in `docs/architecture.md` (Phase 01); lifecycle Phase 10/15 | [~] |
| R-DOC-04 | Nginx example, HTTPS-ready config, backup/restore instructions | §3 | 18 | M | [ ] |
| R-DOC-05 | Demo credentials in local-only file excluded from builds | §20 | 4 | C file ignored by git & excluded from Docker context | [ ] |
| R-DOC-06 | Final implementation report | §23 | 18 | M | [ ] |

## 13. Playwright flows (spec §19)

| Flow | Description | Built incrementally in | Completed in |
|---|---|---|---|
| E1 | Customer registers/signs in, discovers, books, views, cancels/reschedules | 4, 11, 12 | 12 |
| E2 | Shop creates/edits own service, sees only own data, walk-in, status changes, no foreign access/phone | 7, 9, 13 | 13 |
| E3 | Admin creates shop + pin location, professional (masked WhatsApp), assigns shop+service, manages subscription, no transfer action | 6, 7, 8 | 8 |
| E4 | Admin edits customer/professional templates; booking → fake dispatches + 30-min reminders for both audiences; no phone leakage | 15 | 15 |
| E5 | SuperAdmin plan + price, assign, change future price, history unchanged | 8 | 8 |
| E6 | Two concurrent customers, same slot, exactly one succeeds | 10 (API), 12 (UI) | 12 |
| E7 | Completed booking allows one review; incomplete/foreign does not | 12 | 12 |

All seven are re-run as the Phase 18 regression gate.

## 14. Data model inventory (spec §8)

`IShopOwned` entities get a tenant query filter, server-side stamping and composite `(ShopId, …)` FKs (R-TEN-02/04). Migration names are indicative and are confirmed when each phase creates them.

| Concept | Module (schema) | Shop-owned | Phase | Migration | Notes |
|---|---|---|---|---|---|
| User, Role, Permission, UserRole, RolePermission | Identity (`identity`) | — | 4 | 0002_Identity | ASP.NET Core Identity plus the permission catalogue |
| RefreshSession, OtpChallenge, Invitation | Identity | — | 4 | 0002_Identity | Refresh-token family rotation |
| CustomerProfile | Customers (`customers`) | — | 4 | 0002_Identity | Mobile is encrypted, with an HMAC lookup |
| Shop, ShopUser | Shops (`shops`) | Shop is the tenant root | 5 | 0003_ShopsTenancyAudit | ShopOwner / ShopStaff |
| AuditEntry | Administration (`audit`) | Has an optional ShopId | 5 | 0003 | Holds no PII |
| Shop profile, gallery, EditablePolicy | Shops | ✓ | 6 | 0004 | |
| ShopLocation | Shops | ✓ | 6 | 0004 | `geography(Point,4326)` with a GiST index |
| ShopOpeningHour, ShopClosure, pause flag | Availability (`availability`) / Shops | ✓ | 9 | 0007_Schedules | |
| Professional | Professionals (`professionals`) | ✓ | 6 | 0004 | ShopId is immutable |
| ProfessionalContact / WhatsAppSettings | Professionals | ✓ | 6 | 0004 | E.164, encrypted and masked |
| ProfessionalWorkingHour, ProfessionalBreak, ProfessionalTimeOff | Availability | ✓ | 9 | 0007 | |
| ServiceCategory | Services (`services`) | — (platform) | 7 | 0005_ServicesPackages | |
| ShopService | Services | ✓ | 7 | 0005 | Archive only once referenced |
| ServicePackage, ServicePackageItem | Services | ✓ | 7 | 0005 | Items reference services in the same shop |
| ProfessionalService | Professionals | ✓ | 7 | 0005 | Assigned by admins only |
| SubscriptionPlan, SubscriptionPlanPrice | Subscriptions (`subscriptions`) | — | 8 | 0006 | Prices are versioned |
| ShopSubscription, SubscriptionRenewal, SubscriptionOverride | Subscriptions | ✓ | 8 | 0006 | Price snapshot |
| PlatformSettings | Administration | — | 8 | 0006 | Typed sections, audited |
| Booking, BookingStatusHistory, BookingNote | Bookings (`bookings`) | ✓ | 10 | 0008_Bookings | `tstzrange` + exclusion constraint |
| OutboxMessage, IdempotencyRecord | BuildingBlocks (`infra`) | — | 10 | 0008 | |
| Review, rating aggregates | Reviews (`reviews`) | Tied to a shop through the booking | 11–12 | 0009 / 0010 | One review per booking |
| Favorite | Customers | — | 12 | 0010 | |
| WhatsAppTemplate, WhatsAppTemplateVersion, WhatsAppDispatch | Notifications (`notifications`) | — | 15 | 0012 | Dispatch records the template version |
| Notification (in-app), ProcessedMessage | Notifications | Optional ShopId | 15 | 0012 | |
| Hangfire tables | `hangfire` | — | 15 | Hangfire-managed | |
| QrCodeLink, QrVisit | QrAnalytics (`qr`) | ✓ | 16 | 0013_Qr | IP addresses are stored only as hashes |

## 15. External integrations inventory

| Integration | Abstraction | Dev/test implementation | Production configuration (env vars only) | Phase |
|---|---|---|---|---|
| WhatsApp messaging (Meta Cloud API) | `IWhatsAppProvider` | `FakeWhatsAppProvider` records dispatches to a table and a dev inbox | Access token, phone number ID, WABA ID, app secret for webhook signatures, approved templates | 15 |
| OTP delivery | `IOtpSender` | Fake: dev inbox or log, development only | WhatsApp authentication template; optional SMS provider | 4 (fake), 15 (WhatsApp) |
| Email (staff invitations and password resets) | `IEmailSender` | Mailpit container | SMTP host, port, credentials, sender | 4 |
| Maps and geocoding | `IMapProvider` (web), `IGeocoder` (API) | MapLibre GL plus an OSM-compatible tile source and geocoder, within their usage policy | Provider keys (D-007) | 6, 11, 17 |
| File storage (shop cover and gallery, QR posters) | `IFileStorage` | Local disk in `.data/uploads` | Object storage or a mounted volume, decided in Phase 18 deployment | 6, 16 |
| Future payment gateway | `IPaymentGateway` (documented seam only) | — | Not in v1 | 10 (docs) |

## 16. API endpoint inventory

The API endpoints for each feature are listed in the "API contracts and UI routes" and "In scope" sections of each phase file. The generated OpenAPI document (`apps/api/openapi/v1.json`, from Phase 01) is the authoritative, drift-checked inventory once code exists.
