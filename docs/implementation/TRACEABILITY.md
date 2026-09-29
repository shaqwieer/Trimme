# TRIMME — Requirements Traceability

Each requirement has an ID, its spec source, design reference, owning phase, the **named** verification that proves it, and a status (`[ ]` `[~]` `[x]` `[!]` `[-]`).
Test names are the intended names; the phase that implements a requirement must create a test with that name (or update this table if renamed).
Test layers: **U** backend unit · **I** backend integration (Testcontainers PostGIS) · **A** architecture test · **W** web unit/component (Vitest) · **E** Playwright E2E · **C** CI/static check · **M** manual/visual check with recorded evidence.

## 1. Negative requirements (things that must NOT exist)

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-NEG-01 | No barber transfer: `Professional.ShopId` immutable; no update DTO carries `shopId`; no transfer route/permission/UI | §7, §14, §23 | 6 | U `Professional_ShopId_HasNoPublicSetter` ✔, `NoProfessionalUpdateContract_ContainsShopId` ✔ (unit, on the API contracts); I `OpenApi_HasNoTransferOperation` ✔, `Professional_ShopId_IsImmutable_AndTheUpdateContractCannotCarryIt` ✔ (EF refuses to modify the key; extra `shopId` in the body ignored); I `PermissionCatalogue_HasNoTransferPermission` ✔ Phase 04; W `no barber transfer anywhere in the UI copy` ✔; E admin professional page has no transfer/move action ✔ (`shops-professionals.spec.ts`); C grep gate (test files excluded, D-069) ✔ Phase 06 | [x] |
| R-NEG-02 | No payment/checkout UI or charge flow | §2, §12, §23 | 12, 18 | E `customer-booking.spec.ts` E1 review step: no card/payment input or copy ✔; W wizard review "no payment step" ✔; I `OpenApi_has_no_payment_surface` (no payment/checkout/card path; `PaymentStatus` = NotApplicable only) ✔; `/account` payment row read-only "في المحل" (DV-S20) ✔ | [x] |
| R-NEG-03 | No customer export for shops | §7, §13 | 13 | I `OpenApi_has_no_export_surface` (no export, CSV, download or xlsx path) ✔; E E2 no «تصدير»/Export/CSV on the overview, appointments and calendar ✔ | [x] |
| R-NEG-04 | Customer phone never in shop DTOs/SignalR/HTML/logs | §7, §13 | 5, 13, 15 | I `ShopFacingContracts_DoNotContainCustomerPhone` ✔ Phase 05 (every shop-facing endpoint found from metadata; fails closed without typed responses — probe-verified; recursive member scan + live JSON scan; non-vacuity test `PhoneScanner_FindsPhoneMembers_InNestedTypes`); U `SensitiveDataRedactorTests` (logs) ✔; I `ShopHub_Messages_DoNotContainPhone` (13); E `shop_pages_payload_has_no_customer_phone` (13); Phase 13: hub messages carry ids, times and status only (I raw JSON has no customer/phone/mobile/+966) ✔; E E2 network scan of dashboard API responses and live frames for the seeded customer numbers and phone keys ✔ | [~] |
| R-NEG-05 | No shared professional across shops | §7 | 6 | I `Professional_BelongsToExactlyOneShop` ✔ (NOT NULL shop, composite contact FK rejects a cross-shop pair, the same WhatsApp number cannot be a professional in a second shop) | [x] |
| R-NEG-06 | Shops cannot create/delete/move professionals or assign services | §7 | 6, 7 | I `Shop_CannotCreateOrChangeProfessionals_AndSeesOnlyItsOwn` ✔ Phase 06; I `Admin_AssignsProfessionalService_SameShopOnly_AndShopCannotAssign` ✔ Phase 07 (shop 403; no shop assignment route; DB rejects a cross-shop pair); E shop PUT → 403 ✔ | [x] |
| R-NEG-07 | Tokens never in `localStorage`/`sessionStorage` | §9 | 4 | C ESLint bans `localStorage`/`sessionStorage` (probe-verified) ✔; E `no_tokens_in_web_storage` baseline ✔; E auth flows assert empty Web Storage and that `document.cookie` exposes neither `trimme-access` nor `trimme-refresh` after customer and staff sign-in ✔; I `Cookies_AreSecureHttpOnly` (tokens never in a response body) ✔ Phase 04 | [x] |
| R-NEG-08 | No hardcoded plan names/prices/durations/limits | §7, §15 | 8 | C grep gate ✔ (demo plan names/prices/durations appear only in seeders and tests; D-079); I plans served from DB only ✔; W durations/prices from plan data (`RecordPeriodForm` test) ✔ | [x] |
| R-NEG-09 | No hardcoded final WhatsApp message text in jobs/handlers | §16 | 15 | A `Notifications_Handlers_DoNotContainMessageLiterals`; I dispatch renders from active template version | [ ] |
| R-NEG-10 | Private dashboards not indexed | §6 | 11, 17 | E `private_routes_emit_noindex, robots.txt and sitemap_lists_shops` ✔ Phase 11 (meta robots noindex on search, discover, onboarding, auth, account; robots.txt disallows account/admin/auth/dev/discover/search/onboarding, the shop dashboard as `/xx/shop$` + `/xx/shop/` so `/xx/shops` stays crawlable, and the booking wizard); dashboards carry `noindex` since Phases 04–10 | [x] |

