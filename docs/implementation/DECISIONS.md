# TRIMME — Decision Log

Numbered, append-only. Status: **Accepted** (in force), **Proposed** (default in force until the user decides), **Open** (needs the user; blocks the phase listed), **Superseded**.
Deviation IDs (`DV-…`) refer to `docs/design-deviations.md`.
**Design line citations in this file use the analysis working-copy numbering.** Subtract 2 to get the canonical `design/source/TRIMME.dc.html` line; for example, 3501 here is canonical 3499. `design-deviations.md` already uses canonical numbering.

---

## D-001 — Repository layout and modular monolith — Accepted (Phase 0)
Empty repository → use the spec §4 layout: `apps/web` (Next.js), `apps/api` (ASP.NET Core host), `src/BuildingBlocks`, `src/Modules/{Identity,Shops,Services,Professionals,Availability,Bookings,Customers,Reviews,Subscriptions,Notifications,QrAnalytics,Administration}`, `tests/{UnitTests,IntegrationTests,ArchitectureTests,E2E}`, `infra/`, `docs/`, `design/`. pnpm workspace for JS. One PostgreSQL database with a schema per module; exact DbContext split (single vs per-module contexts) finalised in Phase 1 and recorded as an addendum here.

## D-002 — Pin .NET SDK to stable 10.0.1xx — Accepted (Phase 0)
The machine's default SDK is `10.0.300-preview.0.26177.108`; stable `10.0.112` is installed. Add `global.json` `{ "sdk": { "version": "10.0.112", "rollForward": "latestFeature", "allowPrerelease": false } }` in Phase 1. CI uses the same.

## D-003 — Package baselines (verified available 2026-09-25) — Accepted (Phase 0)
npm: `next` 16.3.x, `next-intl` 4.14.x, `@tanstack/react-query` 5.x, `tailwindcss` 4.3.x, `@playwright/test` 1.63.x. NuGet: EF Core 10.0.12, Npgsql.EntityFrameworkCore.PostgreSQL (+ NetTopologySuite) 10.0.3, Hangfire.AspNetCore 1.8.25, Hangfire.PostgreSql 1.21.1, FluentValidation 12.1.1, Testcontainers.PostgreSql 4.15.0. Exact versions pinned in lock files in Phases 1–2.

## D-004 — Application dispatcher: MediatR vs alternative — Accepted (user, 2026-09-25): in-house dispatcher
Spec lists MediatR "where useful". MediatR ≥ 13 requires a commercial licence key (Community licence free for orgs < US$5M revenue and < US$10M outside capital; a missing key logs warnings, no runtime limits). **Recommended default:** a thin in-house `ICommandHandler<TCommand,TResult>` / `IQueryHandler<,>` + pipeline behaviours (validation, logging, transaction) registered via DI — no licence dependency, same architecture. Alternative: MediatR 14 with a Community key supplied via configuration (never committed).

## D-005 — Customer authentication model — Accepted (user, 2026-09-25): passwordless OTP for customers
Design (3501, 918–970): passwordless mobile + 4-digit OTP via WhatsApp (SMS fallback text), "no passwords in v1". Spec §9/§12: ASP.NET Core Identity with password security, forgot/reset. **Recommended default:** customers = passwordless OTP on ASP.NET Core Identity (phone user, custom token provider, **6-digit** code (DV-C01), ≤5 min expiry, ≤3 attempts/code, per-phone + per-IP rate limits, lockout); shop and admin accounts = email + password with forgot/reset, lockout, invite flow. Forgot/reset is N/A for customers under this default. OTP sender is an adapter (fake in dev; WhatsApp authentication template in prod; SMS provider optional later).

## D-006 — Online booking initial status — Accepted (user, 2026-09-25): per-shop setting, default auto-confirm
Design contradicts itself: success copy "تم تأكيد حجزك" (1586) vs lifecycle "بانتظار التأكيد" + shop confirm (3575, 4024). **Recommended default:** per-shop setting `RequireManualConfirmation` (default **false** → online bookings are created `Confirmed`; when true → `Pending` and the shop must confirm). Copy and WhatsApp templates vary by resulting status ("confirmed" vs "request received").

## D-007 — Maps and geocoding provider — Accepted (user, 2026-09-25): OpenStreetMap
The design has **no** location pin picker (DV-A02) although spec §8 says it does; we design one in the TRIMME visual language. **Recommended default:** `IMapProvider`/`IGeocoder` adapters; development uses MapLibre GL JS with an OSM-compatible tile source and geocoder respecting their usage policies (low volume, attribution); production provider (e.g. Google Maps Platform or Mapbox) chosen by the user before Phase 17. API keys only via environment variables.

## D-008 — Spec wins over design where the spec is explicit — Accepted (Phase 0)
Operating rule 9. Visual language preserved; workflow corrected; every case recorded in `docs/design-deviations.md` (DV-S* items). Includes: no transfer, shop-owned services, SuperAdmin-only data-driven plan pricing, 30-minute reminders, only-bookable slots, separate cancellation statuses, customer phone never to shop.

## D-009 — Availability API returns only bookable slots — Accepted (Phase 0)
Spec §11 over design's "disabled slots with reasons" (DV-S07). The UI groups returned slots by period (morning/afternoon/evening) and shows a neutral empty-period message; no fabricated disabled slots. Past and lead-time slots are never returned.

## D-010 — Reminder offset — Accepted (Phase 0)
Platform setting `ReminderOffsetMinutes`, default **30**, used for customer and professional reminders (DV-S06). Design's 3-hour and day-before copy replaced.

## D-011 — No barber transfer, enforced structurally — Accepted (Phase 0)
`Professional.ShopId` set at creation, no setter after creation, not present in any update DTO; no route, permission, UI element, audit type, seed or doc mention (except `design-deviations.md` as removed, DV-S01). Tests: see TRACEABILITY `R-NEG-01`.

## D-012 — "Any available professional" option — Proposed (Phase 10/12)
Design shows "أي حلاق متاح" as default (3851, 4001). Kept: the server resolves a concrete professional inside the booking transaction (eligible = assigned to the service and free; tie-break = fewest bookings that day, then stable ID order). The booking always stores a concrete professional.

## D-013 — Paused shops in discovery — Proposed (Phase 9/11)
Design hides paused shops from discovery (2512, 3789). Default: platform setting `HidePausedShopsFromDiscovery = true`; the shop page stays reachable by direct link and shows "الحجز متوقف مؤقتاً"; no availability returned while paused; existing bookings unaffected.

## D-014 — Subscription expiry/suspension enforcement — Proposed (Phase 8)
Platform setting `ExpiredSubscriptionEnforcement` ∈ {`None`, `HideAndBlockNewOnlineBookings`} default `HideAndBlockNewOnlineBookings` for `Expired`/`Suspended`; future bookings are **never** altered or deleted; walk-ins remain allowed; `ExpiringSoon` threshold setting default 14 days. Covered by tests.

## D-015 — Cancellation after the cutoff — Proposed (Phase 10/12)
Design's "cancellation request reviewed by the shop" (1781, 3503) is not in the spec state machine. Default: customer can cancel online until `CancellationCutoffMinutes` (setting, default 120) before start; after that the UI shows the policy and the shop's public contact; no `CancellationRequest` entity in v1 (DV-S10).

## D-016 — Booking statuses — Accepted (Phase 0)
`Pending, Confirmed, Arrived, Completed, CancelledByCustomer, CancelledByShop, NoShow`. Admin interventions reuse `CancelledByShop` with actor type `PlatformAdmin` recorded in `BookingStatusHistory`. Allowed transitions: Pending→{Confirmed, CancelledByCustomer, CancelledByShop}; Confirmed→{Arrived, NoShow, CancelledByCustomer, CancelledByShop}; Arrived→{Completed}; terminal otherwise. UI renders only `allowedTransitions` from the API (DV-S08/S09).

## D-017 — Reviews — Proposed (Phase 12/14)
Once per completed booking by its customer; window `ReviewWindowDays` (setting, default 7); rating applies to the booking's professional and aggregates to shop and professional; displayed as first name + surname initial; post-moderation (published immediately, admins can hide with reason, audited).

## D-018 — Shop user roles — Proposed (Phase 4/5)
Design audit shows a reception user (4310). Seed `ShopOwner` (full shop scope per spec §7) and `ShopStaff` (bookings, calendar, walk-ins, status changes only). Both tenant-scoped to exactly one shop. Shop accounts are created/invited by admins only.