## 2. Foundation, quality and infrastructure

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-FND-01 | Monorepo layout + module boundaries/dependency direction | §4 | 1 | A `ArchitectureRules` (building-block direction, module Domain/Application namespace rules, cross-module refs via compiled metadata **and** `.csproj`, internal handlers) — proven non-vacuous (phase-01 evidence) | [x] |
| R-FND-02 | Nullable, warnings-as-errors, analyzers; strict TS, ESLint, formatting | §21 | 1, 2 | C `dotnet build -c Release` (TreatWarningsAsErrors, analyzers, code style) ✔ Phase 01; `pnpm lint` (0 warnings) / `pnpm typecheck` (strict) / `pnpm format:check` ✔ Phase 02 | [x] |
| R-FND-03 | `/api/v1` versioning, RFC 7807 problem details with stable error codes | §18 | 1 | I `UnknownRoute_Returns_ProblemDetails`, `MethodNotAllowed_Returns_ProblemDetails`, `ValidationError_HasStableCode_AndFieldErrors`, `UnexpectedError_DoesNotLeakExceptionDetails`; U `Status_codes_map_to_stable_error_codes`, `Domain_errors_become_problem_responses_with_their_code` | [x] |
| R-FND-04 | Health endpoints liveness/readiness | §3 | 1 | I `Health_Live_Returns200`, `Health_Ready_ChecksDatabase`, `Health_Ready_Returns503_WhenDatabaseIsUnreachable`; container `HEALTHCHECK` healthy | [x] |
| R-FND-05 | OpenAPI document + generated TS client, drift check | §18 | 1, 2 | I `OpenApi_document_matches_committed_contract` ✔ Phase 01; C `pnpm openapi:check` (generated `schema.d.ts` vs `v1.json`) ✔ Phase 02 | [x] |
| R-FND-06 | Structured logging, correlation IDs, PII redaction | §3, §21 | 1, 5 | I `Response_HasCorrelationId_GeneratedWhenMissing`, `Response_EchoesOnlyWellFormedCorrelationIds`; U `SensitiveDataRedactorTests` (phones incl. Arabic-Indic, JWT/Bearer, e-mail, sensitive names, enricher) | [x] |
| R-FND-07 | EF migrations from empty DB (PostGIS, btree_gist) | §19 | 1 → every data phase | I `Migrations_ApplyToEmptyDatabase`, `Migrations_AreIdempotent`, `Model_HasNoPendingChanges`; C `dotnet ef migrations has-pending-model-changes` — re-run by every data phase (✔ Phase 04 with `Identity`); ✔ Phase 11 with `Reviews` | [~] |
| R-FND-08 | Docker Compose (web, api, postgis) + Dockerfiles; `docker compose up --build` | §3, §21 | 1, 2 | C `infra/scripts/compose-smoke.sh`: postgis → migrate → api → web, web `/ar` 200 + web-origin `/api` proxy; containers healthy | [x] |
| R-FND-09 | CI: lint, typecheck, test, build, migration validation | §3 | 1, 2 | C `.github/workflows/ci.yml` jobs backend, web, secret-scan, stack (compose + Playwright) — **green on GitHub Actions run 36134142951** | [x] |
| R-FND-10 | `.env.example` files, no secrets committed | §3, §23 | 1 | C gitleaks v8.30.1 (`.gitleaks.toml`) working tree + history: no leaks; `.env.example`, `infra/.env.example` | [x] |
| R-FND-11 | Deterministic dev-only seed command (extended per phase) | §20 | 1 → 16 | U `Seed_IsDevelopmentOnly_and_requires_explicit_opt_in`, `Seed_is_allowed_with_development_flag_and_argument` ✔; I `AdminBootstrap_RequiresDevelopmentAndEnv` ✔; I `DevSeed_IsDeterministic_AndIdempotent` (two fresh databases, two runs each, identical rows) ✔ Phase 05 — extended by every data phase; Phase 08: plans with versioned prices, one subscription per status (two extra demo shops without users), asserted by the same test; Phase 11: six completed visits with reviews and rating aggregates, visit times on the history | [~] |
| R-FND-12 | Cancellation tokens end-to-end | §18 | 1+ | A `Endpoints_AcceptCancellationToken`, `Feature_endpoints_are_versioned_under_api_v1` (re-run every phase) | [x] |
| R-FND-13 | Pagination with safe max page size, filtering, sorting | §18, §21 | 5+ | U `PageRequest_ClampsPageSize` ✔; `PagedResponse<T>` envelope on `GET /admin/shops` (search, status filter, newest first) ✔ Phase 05; every later list endpoint uses it | [x] |
| R-FND-14 | Security headers, request size limits, upload validation | §9 | 1, 17 | I `SecurityHeaders_Present`, `RequestBody_TooLarge_Returns413Problem` ✔ Phase 01; upload validation Phase 06/17 | [~] |
| R-FND-15 | Strict CORS | §9 | 1 | I `Cors_AllowsConfiguredOrigin_WithCredentials`, `Cors_RejectsUnknownOrigin`; wildcard origins rejected at startup | [x] |

## 3. Web, design system, i18n, SEO, a11y

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-WEB-01 | Design tokens extracted to CSS variables; Tailwind uses them (no default palette leakage) | §5 | 2 | W `tokens.test.ts` (identity hexes, Tailwind defaults reset, breakpoints); C ESLint `no-restricted-syntax` raw-hex guard (probe-verified) | [x] |
| R-WEB-02 | Reusable component library matching ds-components | §5 | 3 | W 147 component tests incl. axe (`primitives`, `inputs`, `display`, `interactive`, `forms`); E gallery `/[locale]/dev/components` — every ds-components card has an equivalent, axe 0 serious/critical incl. contrast in ar/en, RTL keyboard, focus return; M 390/768/1440 captures vs `design/reference/1440/ds-components.jpg` | [x] |
| R-WEB-03 | Logo used unmodified via `next/image` (nav, auth, public, metadata) | Op. rule 6 | 2 | W `Logo_renders_with_intrinsic_ratio`; lossless transparent-margin crop verified (D-042); used in public, customer and dashboard shells | [x] |
| R-WEB-04 | `/ar` RTL default + `/en` LTR, `dir`/`lang` correct | §6 | 2 | E `locale_ar_is_rtl`, `locale_en_is_ltr`, root redirects (fr/ar browser → /ar, en → /en), language switch | [x] |
| R-WEB-05 | No hardcoded UI strings; ar/en key parity | §6 | 2 → all | W `messages_have_key_parity` (keys, empties, ICU placeholders; failure proven); C `react/jsx-no-literals` (probe-verified) — continues every phase; Phase 11: placeholder check ignores plural-branch text (probe: removing `{km}` from English fails) | [~] |
| R-WEB-06 | Locale-aware date/time/number/currency (SAR, Asia/Riyadh), bidi-safe phones/times/prices | §5, §6 | 2 | W `formatters_ar_en` (Gregorian Arabic, Asia/Riyadh, D-040 numerals, SAR, distance, rating, phone); E `bdi` LTR isolation | [x] |
| R-WEB-07 | Responsive at ~390/768/1440; dashboards sidebar + mobile drawer; tables → cards on small screens | §5 | 3, 13, 14 | E shells + gallery: RTL/LTR sidebar side, drawer <1200, bottom bar, table→cards <768, **no horizontal overflow at 390/768/1024/1440**; `captureViewports` ✔ Phases 02–03; screen-level layouts Phases 11–14 (Phase 11: public pages have no overflow at 390/768/1440 in E2E, with captures) | [~] |
| R-WEB-08 | Loading, empty, error, permission-denied, expired-session states; optimistic rollback | §5 | 3 → all | W `ErrorState`/`PermissionDenied`/`ExpiredSession`/`SkeletonList`/`InlineAlert`, toast rollback-ready ✔ Phase 03; E `expired_session_redirects_to_sign_in` (silent refresh, then sign-in with `returnTo`) and PermissionDenied for a customer on `/admin` ✔ Phase 04; W session fetch refresh/retry/expiry ✔ Phase 04; optimistic rollback in Phase 13 | [~] |
| R-WEB-09 | WCAG AA contrast, keyboard nav, visible focus, labels, accessible dialogs, 44px targets | §5 | 3, 17 | W token contrast (text 4.5:1, switch 3:1, segmented), dialog focus trap/return, labelled controls, axe (jsdom); E axe 0 serious/critical incl. contrast on /ar, /en, 3 shells, gallery ar/en ✔ Phases 02–03 — continues every phase; Phase 11: axe clean (serious/critical) on landing, `/shops`, discover, search, shop, professional and legal pages | [~] |
| R-WEB-10 | Localized metadata, canonical, hreflang, OG, robots.txt, sitemap | §6 | 11, 17 | W `public_pages_have_hreflang_and_canonical` ✔; E landing/shop canonical + hreflang (ar, en, x-default) ✔, `sitemap_lists_shops` (shops, professionals, hreflang alternates, hidden shops absent) ✔, robots.txt sitemap URL from runtime `TRIMME_SITE_URL` ✔ Phase 11; localization completion and SEO audit Phase 17 | [~] |
| R-WEB-11 | JSON-LD (Organization, LocalBusiness, Breadcrumb, AggregateRating only when real data) | §6 | 11 | W `jsonld_aggregateRating_absent_without_reviews`, escaping (`</script>` cannot break out), opening hours past midnight ✔; E Organization + WebSite on the landing (no AggregateRating), HairSalon with geo and AggregateRating (2 stored reviews) + BreadcrumbList on the shop page, Person + worksFor on the professional page ✔ Phase 11 | [x] |
| R-WEB-12 | RSC for public/read-heavy routes; client only where needed | §3 | 11 | M Phase 11: landing, `/shops`, shop, professional, discover, search and legal pages are Server Components; client islands only for the shop tabs, the filter drawer, the results map and mini-map, the location chooser and the browser-side distance (build output: public pages `ƒ` server-rendered, legal pages per request) | [x] |
| R-WEB-13 | Permission-aware navigation | §19 | 4, 13, 14 | W `nav_hides_items_without_permission`, DashboardShell permission test ✔; E SuperAdmin sees "Subscription plans", an invited OperationsManager does not (real `/me` permissions) ✔ Phase 04; per-screen checks in 13/14 | [~] |
| R-WEB-14 | Forms: RHF + Zod; API error mapping to fields | §3, §19 | 3+ | W `problemDetails_maps_to_field_errors` (exact API `validation.failed` shape → RHF field errors + form-level fallback), Zod schemas return message keys, translated errors | [x] |

## 4. Identity, sessions, authorization

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-AUTH-01 | Customer self-registration + sign-in per design flow with verified mobile (D-005) | §9, §12 | 4 | I `Customer_SignUp_VerifiesMobile` (+ attempts, expiry, single use, terms) ✔; E "signs up with a WhatsApp code, completes the profile and manages sessions" ✔ | [x] |
| R-AUTH-02 | Shop accounts created/invited by admin only | §9 | 4, 5 | I `ShopAccount_CannotSelfRegister`, `Admin_InvitesStaffUser_WhoAcceptsAndSignsIn`, `Invitation_CannotGrantARoleOfAnotherUserType` ✔ Phase 04; I `Admin_CreatesShop_WithOwnerInvite`, `Admin_InvitesShopUser_OnlyForAnExistingShop_AndShopRoles` ✔ Phase 05; E admin invites a shop owner who signs in to `/shop` ✔ | [x] |
| R-AUTH-03 | Admin seeded only in dev via env vars / one-time bootstrap | §9 | 4 | I `AdminBootstrap_RequiresDevelopmentAndEnv` ✔; compose `seed` service creates it once ✔ | [x] |
| R-AUTH-04 | Short access + rotating refresh in Secure/HttpOnly/SameSite cookies; reuse detection | §9 | 4 | I `Refresh_Rotates` (incl. 15-min access expiry), `RefreshReuse_RevokesFamily` (incl. 10 s race grace), `Cookies_AreSecureHttpOnly` ✔ | [x] |
| R-AUTH-05 | Sign-out, revoke-all-sessions, session list | §9 | 4 | I `RevokeAll_InvalidatesOtherSessions` (other access cookie rejected immediately), `SignOut_EndsTheSession_AndClearsCookies`, `RevokeSession_OfAnotherUser_Returns404` ✔; E "revoking other devices signs them out immediately" ✔ | [x] |
| R-AUTH-06 | Password reset (staff) / verification per D-005 | §9 | 4 | I `Staff_PasswordReset_Flow` (Mailpit link, sessions revoked, single use), `ForgotPassword_UnknownEmailOrCustomer_Returns202_AndSendsNothing`, `ResetPassword_RejectsWeakPassword_WithFieldError` ✔; E forgot → Mailpit → reset → sign in ✔ | [x] |
| R-AUTH-07 | Rate limiting + lockout (auth, OTP) | §9, §18 | 4 | I `Otp_RateLimited_PerNumber_WithResendCooldown`, `Otp_RateLimited_PerClient`, `Lockout_AfterFailedAttempts`, `Staff_SignIn_UnknownEmailAndWrongPassword_AreIndistinguishable` ✔ | [x] |
| R-AUTH-08 | CSRF protection for cookie-authenticated unsafe requests | §9 | 4 | I `UnsafeRequest_WithoutCsrf_Rejected`, `CsrfCheck_DoesNotMaskRoutingErrors_OrSafeMethods`, `CsrfToken_RotatesOnSignIn`, `Cors_Preflight_AllowsCsrfHeader_WithCredentials_ForKnownOriginOnly` ✔ | [x] |
| R-AUTH-09 | Roles + data-driven permission catalogue; authorization enforced by API | §7 | 4 | I `Endpoint_WithoutPermission_Returns403` matrix over every `/api/v1` endpoint (proven non-vacuous by removing a permission: test failed) ✔; I `CatalogueSync_RestoresManagedRoles_AndKeepsAdminEditsToEditableRoles`, `SeedRoles_GrantOnlyPermissionsOfTheirUserType` ✔; `docs/permissions-matrix.md` ✔ | [x] |
| R-AUTH-10 | SuperAdmin-only plan/pricing & overrides | §7, §15 | 8 | I `NonSuperAdmin_CannotManagePlans_OrOverride` ✔ (Operations Manager, Support and shop owner: 403 on plan writes, prices, override, and custom-duration/back-dated/explicit-price periods (D-081); endpoint matrix) | [x] |