## D-019 — Admin roles — Proposed (Phase 4)
Seed `SuperAdmin` (= design's "مدير عام"), `OperationsManager`, `Support`, with a data-driven permission catalogue (`Admin.*`, `SuperAdmin.*`). Plan/pricing and subscription overrides only in `SuperAdmin`. Design matrix row granting subscriptions to Operations Manager is corrected (DV-S05).

## D-020 — Packages — Proposed (Phase 7)
Shop-owned `ServicePackage` with explicit localized name, price, total duration and items (references to the same shop's services, composite FK). A package booking is one contiguous appointment for one professional who must be assigned to every item service; reporting expands items. Admins can moderate/deactivate. Platform-level package *templates* are not in v1 unless requested.

## D-021 — Accessible colour tokens — Accepted (Phase 0; applied Phase 2)
Keep the steel-blue identity but adjust failing tokens to meet WCAG AA: link/secondary-brand text darkened from `#4A7FB5` (4.20:1) to a ≥4.5:1 shade; tertiary greys used for real text raised to ≥4.5:1 (decorative uses may keep original). Exact values computed in Phase 2 (DV-T01).

## D-022 — Fonts — Accepted (Phase 0)
Tajawal (Arabic) + Inter (Latin/numerals) via `next/font/google` (self-hosted at build), including the weights the design uses but did not load (Tajawal 600, Inter 800) (DV-T02).

## D-023 — Contact-mediation ("طلب تواصل") — Proposed: deferred `[-]`
Described in copy only (2370, 4617). Not in v1; the copy is removed. The phone rule is unaffected.

## D-024 — Shop daily WhatsApp summary — Proposed: deferred `[-]`
Design (4405) only; not in spec §16. Deferred.

## D-025 — Home "popular services" tiles — Accepted (Phase 0)
Rendered as category shortcuts with "from X ر.س" aggregated across nearby shops — never a global price (DV-S11).

## D-026 — Phone number storage — Proposed (Phase 5)
E.164-normalised; stored encrypted at rest (ASP.NET Core Data Protection or pgcrypto — finalised Phase 5) plus a keyed HMAC for lookup/uniqueness; masked in admin lists (`+966 5•• ••• •12`); full reveal only through an explicit, permission-gated, audited admin command; redacted by the logging enricher.

## D-027 — Cookie/session topology — Proposed (Phase 1/4)
Single production domain; Nginx: `/api/*`, `/hubs/*` → API, else → web. Access cookie (≈10–15 min) + rotating refresh cookie (reuse detection → revoke family), both `Secure; HttpOnly; SameSite=Lax`, path-scoped; CSRF via synchronizer/double-submit token header on unsafe methods; strict CORS allowlist (dev: `http://localhost:3000`). Any API-served operator UI must live under `/api/*` to be reachable, for example the Hangfire dashboard at `/api/ops/jobs`.

## D-028 — Booking wizard state in URL — Accepted (Phase 0)
`/shops/[slug]/book?step=&service=&pro=&date=&time=` so back/refresh/deep-link work; server validates everything on submit.

## D-029 — Locales — Accepted (Phase 0)
`/ar` default (RTL), `/en` (LTR); `/` redirects by `Accept-Language` with `ar` fallback. Latin digits for times/prices in both locales unless the design review in Phase 2 dictates Arabic-Indic digits for Arabic (design mixes both — DV-T03); decision finalised in Phase 2.

## D-030 — Time handling — Accepted (Phase 0)
`timestamptz` UTC in DB; `Shop.TimeZone` (IANA, default `Asia/Riyadh`); availability computed in shop-local time then converted; demo country SA, currency SAR.

## D-031 — Design import handling — Accepted (Phase 0)
`design/source/` holds the imported files; `support.js` is the generic Claude Design runtime and is never shipped or injected. Logo source of truth: repo-root `logo.png` (same size as the project file; the preview server re-encodes images).

## D-032 — Phase split (19 phases) — Accepted (Phase 0)
Rationale in `MASTER_PLAN.md` §7.

## D-033 — Guest booking via QR / inline auth — Proposed (Phase 12)
Design: phone requested only at confirmation (2059, 2079). Under D-005 default, the review step embeds phone → OTP → name sub-steps before `POST /bookings`; wizard state preserved in the URL.

## D-034 — Shop week view — Proposed (Phase 13)
Real week grid (per day × professional, minute-accurate) plus the design's density heatmap as a summary strip (DV-A19).

## D-035 — Walk-in initial status — Proposed (Phase 13)
"Start now" walk-in → `Arrived`; scheduled walk-in → `Confirmed`. Both pass the same collision checks as online bookings.

## D-036 — Shop services editing restored — Accepted (Phase 0)
Design's "activation only / prices managed by platform" for shops (2556–2575, 3537, 3539, 4414) replaced with full shop CRUD per spec §10 (DV-S03). Admin "global catalogue" replaced by categories + moderation + audited override (DV-S02).

## D-037 — Resolution of D-004…D-007 (user answers, Session 1, 2026-09-25) — Accepted
- **D-004:** use the **in-house dispatcher**, not MediatR. It is a thin `ICommandHandler<,>`/`IQueryHandler<,>` with pipeline behaviours for validation, logging and transactions, registered through DI. No MediatR package is referenced. The spec's "MediatR where useful" is satisfied by the same pattern.
- **D-005:** customers use **passwordless mobile + OTP**. The code is 6 digits (DV-C01), expires in 5 minutes or less, allows at most 3 attempts per code, and is rate-limited per phone and per IP with lockout. It is sent over WhatsApp through `IOtpSender`, using a fake in development. Shop and admin staff use email + password, with forgot/reset, lockout and invitations over email (Mailpit in development). This follows spec §9: customers get "the flow shown in the imported design", and password reset applies to staff.
- **D-006:** each shop has a `RequireManualConfirmation` setting. It defaults to **off**, so online bookings are created `Confirmed`. When a shop turns it on, its online bookings are created `Pending` and the shop confirms them. Confirmation copy and WhatsApp templates follow the resulting status.
- **D-007:** production uses **OpenStreetMap**. The web map is MapLibre GL rendering OSM-based vector or raster tiles, and geocoding is OSM-based (Nominatim-compatible API), both behind `IMapProvider`/`IGeocoder`. **Constraint:** the public `tile.openstreetmap.org` and `nominatim.openstreetmap.org` services have usage policies that forbid heavy or production application traffic. Production must therefore point the adapters at a **self-hosted** tile server and Nominatim, or at an OSM-based hosted tile and geocoding service. The endpoint URLs, keys and attribution text are configuration only, and "© OpenStreetMap contributors" attribution is always displayed. Development may use the public endpoints at very low volume with a proper User-Agent and caching. The concrete production host is chosen in Phase 17 or 18 as a configuration item and does not block any phase.

## D-038 — Backend project layout, persistence and host commands (D-001 addendum) — Accepted (Phase 01)
- **One project per module** (`src/Modules/<X>/Trimme.Modules.<X>`). Layers are namespaces: `.Domain`, `.Application`, `.Infrastructure`, `.Api`. Handlers and infrastructure types are `internal`, so the compiler stops other modules reaching them. When cross-module contracts are needed, a module gets a public `Trimme.Modules.<X>.Contracts` project, which is the only module project other modules may reference. Namespace-level rules are architecture tests, proven non-vacuous by a deliberate violation (recorded in the phase-01 evidence).
- **BuildingBlocks:** `Trimme.BuildingBlocks.Domain`, `.Application` (in-house dispatcher, D-037), `.Infrastructure` (EF base, `TrimmeDbContext`, conventions, redaction) and `.Web` (module and endpoint abstractions, problem details, middleware).
- **One shared `TrimmeDbContext` with one PostgreSQL schema per module.** Modules contribute `IEntityTypeConfiguration`s through `IModelContributor`. This keeps composite FKs across modules, the booking exclusion constraint, and booking + outbox writes inside one transaction straightforward. Naming is snake_case (EFCore.NamingConventions). Instants are `timestamptz` in UTC. Optimistic concurrency uses PostgreSQL `xmin`.
- **Migrations** live in a dedicated `src/Trimme.Migrations` assembly, and the history table is `public.__ef_migrations_history`. Command: `dotnet ef migrations add <Name> --project src/Trimme.Migrations --startup-project apps/api`.
- **Host commands:** `Program.cs` has no side effects before `Build()`. After `Build()`, the host dispatches CLI verbs:
  - `migrate` applies migrations (an explicit release step, and a one-shot compose service);
  - `seed --dev` runs only in `Development` with `TRIMME_ALLOW_DEV_SEED=true`.
  
  A lightweight `healthcheck` verb probes `/health/live` before the builder is created, for container health checks. The API never auto-migrates on startup.
- **Integration tests** run in environment `Testing`, which enables the OpenAPI document but not the dev seed. The Testcontainers image is `postgis/postgis:17-3.5`.
- **Clock:** .NET `TimeProvider` (with `FakeTimeProvider` in tests) instead of a custom `IClock`.
- **OpenAPI drift check:** an integration test compares `/openapi/v1.json` with the committed `apps/api/openapi/v1.json`; `TRIMME_UPDATE_OPENAPI=1` regenerates it. There is no build-time generation.

## D-003 addendum — Test and tooling packages (Phase 01)
- Tests: xunit.v3 4.0.1, xunit.runner.visualstudio 4.0.0, Microsoft.NET.Test.Sdk 18.10.1, Microsoft.AspNetCore.Mvc.Testing 10.0.12, Testcontainers.PostgreSql 4.15.0, Shouldly 4.3.0.
- Architecture: NetArchTest.eNhancedEdition 1.4.5.
- Logging: Serilog.AspNetCore 10.0.0 and Serilog.Formatting.Compact 3.0.0.
- OpenAPI: Microsoft.AspNetCore.OpenApi 10.0.12, with Scalar.AspNetCore 2.17.9 for the dev UI.
- Health: Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 10.0.12.
- EF naming: EFCore.NamingConventions 10.0.1.
- Tool: dotnet-ef 10.0.12 (local tool manifest).
- Validation: FluentValidation(.DependencyInjectionExtensions) 12.1.1.
- FluentAssertions is avoided because v8 is commercially licensed.

## D-039 — Accessible text colour values (applies D-021) — Accepted (Phase 02)
These colours were measured in the WCAG check. The design's identity colours are unchanged, and only the colours used for text were adjusted:
- `text-tertiary`: design `#8C9BAA` (2.84:1) → **`#5F6F80`** (5.16:1 on white, 4.89:1 on the page background).
- `text-placeholder`: design `#A9B6C4` (2.06:1) → **`#5F6F80`**. Search fields sit on light grey, so `#687888` was rejected (4.30:1 on the page background).
- Link text uses `brand-700` `#2C5C8C` (6.96:1). `brand-600` `#4A7FB5` (4.20:1) is kept for icons and dots only.
- The inactive bottom-nav label uses `text-secondary` `#647484` (4.80:1) instead of `#98A7B5` (2.46:1).
- White text is never placed on `success-500` or `brand-500` (3.38:1 and 2.92:1).

`src/styles/tokens.test.ts` enforces 4.5:1 for every text token on white and on the page background, and for every booking-status badge.

## D-040 — Numerals, calendar and number formatting — Accepted (Phase 02)
This follows the design's numeral rule (design/analysis/01 §1.5):
- **Arabic-Indic digits** for clock times and date text, on a 12-hour clock, for example `٥:٣٠ م` and `الجمعة، ١٨ سبتمبر`.
- **Latin digits** for measurable quantities: price (`85 ر.س`), distance (`2.4 كم`), rating, counts, calendar day numbers and phone numbers.
- English uses Latin digits everywhere.
- Service durations follow the clock rule (`٣٠ دقيقة`).
- The **Gregorian calendar is always forced** (`-u-ca-gregory`), because plain `ar-SA` defaults to the Umm al-Qura (Hijri) calendar.
- The operating time zone is `Asia/Riyadh`.

Implemented in `apps/web/src/lib/i18n/format.ts` and tested in `format.test.ts`.

## D-041 — Breakpoints — Accepted (Phase 02)
The design says 992px in one place and 1200px in another. The responsive rule text ("≥1200 fixed sidebar · 768–1199 drawer · <768 bottom bar/cards") is chosen. Tailwind breakpoints are `sm` 390, `md` 768, `lg` 1200 and `xl` 1440, and Tailwind's defaults are removed.

## D-042 — Logo asset handling — Accepted (Phase 02)
- The official PNG (677×369) is mostly transparent padding. For the web, only its **fully transparent margin was removed** (371×177), keeping a 4% transparent safety border. The crop was verified pixel-identical to the source region, with zero non-transparent pixels dropped.
- The mark is never redrawn, recoloured or stretched. `next/image` renders it at its intrinsic aspect ratio.
- On navy surfaces, the design's own documented treatment (`brightness(1.35) saturate(.85)`, design lines 216 and 797) is applied. It is the only visual adjustment and comes from the design, not from us.
- The source file stays untouched in `design/source/` and at the repo root.
- Open follow-up: the design's minimum logo width is 96px, but its header and sidebar sizes are below that. We follow the design sizes: header 34–38px tall (71–80px wide) and sidebar 36px tall (75px wide).

## D-043 — Web toolchain versions — Accepted (Phase 02)
- Next 16.3.6, React 19.3.0, next-intl 4.14.7, Tailwind 4.3.3, TanStack Query 5.103.2, openapi-fetch 0.17 plus openapi-typescript 7.13, lucide-react 1.48, Vitest 5.0.2, Testing Library, Playwright 1.63 plus @axe-core/playwright 4.13, and ESLint 10.11 with eslint-config-next 16.3.6.
- **TypeScript 6.0.3** rather than 7.0, because typescript-eslint 8.70 supports only TypeScript below 6.1.
- **jsdom 29.1.1** rather than 30, because jsdom 30 requires Node 22.22.2 or later and the dev machine has 22.18.0. The Docker image `node:22-alpine` has 22.23.
- ESLint 10 needs an explicit `settings.react.version`, because eslint-plugin-react 7.37 uses a removed API for auto-detection.
- `vite-tsconfig-paths` was dropped (a TypeScript 5 peer requirement) in favour of an explicit `@` alias.
- pnpm workspace: `apps/web` and `tests/E2E`.

## D-044 — Same-origin API access from the web app — Accepted (Phase 02)
- The browser always calls `/api/...` on the web origin. In production Nginx routes `/api` and `/hubs` to the API. In development, and for a bare `next start`, `next.config.ts` rewrites them to `TRIMME_API_INTERNAL_URL`, which is fixed at build time. The Docker build argument is `http://api:8080`.
- Server Components use `getServerApi()`. It forwards the caller's cookies, `Accept-Language` and correlation ID, and uses `cache: 'no-store'`, so per-user data is never shared.
- The browser client sends the CSRF header from the readable double-submit cookie. The CSRF cookie itself arrives in Phase 04.

## D-045 — Development-only routes — Accepted (Phase 02)
`/[locale]/dev/*` (shell previews now, the component gallery in Phase 03) calls `assertDevRoutesEnabled()` on every request. The routes return 404 when `NODE_ENV=production`, unless `TRIMME_ENABLE_DEV_ROUTES=true`. Local compose sets that flag so the E2E smoke tests can run; production must never set it. The dev routes are also `noindex`.

## D-046 — Design reference screenshots — Accepted (Phase 02)
All 33 prototype screens are captured at the prototype's own 1440px canvas (`design/reference/1440/*.jpg`, 3.5 MB). The prototype is a fixed desktop canvas: phone (390px) designs are drawn inside it as device frames, and tablet behaviour is only described on `r-responsive`. Separate 390px and 768px captures of the prototype would be meaningless. Our own implementation is captured at 390, 768 and 1440 by the Playwright helper `captureViewports`.

## D-047 — Font stack — Accepted (Phase 02)
`font-family: Inter, Tajawal, …`. Latin letters and Latin digits render in Inter, and Arabic glyphs fall through to Tajawal. This implements the design's mixed-text rule without wrapping every number. next/font's metric-adjusted Inter fallback (Arial) is disabled, because Arial contains Arabic glyphs and would otherwise capture Arabic text. English pages use Inter. Line height is 1.8 for Arabic and 1.6 for English. Weights: Tajawal 400/500/700/800 and Inter 400–800, including the 800 the design uses but did not load (DV-T02).

## D-048 — UI primitives, forms and component testing — Accepted (Phase 03)
- **Headless primitives:** `radix-ui` 1.6.7 (the unified package), used **only** where accessibility behaviour is non-trivial: Dialog, which also backs Sheet/Drawer and ConfirmDialog; DropdownMenu; Tooltip; Tabs; and Slider (dual-thumb price range). Everything else is a small component styled with tokens.
- **Direction:** a `Direction.Provider` in the locale layout feeds `dir` to Radix, so arrow keys and the slider are mirrored in RTL.
- **Single dialog implementation:** the Phase 02 dashboard drawer moves onto the Radix-based `Sheet`, and the hand-written `useFocusTrap` is removed.
- **Forms:** react-hook-form 7.88, zod 4.6 and `@hookform/resolvers` 5.9. Schemas return **message keys** (`validation.*`), which fields translate. `applyProblemToForm()` maps API `validation.failed` field errors onto form fields, and unknown fields become a form-level error.
- **Server Component safety:** presentational components (badges, cards, empty/error states, skeletons, rating display, KPI tiles, breadcrumb, timeline) contain no hooks other than `useTranslations`, so public pages stay RSC. Selectable rows and cards use native radio or checkbox inputs.
- **Class composition:** no `tailwind-merge`, because its default config misreads TRIMME's custom `text-*` size tokens as colours. Components expose `variant`/`size` props, and `className` only appends layout classes.
- **Accessibility tests:** an `axe-core` helper runs in Vitest (jsdom). It asserts on violations only; jsdom cannot evaluate colour contrast. Contrast is covered by `tokens.test.ts` and by Playwright axe runs on the component gallery.
- **Components follow earlier decisions:**
  - SlotGrid shows only bookable and selected slots (D-009), with no disabled slots and no reasons;
  - StatusBadge uses the D-016 enum (`CancelledByCustomer` and `CancelledByShop` share a colour);
  - OTP has 6 digits (D-037);
  - PhoneField has a fixed +966 prefix and emits E.164;
  - slots are 44px tall (DV-T05);
  - dates are passed as local `YYYY-MM-DD` strings in the shop's time zone.

## D-049 — Component-level accessibility adjustments to the design — Accepted (Phase 03)
Each adjustment was found by tests and keeps the design's look.
- **Switch off-track:** the design's `#DDE4EC` (1.3:1) became the new token `--color-switch-off` `#7F8FA0` (3.31:1 on white), meeting WCAG 1.4.11 non-text contrast. Enforced by `tokens.test.ts`.
- **Segmented control, inactive labels:** the design's `#647484` on the `#F1F5F9` track was under 4.5:1, which Playwright axe caught in the gallery. The labels now use `text-tertiary` `#5F6F80` (≥4.5:1 on that track), enforced by `tokens.test.ts`.
- **Bar chart:**
  - Regular bars use brand-500 `#6D9BCB` (2.84:1) instead of the design's `#B9CBDD` (1.62:1). Peaks stay navy, as in the design.
  - Per the dataviz rules, the contrast WARN is relieved by a direct label on the peak value, per-bar values on hover, and a screen-reader data table.
  - Bars are capped at 85% of the plot height so the peak label fits.
- **Visually hidden inputs:** every `<label>` that wraps a `sr-only` input is `position: relative`. Without it, the absolutely positioned input escaped horizontal scrollers (the date strip) and caused 87–138px of page overflow at 390px. A Playwright test now asserts zero horizontal overflow at 390/768/1024/1440 on the home page, the gallery and the shells.
- **Fieldsets** that hold horizontally scrolling content get `min-w-0`, because the browser default `min-inline-size: min-content` defeats `overflow-x`.
- **Arabic initials** skip the definite article (`محمد العنزي` → `م ع`, matching the design).
- **Toast timing:** auto-dismiss is 5 s (the design used 2.6 s), paused on hover and focus. Toasts with an action never auto-dismiss (WCAG 2.2.1).
- **Radix behaviour:**
  - Dialogs expose modality by hiding the rest of the page (`aria-hidden` on siblings) rather than with `aria-modal`.
  - Focus returns to the element passed as `trigger`, so every Sheet or Dialog opener is passed as its trigger.
  - Menus are named by their trigger.