## 5. Tenancy and privacy

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-TEN-01 | Tenant resolved from claims, never client-supplied shop ID | §7 | 5 | I `ShopEndpoints_IgnoreClientSuppliedShopId` (query + header ignored; probe shows the resolved tenant) ✔; E `shop_user_cannot_open_another_shop` ✔ | [x] |
| R-TEN-02 | Global query filters on every shop-owned entity | §7 | 5 → all | A `AllShopOwnedEntities_HaveTenantFilter` (+ tenant-root FK), `EntitiesWithAShopId_AreTenantScoped` (both probe-verified) ✔; I `ShopUser_SeesOnlyItsOwnRows_AndOthersSeeNone` on PostgreSQL ✔ | [x] |
| R-TEN-03 | Server-side tenant stamping on writes | §7 | 5 | I `Create_StampsTenantFromClaims`, `ShopId_CannotChange_EvenInsideTheAdminScope` ✔ | [x] |
| R-TEN-04 | Composite FKs prevent cross-shop references | §7 | 5 → all | I `CrossShopReference_RejectedByDatabase` (composite FK and tenant-root FK, application checks bypassed) ✔; I `ShopMembership_IsEnforcedByTheDatabase_AndCannotChange` ✔ | [x] |
| R-TEN-05 | Explicit isolated admin bypass | §7 | 5 | A `IgnoreQueryFilters_IsNeverCalled` (IL scan), `AdminDataScope_IsUsedOnlyByAdminUseCases`, `SystemDataScope_IsUsedOnlyByHostingSeedingAndJobs` (each probe-verified) ✔; runtime guards in `AdminDataScope`/`SystemDataScope`; Phase 06: `PublicDataScope_IsUsedOnlyByPublicUseCases` (probe-verified), read-only public scope (D-066); Phase 11: multi-shop public scope (D-090), I `PublicScopeForManyShops_ShowsExactlyThoseShopsRows_AndIsReadOnly` ✔ | [x] |
| R-TEN-06 | Cross-shop read/update/delete/booking/SignalR/enumeration (IDOR) tests | §7, §19 | 5 → 13 | I `CrossShop_ReadById_UpdateAndDelete_AreImpossible` (data layer), `ShopEndpoints_IgnoreClientSuppliedShopId`, `Suspending_A_Shop_RevokesTenantAccess_Immediately` ✔ Phase 05; isolation harness `ShopTestData.CreateTwoShopsAsync` ready; per-endpoint `CrossShop_*` suites as shop endpoints arrive (6–13); `ShopHub_DoesNotReceiveOtherShopEvents` (13); Phase 13 SignalR part: I `EachShop_ReceivesOnlyItsOwnBookings…` (no other shop's events; no client group join) ✔, E E2 another shop's booking 404 ✔ | [~] |
| R-TEN-07 | Phone numbers normalized E.164, encrypted, masked, redacted | §7, §8 | 5, 6 | U `PhoneNumber_NormalizesToE164`, `PhoneNumber_Masks_AndNeverPrintsTheNumber`, `Customer_normaliser_and_value_object_agree` ✔; I customer mobile stored only encrypted + keyed hash (`Customer_SignUp_VerifiesMobile`) ✔; U redaction ✔; professional numbers Phase 06 | [~] |
| R-TEN-08 | Audit log for admin/sensitive actions | §7, §14 | 5 → all | I `AdminShopActions_AreAudited_WithoutPersonalData` (create, activate, suspend with reason, invite; actor, correlation id, no email) ✔ Phase 05; `AdminServiceOverride_IsAudited` (7), `PhoneReveal_IsAudited` (6/14) | [~] |

## 6. Domain: shops, professionals, services, subscriptions

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-SHP-01 | Admin shop CRUD, activate/suspend, account setup | §14 | 5, 6 | I `Admin_CreatesShop_WithOwnerInvite`, `Suspending_A_Shop_RevokesTenantAccess_Immediately` ✔ Phase 05; I `Admin_EditsProfile_WithOptimisticConcurrency_AndAudit`, `Images_AreStoredInTheDatabase_ValidatedByContent_AndServedImmutable`, `PublicShopPage_ShowsOnlyActiveShops_ToEveryCaller` ✔ Phase 06; E admin edits profile + cover upload ✔ Phase 06 | [x] |
| R-SHP-02 | Location via designed pin picker; stored as PostGIS geography with spatial index | §8 | 6 | I `ShopLocation_StoredAsGeography_WithGistIndex_AndUsedBySpatialQuery` ✔ (type `geography(Point,4326)`, GiST index, X=lng/Y=lat, `ST_Distance` ordering); U location rounding/axis ✔; W `LocationPicker` search/drag/fallback ✔; E search → drag pin → confirm → saved point moved within Riyadh ✔ | [x] |
| R-SHP-03 | Shop public-profile edit limited to admin-permitted fields | §13 | 6 | I `Shop_CannotEditLockedField` ✔ (profile, location; staff 403; policy change applies); I `CrossShop_GalleryImageCannotBeRemovedByAnotherShop` ✔; I `SuspendedShop_HasNoProfileAccess_EvenThoughTheClaimRemains` ✔ | [x] |
| R-SHP-04 | Shop and professional images stored in the database, validated by content, metadata (EXIF/GPS) stripped, served immutable (D-064) | §9, user | 6 | U `ImageSanitizerTests` ✔; I `Images_AreStoredInTheDatabase_ValidatedByContent_AndServedImmutable` ✔; E cover upload renders (`naturalWidth` 1600) ✔ | [x] |
| R-PRO-01 | Admin professional CRUD/disable, one shop, WhatsApp number masked, notifications toggle | §7, §14 | 6 | I `Admin_CreatesProfessional_WithMaskedWhatsApp` ✔, `Reveal_RequiresPermissionAndReason_AndIsAudited` ✔, `DisabledProfessionals_LeaveThePublicPage_AndAvatarIsStored` ✔; U contact rules ✔; E flow 3 (masked number, audited reveal) ✔ | [x] |
| R-PRO-02 | Professional number absent from public/customer/other-shop DTOs | §8 | 6 | I `PublicAndShopProfessionalDtos_HaveNoPhone` ✔ (raw JSON of the public and shop lists; contract members) | [x] |
| R-SVC-01 | Shop creates/edits/activates/deactivates/archives/orders own services (ar/en, price, duration) | §10 | 7 | I `Shop_ServiceCrud_Flow` ✔ (own record only; stale 409; price/duration rules; staff 403; full-set reorder; archive final); U `CatalogDomainTests` ✔; W `ServiceForm`/`ShopCatalogList` ✔; E shop creates, edits price/duration, reorders by keyboard, archives ✔ | [x] |
| R-SVC-02 | Referenced services never physically deleted; archive only | §7 | 7, 10 | I `Service_InPackage_CannotBeDeleted_OnlyArchived` ✔ (package reference → 409 `service.in_use`; unused → 204) through the `IShopServiceUsage` seam; `Service_WithBookings_CannotBeDeleted` when bookings exist (Phase 10) | [~] |
| R-SVC-03 | Cross-shop service isolation on reads and every mutation | §19 | 7 | I `CrossShop_ServiceAndPackage_EveryVerb_Is404_AndNothingChanges` ✔ (GET/PUT/activate/deactivate/archive/DELETE on services and packages → 404; foreign ids in package items and order → 400; rows unchanged); I `SuspendedShop_CatalogCommands_Are404_Not500` ✔; shop-facing contract scan ✔ | [x] |
| R-SVC-04 | Admin categories, moderation, audited override; professional-service assignment | §14 | 7 | I `Admin_OverrideService_Audited_AndModerationHidesFromCustomers` ✔ (audit "Price 60.00 → 55.00 SAR; Duration 30 → 25 min; Name changed" + reason; Support 403); I `Admin_AssignsProfessionalService_SameShopOnly_AndShopCannotAssign` ✔; I `Categories_AdminManaged_BothLanguages_ActiveOnesPublishedAndSelectable` ✔; A `CrossModuleShopScopedReferences_ResolveToTheRealEntity` ✔; E admin hide/unhide, override, assignment ✔ | [x] |
| R-SVC-05 | Packages with items, price, duration; expand for reporting | §10 | 7, 10 | U `Package_Duration_And_Items` ✔ (explicit duration, ordered `ExpandItems`, rows kept on reorder, items-only edit touches the row); I `Packages_ConcurrencyOnItemsOnlyEdits_AndPublishedOnlyWhenEveryItemIsAvailable` ✔; reporting expansion with bookings (Phase 10) | [~] |
| R-SUB-01 | SuperAdmin plan catalogue (localized, price, currency, interval, features, limits, trial/grace, published, order) | §7, §15 | 8 | I `SuperAdmin_ManagesPlans_WithAppendOnlyPriceVersions` ✔; U `Publish_NeedsAPrice_AndArchiveIsFinal`, `Interval_IsBounded` ✔; W `PlanForm` ✔; E flow 5 ✔ | [x] |
| R-SUB-02 | Price versioning; no retroactive change | §15 | 8 | U `Prices_AreVersioned_…`, `AddPrice_NeverRewritesHistory` ✔; I `ExistingSubscription_KeepsPriceSnapshot_WhenThePlanPriceChanges` ✔; E flow 5 ✔ (recorded period unchanged; renewal takes version 2); Phase 11: a stale version can no longer add a price when nothing else on the plan changes (test clock on whole microseconds; probe: fails without the fix) | [x] |
| R-SUB-03 | Assign, renew, override (audited), history | §15 | 8 | I `Assign_Renew_Override_Suspend_KeepHistory_AndRejectInvalidChanges` ✔ (concurrent assign → one 409; overlap/gap; SuperAdmin custom periods with explicit total, standard amount and reason (D-081); override on the period in force; audit trail); U renew/override rules ✔; E flow 5 (suspend, reinstate, override) ✔ | [x] |
| R-SUB-04 | Statuses Active/ExpiringSoon/Expired/Suspended with configurable threshold | §15 | 8, 15 | U `Status_FollowsDaysRemaining_AndTheThreshold`, `MonthPeriods_…`, `MonthlyRenewalChain_…` ✔; I `Status_FollowsThePlatformCalendar_AndGatesBookability` ✔ (threshold boundary on a moving clock). Automated expiry notifications: Phase 15 | [x] |
| R-SUB-05 | Warnings to admin and shop; explicit enforcement; future bookings untouched | §15 | 8, 13, 15 | Phase 08 part ✔: shop warning + admin KPIs/list (E flow 3), enforcement setting and `IShopBookability` (I status test, settings test, D-078). Phase 10 part ✔: I `PauseAndExpiry_NeverTouchExistingBookings_ButBlockNewOnesAndReschedules` (suspension blocks new online bookings, the booking keeps its status and time, walk-ins still allowed). Expiry notifications: Phase 15 | [~] |

## 7. Availability and booking

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-AVL-01 | Availability combines hours, closures, pause, pro hours, breaks, time off, duration, bookings, lead time, horizon, slot step (5-min capable) | §11 | 9 | U `Availability/AvailabilityEngineTests` (22 incl. midnight, closures of the business day, lead/horizon edges, steps 5/10/15, New York DST gap and repeat) + `ScheduleDomainTests` (15) | [x] |
| R-AVL-02 | Only bookable slots returned | §11 | 9 | I `Availability/ScheduleTests` (public slots follow hours, breaks, time off, closures, pause, bookings via a fake reader; gates 404/422; package; disabled professional); E `flows/shop-schedule.spec.ts` | [x] |
| R-AVL-03 | Working hours, breaks, time off, pause/resume managed by shop | §13 | 9 | I `Shop_ManagesSchedule_AndThePublicSlotsFollowEveryChange`, `CrossShop_ScheduleIds_Are404_ForEveryVerb_AndNothingChanges`; E flow 2 schedule part (`shop-schedule.spec.ts`) | [x] |
| R-BKG-01 | Booking aggregate with service/price/duration snapshot | §10 | 10 | I `OnlineBooking_IsIdempotent_KeepsItsSnapshot_…` (price, name and duration kept after the service edit); U `Reschedule_KeepsTheSnapshot…` | [x] |
| R-BKG-02 | State machine; invalid transitions rejected; history with actor/timestamp | §11 | 10 | U `StateMachine_AllowsExactlyTheDesignedTransitions` + `ShopTransitions_FollowTheStateMachine_AndRecordHistory` (49 pairs each), time rules; I `WalkIns_…Transitions…` (409 invalid, 422 too early, reason required, history) | [x] |
| R-BKG-03 | Transactional recheck + PostgreSQL exclusion constraint | §11 | 10 | Migration `Bookings` exclusion constraint; I `TheDatabase_RefusesOverlappingActiveBookings_EvenWithoutTheApplication`; recheck in the transaction (D-089) | [x] |
| R-BKG-04 | Concurrency: exactly one winner | §11, §19 | 10, 18 | I `BookingConcurrencyTests` (8-way race, partial overlaps, reschedule race) ✔ Phase 10; E E6 two browser contexts confirm the same time at once: exactly one booking, the other sees "just taken" ✔ (×3 runs) | [x] |
| R-BKG-05 | Idempotent create/reschedule (idempotency keys) | §11, §18 | 10 | I `OnlineBooking_IsIdempotent_…` (required, replay, reuse 422), `TheSameIdempotencyKey_InParallel_CreatesOneBooking…`, reschedule replay | [x] |
| R-BKG-06 | Typed conflict response | §11 | 10, 12 | I every conflict asserts `booking.slot_unavailable` ✔; W wizard 409 → time step with the conflict notice and refetched slots ✔; E E6 ✔ | [x] |
| R-BKG-07 | Walk-ins use the same collision checks | §11 | 10, 13 | I `WalkIns_UseTheSameCollisionChecks_…` (overlap 409, outside hours 409, off-grid allowed); U `WalkInRule_AllowsAnyMinute_ButNotACollision` | [x] |
| R-BKG-08 | Outbox message written in the booking transaction | §16 | 10 | I outbox row per change, none for refused or losing commands (flow, races, refusals tests) | [x] |
| R-BKG-09 | Customer cancel/reschedule per policy | §12 | 10, 12 | U cutoff boundary ✔; I cancel and reschedule flows, pause/expiry ✔; I `RescheduleAvailability_IsForTheCustomersOwnActiveBookings_AndTheBookingsOwnTimeIsFree` ✔; W cancel dialog (reason, version, cutoff error) ✔; E E1 reschedule then cancel ✔ | [x] |
| R-BKG-10 | Payment seam: neutral booking fields + documented abstraction | §2 | 10 | M `docs/availability-and-booking.md` §Payment seam; `PaymentStatus`/`AmountDue` on every booking | [x] |

## 8. Customer experience

| ID | Requirement | Spec | Design | Phase | Verification | Status |
|---|---|---|---|---|---|---|
| R-CUS-01 | Landing page | §12 | c-landing | 11 | E `landing_renders_ar_en` ✔ (real non-zero figures, top-rated shops from stored ratings, search, partner band) | [x] |
| R-CUS-02 | Location permission / manual location | §12 | c-auth (location) | 11 | E `manual_location_sets_search_origin` ✔ (district list, cookie with rounded coordinates, no Web Storage); W `LocationChooser` (asks the browser only on press, falls back to the list) ✔ | [x] |
| R-CUS-03 | Nearby shops + search; list/map; distance sort | §12 | c-home, c-map | 11 | I `NearbySearch_IsNearestFirst_WithinTheRadius_AndListsOnlyVisibleShopsWithOffers` ✔ (PostGIS order, radius, far/unsubscribed/empty shops absent, city, pages); E list, price pins, selected-shop card, list alternative ✔ | [x] |
| R-CUS-04 | Filters: service, open-now, price range, verified, bookable-today, rating (sort) | §12 | c-map | 11 | I `Search_Filters_ByTextCategoryPriceOpenNowVerifiedAndBookableToday_AndSortsByRatingOrEarliest` ✔ (Arabic spelling variants, category "from" prices, price range, open now at a fixed clock, verified, bookable today with the bounded probe incl. an absent professional, rating and earliest sorts, popular categories); W `FilterSheet` live count ✔; E drawer → URL ✔ | [x] |
| R-CUS-05 | Shop page: gallery, description, rating, address, map, distance, open status, services, packages, professionals, reviews | §12 | c-shop | 11 | E `shop_page_sections` ✔ (live open status, directions, services + packages with booking links, barbers with next times, reviews as first name + initial, hours past midnight, policy from settings, mini-map, no phone in the HTML); I `PublicPages_ShopStatusProfessionalAndReviews_AreComplete_Live_AndCarryNoContactData` ✔ | [x] |
| R-CUS-06 | Professional profile within shop | §12 | c-shop | 11 | E professional profile (services, next free times, Person data, English) ✔; I professional detail and next slots, another shop's slug 404 ✔ | [x] |
| R-CUS-07 | Booking wizard service/package → professional → date → slot → review → confirmation | §12 | c-booking | 12 | W `BookingWizard` steps, URL state, "any" preselected, eligible professionals only, guest sign-in round trip, profile-incomplete redirect, conflict, idempotency key reuse ✔; W `wizard.test.ts` (URL derivation, returnTo round trip < 512, key per request) ✔; E E1 ✔ | [x] |
| R-CUS-08 | Upcoming/previous bookings, details, cancel, reschedule, .ics | §12 | c-appointments, c-rate | 12 | E E1 (upcoming list, details, `.ics`, reschedule, cancel) ✔; W cancel dialog ✔; W `BookingPolicy` (after the cutoff: closed window + shop phone, DV-S10) and `CreatedBanner` (Confirmed vs Pending, DV-S13) ✔ | [x] |
| R-CUS-09 | Post-completion review once; foreign/incomplete rejected | §12, §17 | c-rate | 12 | I `Review_IsForTheCustomersOwnCompletedVisit_OnceWithinTheWindow_AndUpdatesTheRatings` ✔, `Reviews_SubmittedTwiceAtOnce_OrInParallel_KeepOneReviewPerVisit_AndExactTotals` ✔; U review action/window and tags ✔; W `ReviewForm` ✔; E E7 ✔ | [x] |
| R-CUS-10 | Favorites (shops, professionals) — present in design | §12 | c-profile | 12 | I `Favorites_AreTheCustomersOwn_Idempotent_AndOnlyForListedShopsAndActiveProfessionals` (other customer cannot see or remove; data layer refuses; disabled professional and suspended shop leave the list) ✔; W `FavoriteButton` (signed-out link, optimistic with rollback, asks again after a client-side sign-in) ✔; E favorites test (guest heart → sign-in/sign-up → back with a working heart) ✔ | [x] |
| R-CUS-11 | Notifications center | §12 | c-profile | 15 | E mark-read | [ ] |
| R-CUS-12 | Profile, language, security/session settings | §12 | c-profile | 4, 12 | E account page + security/session list ✔ Phase 04; E profile name edit, language row, payment row ✔ Phase 12 | [x] |
| R-CUS-13 | QR destination with attribution (shop and professional) | §12, §17 | c-qr | 16 | I `QrVisit_Recorded`, `Booking_AttributedToQr`; E | [ ] |

## 9. Shop dashboard

| ID | Requirement | Spec | Design | Phase | Verification | Status |
|---|---|---|---|---|---|---|
| R-SD-01 | Operational overview + today's appointments | §13 | s-overview | 13 | I `Overview_CountsTheBusinessDay_WithEachProfessionalsLoad_AndTheLastWeekByHour` ✔; W board helpers (load, hours) ✔; E E2 overview + axe ✔ | [x] |
| R-SD-02 | Day/week calendar | §13 | s-calendar | 13 | I `Calendar_ShowsLanes_Bookings_AndKeepsTheHoursAfterMidnightInTheirBusinessDay` ✔; U `DayOf_GivesTheBusinessDaysWorkingTime…` ✔; W positioning, lanes, axis past 24:00, density ✔; E E2 day + week + axe ✔ | [x] |
| R-SD-03 | Appointment list/details, valid status changes | §13 | s-appointments | 13 | I `AppointmentsList_TakesAnyOfSeveralStatuses_AndCountsEveryChip…` ✔; W drawer (allowed transitions only, optimistic, rollback on 409, cancel reason) ✔; E E2 drawer note + cancel, as staff ✔ | [x] |
| R-SD-04 | Walk-in creation | §13 | s-walkin | 13 | I `WalkInOptions_ListTheActiveAssignedProfessionals…` ✔; W walk-in (no phone field, start now, conflict refresh) ✔; E E2 walk-in, as staff ✔ | [x] |
| R-SD-05 | Hours, professional schedule visibility, breaks, vacations, time off, pause/resume | §13 | s-hours | 9 | E | [ ] |
| R-SD-06 | Own services CRUD + archive | §13 | s-services (corrected) | 7 | E `services.spec.ts` shop flow ✔ | [x] |
| R-SD-07 | Subscription status + expiry visibility | §13 | s-services | 8 | Phase 08 E5 shop warning ✔; Phase 13 dashboard-wide banners (W `ShopBanners`) ✔ | [x] |
| R-SD-08 | Notifications | §13 | (absent) | 15 | E | [ ] |
| R-SD-09 | Profile editing within admin policy; location edit via pin | §13 | s-settings | 6 | W `ShopProfileEditor` locks ✔; E `shop owner edits only the fields the admin policy opens` ✔ (locked fields disabled with shield; location read-only; API 403) | [x] |
| R-SD-10 | Scoped SignalR live updates | §17 | — | 13 | I `EachShop_ReceivesOnlyItsOwnBookings_AdminsReceiveAll_AndMessagesCarryNoCustomerData` (non-vacuous: B's first message is its own), `Customers_AndAnonymousCallers_CannotConnect`, `HubRequests_FromAnotherOrigin_AreRefused`, `WebSockets_AreAcceptedOnlyFromTheWebAppsOrigin` ✔; W `OperationsLive.test.tsx` (reconnect backoff across closes, stops only on a refused refresh) ✔; E E2 live update on the owner's screen for a staff walk-in ✔ | [x] |

## 10. Admin dashboard

| ID | Requirement | Spec | Design | Phase | Verification | Status |
|---|---|---|---|---|---|---|
| R-AD-01 | Overview KPIs (today's appointments, completion rate, cancellations, no-shows, active shops, expiring subs, active pros, new customers, popular services, top shops) | §14, §17 | a-overview | 14 | I KPI query tests | [ ] |
| R-AD-02 | Shops management | §14 | a-shops | 5, 6 | E `/admin/shops` list/create/detail/invite/activate/suspend ✔ Phase 05; profile + location Phase 06 | [~] |
| R-AD-03 | Professionals management (no transfer) | §14 | a-pros | 6 | E flow 3 | [ ] |
| R-AD-04 | Services & packages platform-wide, categories, moderation, override, assignment | §14 | a-services | 7 | E `services.spec.ts` admin flow ✔; I suites above ✔ | [x] |
| R-AD-05 | Bookings global search/filters/details/history/intervention | §14 | a-appointments | 14 | I/E | [ ] |
| R-AD-06 | Customers list/profile with protected contact | §14 | a-appointments | 14 | I `PhoneReveal_RequiresPermission_AndAudits` | [ ] |
| R-AD-07 | Reviews moderation | §14 | a-reviews | 14 | I/E | [ ] |
| R-AD-08 | Subscription plans (SuperAdmin), assignment, renewal, overrides, history | §14 | a-subs | 8 | E flow 5 | [ ] |
| R-AD-09 | QR generation + analytics | §14 | a-reviews | 16 | E | [ ] |
| R-AD-10 | WhatsApp templates, dispatch log, retries, failures | §14 | a-reviews | 15 | E flow 4 | [ ] |
| R-AD-11 | Roles & permissions management | §14 | a-roles | 14 | I/E | [ ] |
| R-AD-12 | Audit activity | §14 | a-roles | 14 | E | [ ] |
| R-AD-13 | Platform settings (booking policy, locale, currency, tz, reminder offset, map defaults, thresholds, enforcement) | §14 | (absent) | 8, 14 | Phase 08 ✔: I `PlatformSettings_HaveDefaults_AreAudited_VersionChecked_AndPermissionGated` (defaults, ranges, 409, audit, migrate keeps edits, Ops read-only); U defaults/changed fields; W `PlatformSettingsForm`; E settings flow. Full sectioned screen: Phase 14 | [~] |

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
| R-RVW-01 | Rating aggregates maintained transactionally or via reliable projection | §17 | 11, 12 | Phase 11 `RatingBook` in the review unit of work ✔; Phase 12: atomic SQL upsert, I six parallel reviews give exact count/sum/histogram for shop and professional, double submit one 201 + one 409 ✔; U aggregate maths ✔ | [x] |

## 12. Documentation and delivery

| ID | Requirement | Spec | Phase | Verification | Status |
|---|---|---|---|---|---|
| R-DOC-01 | README (architecture, prerequisites, setup, migrations, seed, run, test, deploy) | §22 | 1 → 18 | M — initial README (Phase 01) | [~] |
| R-DOC-02 | architecture, domain-model, permissions-matrix, availability-and-booking, whatsapp-integration, deployment, backup-restore, design-deviations docs | §22 | per phase | M | [~] (design-deviations Phase 0; permissions-matrix Phase 04; availability-and-booking availability section Phase 09) |
| R-DOC-03 | Mermaid: deployed topology + booking/reminder lifecycle | §22 | 1, 15 | M — topology + backend structure in `docs/architecture.md` (Phase 01); lifecycle Phase 10/15 | [~] |
| R-DOC-04 | Nginx example, HTTPS-ready config, backup/restore instructions | §3 | 18 | M | [ ] |
| R-DOC-05 | Demo credentials in local-only file excluded from builds | §20 | 4 | C `docs/local/DEMO_CREDENTIALS.local.md` is git-ignored (`git check-ignore` ✔) and `docs/` is excluded from every Docker build context (`.dockerignore`) ✔ | [x] |
| R-DOC-06 | Final implementation report | §23 | 18 | M | [ ] |

## 13. Playwright flows (spec §19)

| Flow | Description | Built incrementally in | Completed in |
|---|---|---|---|
| E1 | Customer registers/signs in, discovers, books, views, cancels/reschedules | 4, 11, 12 | 12 ✔ (`customer-booking.spec.ts`) |
| E2 | Shop creates/edits own service, sees only own data, walk-in, status changes, no foreign access/phone | 7, 9, 13 | 13 ✔ (`services.spec.ts` + `shop-dashboard.spec.ts`: walk-in, live second screen, calendar, drawer note and cancel, foreign booking 404, payload and live-frame phone scan, no export) |
| E3 | Admin creates shop + pin location, professional (masked WhatsApp), assigns shop+service, manages subscription, no transfer action | 6, 7, 8 | 8 ✔ — Phase 06 part (`shops-professionals.spec.ts`: profile, cover, pin, professional, masked number, reveal, no transfer); Phase 07 service assignment (`services.spec.ts`); Phase 08 subscription part (`subscriptions.spec.ts`: statuses, suspend/reinstate, override, shop warning) |
| E4 | Admin edits customer/professional templates; booking → fake dispatches + 30-min reminders for both audiences; no phone leakage | 15 | 15 |
| E5 | SuperAdmin plan + price, assign, change future price, history unchanged | 8 | 8 ✔ (`subscriptions.spec.ts`) |
| E6 | Two concurrent customers, same slot, exactly one succeeds | 10 (API), 12 (UI) | 12 ✔ (`customer-booking.spec.ts`) |
| E7 | Completed booking allows one review; incomplete/foreign does not | 12 | 12 ✔ (`customer-booking.spec.ts`) |

All seven are re-run as the Phase 18 regression gate.

## 14. Data model inventory (spec §8)

`IShopOwned` entities get a tenant query filter, server-side stamping and composite `(ShopId, …)` FKs (R-TEN-02/04). Migration names are indicative and are confirmed when each phase creates them.

| Concept | Module (schema) | Shop-owned | Phase | Migration | Notes |
|---|---|---|---|---|---|
| User, Role, Permission, UserRole, RolePermission | Identity (`identity`) | — | 4 | `20260926080806_Identity` ✔ | ASP.NET Core Identity plus the permission catalogue (D-050, D-051) |
| UserSession + RefreshToken, OtpChallenge, Invitation | Identity | — | 4 | `Identity` ✔ | Refresh-token family rotation (D-052) |
| Data Protection key ring | BuildingBlocks (`infra`) | — | 4 | `Identity` ✔ | `infra.data_protection_keys` (D-052) |
| CustomerProfile | Customers (`customers`) | — | 12 | 0010 | Name, locale, terms and the encrypted mobile + HMAC lookup live on the Identity user from Phase 04 (D-050) |
| Shop | Shops (`shops`) | Tenant root (`ITenantRoot`) | 5 | `ShopsTenancyAudit` ✔ | Draft/Active/Suspended, slug, ar/en names, time zone, manual-confirmation flag (D-061) |
| Shop membership | Identity (`identity.users.shop_id`) | `ITenantMember`, FK to shops | 5 | `ShopsTenancyAudit` ✔ | Replaces a ShopUser table; ShopOwner / ShopStaff roles (D-059) |
| AuditEntry | Administration (`administration.audit_entries`) | Optional ShopId (allow-listed) | 5 | `ShopsTenancyAudit` ✔ | Holds no PII; append-only (D-063) |
| Shop profile, gallery, EditablePolicy | Shops | ✓ | 6 | 0004 | |
| ShopLocation | Shops | ✓ | 6 | 0004 | `geography(Point,4326)` with a GiST index |
| ShopOpeningHour, ShopClosure, pause flag | Availability (`availability`) / Shops | ✓ | 9 | `Schedules` ✔ | `shop_opening_hours` (week as JSON, D-082), `shop_closures`; pause row `shops.online_booking_pauses` (D-083) |
| Professional | Professionals (`professionals`) | ✓ | 6 | 0004 | ShopId is immutable |
| ProfessionalContact / WhatsAppSettings | Professionals | ✓ | 6 | 0004 | E.164, encrypted and masked |
| ProfessionalWorkingHour, ProfessionalBreak, ProfessionalTimeOff | Availability | ✓ | 9 | `Schedules` ✔ | `professional_working_hours`, `breaks` (professional optional = everyone), `professional_time_off`; composite FKs to professionals |
| ServiceCategory | Services (`services`) | — (platform) | 7 | 0005_ServicesPackages | |
| ShopService | Services | ✓ | 7 | 0005 | Archive only once referenced |
| ServicePackage, ServicePackageItem | Services | ✓ | 7 | 0005 | Items reference services in the same shop |
| ProfessionalService | Professionals | ✓ | 7 | 0005 | Assigned by admins only |
| SubscriptionPlan, SubscriptionPlanPrice | Subscriptions (`subscriptions`) | — | 8 | 0006 | Prices are versioned |
| ShopSubscription, SubscriptionRenewal, SubscriptionOverride | Subscriptions | ✓ | 8 | 0006 | Price snapshot |
| PlatformSettings | Administration | — | 8 | 0006 | Typed sections, audited |
| Booking, BookingStatusHistory, BookingNote | Bookings (`bookings`) | ✓ (+ customer-owned, D-085) | 10 | `Bookings` ✔ | `during tstzrange` + exclusion constraint; history owned (`booking_history`); snapshot (D-086) |
| OutboxMessage, IdempotencyRecord | BuildingBlocks (`infra`) | — | 10 | `Bookings` ✔ | D-089 |
| Review, RatingAggregate | Reviews (`reviews`) | Review: ✓ (+ customer-owned, D-085); aggregates: platform read model (allow-listed) | 11 | `Reviews` ✔ | One review per booking (unique); same-shop professional FK and booking FK (SQL); aggregates per shop and professional (D-092). Review creation: Phase 12 |
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
