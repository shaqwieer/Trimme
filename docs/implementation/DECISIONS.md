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

## D-012 — "Any available professional" option — Accepted (candidate sets Phase 09, D-082; resolution Phase 10, D-088)
Design shows "أي حلاق متاح" as default (3851, 4001). Kept: the server resolves a concrete professional inside the booking transaction (eligible = assigned to the service and free; tie-break = fewest bookings that day, then stable ID order). The booking always stores a concrete professional.

## D-013 — Paused shops in discovery — Accepted (setting in Phase 08; pause and gate in Phase 09, D-083; discovery in Phase 11)
Design hides paused shops from discovery (2512, 3789). Default: platform setting `HidePausedShopsFromDiscovery = true`; the shop page stays reachable by direct link and shows "الحجز متوقف مؤقتاً"; no availability returned while paused; existing bookings unaffected.

## D-014 — Subscription expiry/suspension enforcement — Accepted (Phase 08; see D-076, D-078)
Platform setting `ExpiredSubscriptionEnforcement` ∈ {`None`, `HideAndBlockNewOnlineBookings`} default `HideAndBlockNewOnlineBookings` for `Expired`/`Suspended`; future bookings are **never** altered or deleted; walk-ins remain allowed; `ExpiringSoon` threshold setting default 14 days. Covered by tests.

## D-015 — Cancellation after the cutoff — Accepted (Phase 10 API, D-087; UI Phase 12)
Design's "cancellation request reviewed by the shop" (1781, 3503) is not in the spec state machine. Default: customer can cancel online until `CancellationCutoffMinutes` (setting, default 120) before start; after that the UI shows the policy and the shop's public contact; no `CancellationRequest` entity in v1 (DV-S10).

## D-016 — Booking statuses — Accepted (Phase 0)
`Pending, Confirmed, Arrived, Completed, CancelledByCustomer, CancelledByShop, NoShow`. Admin interventions reuse `CancelledByShop` with actor type `PlatformAdmin` recorded in `BookingStatusHistory`. Allowed transitions: Pending→{Confirmed, CancelledByCustomer, CancelledByShop}; Confirmed→{Arrived, NoShow, CancelledByCustomer, CancelledByShop}; Arrived→{Completed}; terminal otherwise. UI renders only `allowedTransitions` from the API (DV-S08/S09).

## D-017 — Reviews — Proposed (Phase 12/14)
Once per completed booking by its customer; window `ReviewWindowDays` (setting, default 7); rating applies to the booking's professional and aggregates to shop and professional; displayed as first name + surname initial; post-moderation (published immediately, admins can hide with reason, audited).

## D-018 — Shop user roles — Proposed (Phase 4/5)
Design audit shows a reception user (4310). Seed `ShopOwner` (full shop scope per spec §7) and `ShopStaff` (bookings, calendar, walk-ins, status changes only). Both tenant-scoped to exactly one shop. Shop accounts are created/invited by admins only.

## D-019 — Admin roles — Proposed (Phase 4)
Seed `SuperAdmin` (= design's "مدير عام"), `OperationsManager`, `Support`, with a data-driven permission catalogue (`Admin.*`, `SuperAdmin.*`). Plan/pricing and subscription overrides only in `SuperAdmin`. Design matrix row granting subscriptions to Operations Manager is corrected (DV-S05).

## D-020 — Packages — Accepted (Phase 07, implemented by D-072)
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

## D-033 — Guest booking via QR / inline auth — Superseded by D-096 (Phase 12)
Design: phone requested only at confirmation (2059, 2079). Under D-005 default, the review step embeds phone → OTP → name sub-steps before `POST /bookings`; wizard state preserved in the URL.

## D-034 — Shop week view — Accepted, refined by D-100 (Phase 13)
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
- The inactive bottom-nav label uses `text-secondary` (now `#5F6F80`, 5.16:1; see below) instead of `#98A7B5` (2.46:1).
- `text-secondary`: `#647484` → **`#5F6F80`** (Phase 13 CI follow-up). `#647484` was 4.35:1 on `bg-muted` and 4.38:1 on `bg-subtle`, the grey note boxes and hovered links. `#5F6F80` is at least 4.59:1 on every light surface, including `bg-app`.
- White text is never placed on `success-500` or `brand-500` (3.38:1 and 2.92:1).

`src/styles/tokens.test.ts` enforces 4.5:1 for every text token on white and on the page background, for the secondary and tertiary greys on `bg-muted`, `bg-subtle` and `bg-app`, and for every booking-status badge.

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

## D-048 addendum — Server-Component safety verified (Phase 03, after review)
The first version of D-048 claimed RSC safety too broadly. Five components attached their own
event handlers without `'use client'`: `Chip`, `RemovableChip`, `AddChip`, `SegmentedControl` and
`RatingInput`. They would have failed when rendered from a Server Component, and the gallery never
exercised that because the gallery itself is a client component.

Fix:
- **Split the interactive components out:** the five now live in the `'use client'` modules
  `components/ui/selection.client.tsx` and `components/ui/RatingInput.tsx`.
- **Kept server-safe:** `RadioCard`, `TagChip` and `RatingStars` stay in their server-safe modules,
  so `ShopCard` ships no JS for ratings.
- **Proof route:** `/[locale]/dev/components/server` has **no** `'use client'` and renders every
  component documented as RSC-safe:
  - buttons: Button, ButtonLink, IconButton;
  - badges: badges, StatusBadge, RatingStars;
  - cards: ShopCard, AppointmentCard, KpiTile, ServiceOption, RadioCard, ProfessionalOption;
  - charts: RatingDistribution, BarChart, QrCard;
  - navigation and data: Breadcrumb, LinkTabs, ResponsiveTable, Pagination, Timeline;
  - states: Avatar, EmptyState, InlineAlert, Skeleton, SkeletonList, PermissionDenied,
    ExpiredSession, ErrorState.
- **Regression guard:** E2E asserts HTTP 200, the expected content and zero serious axe
  violations in ar and en.
- **Negative proof:** a temporary server component with an inline `onClick`, placed on that
  route, returned **500** with "Event handlers cannot be passed to Client Component props".
  The probe was then removed.

## D-050 — Identity on the shared context; customer data on the Identity user — Accepted (Phase 04)
- ASP.NET Core Identity runs on the shared `TrimmeDbContext` (no `IdentityDbContext`). All seven Identity tables are mapped explicitly into the `identity` schema, because the generic Identity types live outside the module assembly and the schema convention would not reach them.
- `IdentityUser` subclasses and `UserManager` live in the module's `Infrastructure` namespace (the architecture rules forbid `Microsoft.AspNetCore.*` in Domain and Application). Use cases in `Application` reach them through the `IAccountStore` port.
- One user table serves all three user types (`user_type` column). Customers have no email and no password; staff have both. `RequireUniqueEmail` is off; staff email uniqueness is a filtered unique index on `normalized_email`.
- The customer mobile is stored **only** encrypted (`protected_phone`, Data Protection purpose `trimme.mobile-number`) plus a keyed HMAC lookup hash with a filtered unique index. Identity's plaintext `PhoneNumber` column is not mapped. Normalisation accepts Saudi mobiles only (`+9665XXXXXXXX`, D-048); Phase 05 replaces the helper with the platform phone value object without changing stored values.
- **CustomerProfile deviation:** the customer's display name, preferred locale and terms-acceptance time live on the Identity user, since every user type needs a name and a locale. The Customers module's `CustomerProfile` (favorites, booking projections) is created in Phase 12, when it has customer-domain data. TRACEABILITY §14 is updated.

## D-051 — Permission catalogue, seed roles and authorization — Accepted (Phase 04)
- The catalogue is code (`Identity/Domain/Permissions.cs`, 56 codes). The `migrate` command synchronises it into `identity.permissions` through `IReferenceDataSynchronizer`; the API never does this on startup. Test hosts run the same synchroniser. `HasData` was rejected because it would fight admin edits of roles in Phase 14.
- **Managed** roles (SuperAdmin, ShopOwner, ShopStaff, Customer) are reset to their code-defined grants on every `migrate`. **Editable** seed roles (OperationsManager, Support) get their defaults once, when created. Grants of obsolete permissions, and any grant across user types, are removed.
- Defaults (refining D-018/D-019):
  - SuperAdmin: every admin and `SuperAdmin.*` permission.
  - OperationsManager: all `Admin.*` except `Admin.Roles.Manage`, `Admin.Staff.Manage` and `Admin.Settings.Edit`.
  - Support: view permissions plus `Admin.Bookings.Intervene`, `Admin.Customers.ViewContact` (reveal-with-reason from Phase 14) and `Admin.Reviews.Flag`.
  - ShopOwner: all `Shop.*`. ShopStaff: bookings, walk-ins, status changes and schedule read.
  - Full table: `docs/permissions-matrix.md`.
- **Default deny:** the `/api/v1` route group requires an authenticated user. Endpoints narrow that with `RequirePermission(code)` or `RequireUserType(type)`, or opt out with `AllowAnonymous()`. A global `FallbackPolicy` was rejected because it also applies to unmatched routes and would turn 404/405 into 401.
- Permissions are resolved from the database per request (one join, cached for the request), so role changes apply on the next request rather than when the access cookie expires. The access cookie carries only `sub`, `sid` and `user_type`.

## D-052 — Cookie sessions, refresh rotation and reuse detection (finalises D-027) — Accepted (Phase 04)
- **Access cookie** `trimme-access`: an ASP.NET Core cookie-authentication ticket, 15 minutes, no sliding, `HttpOnly; Secure; SameSite=Lax; Path=/`. The scheme answers 401/403 problem details and never redirects.
- **Refresh cookie** `trimme-refresh`: an opaque 256-bit token (only its SHA-256 is stored), `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`, so it reaches only the refresh and sign-out endpoints. Session lifetime is absolute and a refresh never extends it: customers 30 days, staff 7 days.
- Each session is one token family. A refresh consumes its token atomically (conditional update) and issues the successor. A consumed token presented again within **10 s** answers 409 `auth.refresh_race` (another tab refreshed, and cookies are shared) and revokes nothing. After that it is treated as theft and **revokes the family**.
- Every authenticated request checks that its session is active and the account enabled (one indexed query in `OnValidatePrincipal`). Sign-out, revoke-one, revoke-others, password reset and reuse detection therefore take effect **immediately**.
- Data Protection keys (cookie tickets, reset tokens, encrypted phones) live in `infra.data_protection_keys`, shared by every API container and surviving restarts. They are stored unencrypted at rest; wrapping them with a certificate is a Phase 17 item.
- Web: Server Components cannot refresh, because the refresh cookie is never sent to page paths. `requireUser` therefore redirects to `/[locale]/auth/session?returnTo=…`, a client page that refreshes once, confirms with `/me` and returns, or else sends the user to the matching sign-in page. Client API calls refresh once on a 401 and retry. `returnTo` accepts only same-origin relative paths.

## D-053 — CSRF on every unsafe API request — Accepted (Phase 04)
- Double-submit token: `GET /api/v1/auth/csrf` sets the readable `trimme-csrf` cookie (`Secure; SameSite=Lax; Path=/`).
- Every unsafe `/api/v1` request, **anonymous ones included** (against sign-in CSRF), must echo it in `X-CSRF-Token`, compared in constant time.
- The check runs after routing and only for endpoints that carry the group's CSRF metadata, so unknown routes keep 404 and wrong methods keep 405. `SkipCsrf()` exists for future signature-verified webhooks.
- Sign-in rotates the token; refresh does not, so a retried request stays valid. The web client fetches the token lazily on its first unsafe call.

## D-054 — Customer OTP rules — Accepted (Phase 04, refines D-037)
- 6 digits, 5-minute expiry, 3 attempts per code (atomic counter), 30-second resend cooldown, and at most 5 codes per number per hour. A newer code supersedes older ones. Codes are stored as a keyed hash bound to the challenge and are never logged. The per-IP `otp` rate-limit policy applies on top.
- Enumeration-safe: requesting a code looks the same for new and existing numbers, and `isNewUser` is revealed only after verification.
- Terms acceptance is captured on sign-up, or at profile completion for customers who started from the sign-in screen.
- Delivery: the `DevInbox` sender (in memory, read through the dev-only `GET /api/v1/dev/otp-inbox/latest`, which is excluded from OpenAPI) in Development and Testing. Startup fails if it is configured anywhere else. Without a channel the API answers 503 `otp.delivery_unavailable` until the WhatsApp adapter lands in Phase 15.

## D-055 — Staff passwords, lockout, reset and invitations — Accepted (Phase 04)
- Policy (NIST style): at least 10 characters and 4 distinct characters, no composition rules. Lockout after 5 failures, for 15 minutes.
- Unknown emails verify a dummy hash, so both paths cost one hash and return the same `auth.invalid_credentials`. A locked account reports `auth.locked_out`; that reveals existence only after five failures on the address, which is accepted.
- Reset: the request always answers 202, even when the email cannot be delivered (the failure is logged without the address). The link carries the user id and a 1-hour Identity token, never logged. A successful reset signs out every session and clears the lockout.
- Accepted residual: for a known address the request waits for the SMTP hand-off, so its timing differs from an unknown address. The `auth` rate limit bounds probing; moving the send to the outbox/Hangfire pipeline (Phase 15) removes the difference.
- Staff accounts exist only through admin invitations: a 7-day single-use token (hash stored), the newest invitation per address wins, and the role must belong to the invited user type. The first SuperAdmin comes from the development bootstrap (R-AUTH-03).

## D-056 — Email channel and production-only secrets — Accepted (Phase 04)
- MailKit SMTP behind `IEmailSender`; emails are localized in Arabic (RTL) and English. Local compose and the integration tests use Mailpit (`axllent/mailpit:v1.31.2`). Links use `Web:PublicBaseUrl`.
- Outside Development and Testing the API refuses to start without `Email:Smtp:Host` and `PersonalData:LookupKey` (base64, at least 32 bytes). The development lookup key is public and only used locally.

## D-057 — Strict JSON number handling — Accepted (Phase 04)
The API uses `JsonNumberHandling.Strict`. The ASP.NET web defaults (numbers readable from strings) made the OpenAPI contract type every number as `integer | string`; strict handling gives the generated TypeScript plain `number`s.

## D-058 — Local stack additions — Accepted (Phase 04)
- Compose gains `mailpit` (web UI on `TRIMME_MAILPIT_PORT`, default 8025) and a one-shot `seed` service (`seed --dev` with `TRIMME_ALLOW_DEV_SEED=true`). The seed creates the bootstrap admin from `TRIMME_BOOTSTRAP_ADMIN_EMAIL`/`_PASSWORD`, whose local defaults are `admin@trimme.local` / `trimme local admin`. The API waits for both.
- In local compose the web container proxies `/api`, so every browser shares one client IP at the API. Compose therefore raises the `otp` and `auth` rate limits for development and E2E only. Production keeps the code defaults and sits behind Nginx with forwarded client IPs.
- Sign-in style forms validate on submit, not on blur: an on-blur error under the autofocused empty field shifted the links below it and made the first click miss (found by E2E).

## D-059 — Tenancy model — Accepted (Phase 05)
- **Marker types** (BuildingBlocks.Domain): `ShopId`, `IShopOwned` (a row of exactly one shop), `ITenantRoot` (the shop), `ITenantMember` (an account or invitation that may belong to one shop).
- **Query filter.** `TrimmeDbContext` applies the named filter `tenant` to every `IShopOwned` entity by convention:
  - a shop user sees only rows of their shop;
  - **everyone else sees none** (anonymous, customers, admins, a suspended shop, background work), unless code opens an explicit admin or system scope (D-062);
  - the shops table itself is a public directory, but a shop user sees only their own row.
- **Lazy evaluation.** The filter reads the tenant at query time, not when the context is built, because the context can be created during authentication, before the user is known.
- **Writes, checked in `SaveChanges`:**
  - `ShopId` is stamped from the tenant on insert;
  - a mismatched `ShopId` is rejected (`TenantViolationException`);
  - an insert without a tenant or scope is rejected;
  - changing `ShopId` is rejected, even inside a scope;
  - an update or delete of another shop's row is rejected;
  - `ITenantMember.ShopId` is immutable too.
- **Database guarantees:**
  - a foreign key to the shop is added by convention to every shop-owned entity and tenant member;
  - children reference parents through `(shop_id, parent_id) → (shop_id, id)` using `HasShopScopedKey`/`HasShopScopedReference`, so the database itself rejects cross-shop references.
- **Model cache.** The EF model cache key includes the set of model contributors, so test-only models never share a cached model with production.
- **Deviation from the phase plan: no `ShopUser` table.** Membership is `identity.users.shop_id` (typed, immutable, FK to `shops.shops`) plus the ShopOwner/ShopStaff role. One column enforces "one shop per shop user" structurally, and the invitation already carried the shop. The composite-key pattern is demonstrated and tested on test-only shop-owned entities, created through the real conventions in a separate database. Production has no `IShopOwned` entity until Phase 06; the architecture tests use probe entities so the rules are not vacuous.

## D-060 — Phone value object — Accepted (Phase 05, refines D-026)
- `PhoneNumber` (BuildingBlocks.Domain) wraps libphonenumber-csharp 9.0.40: E.164 output, Latin, Arabic-Indic and Extended Arabic-Indic digits, validity checks, a mobile-only variant, and masking (`+966 5•• ••• •30`). `ToString()` returns the mask, so a number cannot leak into a log by interpolation.
- It is used for professional WhatsApp numbers from Phase 06.
- Customer sign-in keeps the Phase 04 Saudi-mobile rule (`MobileNumber`), so acceptance does not change and stored lookup hashes stay valid. A test proves both produce byte-identical E.164 values and masks for every number both accept.
- Encryption and lookup hashing stay in `IPersonalDataProtector` (Phase 04).

## D-061 — Shop lifecycle and tenant access — Accepted (Phase 05)
- A shop is created as **Draft** by an admin, then **Active**; an admin can **Suspend** it and activate it again. The same-state transition answers 409 `shop.invalid_transition`. Every change is audited, and a suspension may carry a reason.
- A Draft shop's invited owner can sign in and has the shop as tenant, to set it up; customers do not see Draft shops (Phase 11).
- The users of a suspended shop can still sign in and see the status (`GET /shop/me`), but **have no tenant**: shop-owned data is unreachable from their very next request, because the session validation re-reads the shop's status on every request. Suspension deletes and alters nothing.
- Permissions: create is `Admin.Shops.Create`; activate and suspend are `Admin.Shops.Suspend`; invite owner or staff is `Admin.Shops.ManageAccount`; view is `Admin.Shops.View`.

## D-062 — Explicit tenant bypass — Accepted (Phase 05, R-TEN-05)
- `IAdminDataScope.Begin()` throws unless the caller is a PlatformAdmin. Architecture rule: only types in `*.Application.Admin` namespaces may depend on it.
- `ISystemDataScope.Begin()` throws inside an HTTP request. It is for host commands (`migrate`, `seed`) and future jobs, and only `*.Seeding`, `*.Jobs` and `Trimme.Api.Hosting` may depend on it.
- `IgnoreQueryFilters` is banned everywhere; an IL scan with Mono.Cecil enforces it. Each rule is checked against a deliberately violating probe type, so it cannot pass vacuously.
- Nothing in production opens a scope yet. The first admin use cases on shop-owned data arrive in Phase 06.

## D-063 — Audit trail, paging and demo data — Accepted (Phase 05)
- **Audit.**
  - `IAuditLog.Record` adds an `AuditEntry` (`administration.audit_entries`) to the current unit of work, so an entry commits atomically with the change it describes.
  - An entry records the actor (user id and type, or `System`), action code, entity, optional shop, a PII-free summary, the reason and the correlation id.
  - It never contains emails, phones or names; invitations are audited by invitation id.
  - The trail is append-only: no update or delete path exists. The read UI arrives in Phase 14.
- **Paging.** `PageRequest` clamps page ≥ 1 and size 1–100 (default 20). List endpoints return `PagedResponse<T>` (`items`, `page`, `pageSize`, `total`).
- **Demo data.**
  - `DemoData` fixes the ids and slugs of two Riyadh demo shops (Al Asala, Barber House), each with an owner and a staff account.
  - Account ids are derived from the email, so seeding is deterministic across machines.
  - The password comes from `TRIMME_DEMO_PASSWORD` (local default documented in `docs/local`).
  - Development only, through `seed --dev`.

## D-064 — Images are stored in PostgreSQL — Accepted (user, Session 4, Phase 06)
The user asked in Session 4 to store media such as photos in the database. This replaces the phase plan's "local storage adapter in dev" (`.data/uploads`).
- **Table.** `media.media_files` holds id, purpose, content type, width, height, size, SHA-256 and the bytes (`bytea`). It sits in the building blocks next to the Data Protection keys, so the module count stays at twelve (D-001).
- **No shop id on the row.** Ownership comes from the aggregate that references the image (shop logo/cover/gallery, professional avatar). An upload creates the image and its reference in one command and one `SaveChanges`. Clients never attach an existing media id, so one shop cannot point at another shop's image. Replacing or removing an image deletes the old row in the same unit of work.
- **Validation by content.** Only JPEG, PNG and WebP are accepted, recognised from their bytes; the declared type and file name are ignored. SVG, HTML, GIF and malformed files are rejected. Other limits:
  - at most 5 MB, enforced by a per-endpoint body limit;
  - at most 8000 px per side and 40 MP;
  - a minimum size per purpose.
- **Metadata stripped before storage:** EXIF (including GPS), XMP, IPTC, comments and PNG text chunks. Pixels are never decoded or re-encoded, so no imaging library (and no licence) is needed.
- **Serving.** `GET /api/v1/media/{id}` is anonymous (images are public page content). It sends the sniffed type, `nosniff`, the strict CSP, an ETag (304 supported) and `Cache-Control: public, max-age=31536000, immutable`. That is safe because bytes never change: a new upload always gets a new id.
- **Upload endpoints** use `.AcceptsImageUpload()`: ASP.NET antiforgery is off, because the API's own CSRF check (D-053) still applies. The body limit is raised for that endpoint only.
- **Trade-offs accepted:** database size and backup volume grow with images, and there is no CDN. Nginx or a CDN can cache the immutable URLs in production (Phase 17). Moving the bytes to object storage later only changes `IMediaStore` and the serving endpoint.

## D-065 — Shop profile, edit policy and location on the shop row — Accepted (Phase 06)
- **Profile.** Localized description, category (`Barbershop`/`Salon`/`Unisex`), the shop's own public phone (E.164, business data), amenities, admin-only `IsVerified`, logo, cover and an ordered gallery of up to 12 images (a `uuid[]` column).
- **Edit policy (DV-S16).** `EditableFields` lists what the shop may change: Name, Description, Category, PublicPhone, Amenities, Logo, Cover, Gallery, Location.
  - A new shop may edit Description, PublicPhone, Amenities, Logo, Cover and Gallery. Name, Category and Location stay locked until an admin opens them.
  - Verification and the slug are never shop-editable.
  - The shop sends the whole form. The server rejects (403 `shop.profile_field_locked`, with the `fields`) only a locked field whose value actually changes.
  - Image and location endpoints check the same policy. Location also needs `Shop.Location.Edit`.
- **Location.** An owned value on `shops.shops`: `location geography(Point,4326)` with a GiST index, plus address line, district, city, formatted address, source (Manual/Geocoded/Device), and confirmed at/by.
  - Deviation from the phase plan's separate `shops.shop_locations` table: the shop is the public-directory root, so Phase 11 nearby search needs no tenant scope and no join.
  - X = longitude, Y = latitude, rounded to 6 decimal places.
- **Concurrency.** Admin and shop profile edits are optimistic (`xmin`). A stale version answers 409 `resource.concurrency_conflict`, which the exception handler maps from `DbUpdateConcurrencyException`.
- **Suspended shops.** Shop self-service reads and writes resolve the shop from `ICurrentTenant`, never from the session claim, so they answer 404 while the shop is suspended (guard test).

## D-066 — Public read scope — Accepted (Phase 06)
`IPublicDataScope.Begin(shopId?)` is a read-only view for anonymous and customer pages.
- While it is open, every shop row is visible (the directory), shop-owned rows only of the given shop (none when null), and the caller's own tenant is ignored. A signed-in owner of shop B therefore sees shop A's public page exactly as a visitor does.
- `SaveChanges` throws while the scope is open.
- Handlers still filter what is published: active shops and active professionals only.
- Architecture rule plus probe: only `*.Application.Public` namespaces may depend on it.
- Anonymous endpoints (reviewed allow-list): `GET /public/shops/{slug}`, `GET /public/shops/{slug}/professionals`, `GET /media/{id}`.

## D-067 — Professionals and their WhatsApp contact — Accepted (Phase 06)
- **Professional** (`professionals.professionals`, `IShopOwned`, `(shop_id, id)` key):
  - localized name, specialty and bio, a slug unique per shop (derived from the English name when omitted), an avatar, Active/Disabled status and optimistic concurrency;
  - `ShopId` is set by the factory only. No other public member takes a shop, no update contract carries one, and EF refuses to modify it because it is part of the alternate key (D-011, R-NEG-01).
- **ProfessionalContact** (`professional_contacts`, keyed by professional, composite FK `(shop_id, professional_id)`):
  - the E.164 number encrypted with Data Protection purpose `trimme.professional-whatsapp`;
  - a keyed lookup hash, unique platform-wide, so the same person cannot be listed again in another shop (R-NEG-05);
  - a stored mask for lists;
  - the notification toggle, which can only be on while a number is set, and a verification state (Unverified until Phase 15 reports delivery). Changing the number resets verification.
- **Reveal.** `POST .../whatsapp/reveal` needs `Admin.Professionals.RevealWhatsApp` and a reason of at least 5 characters. It is audited with the reason and returns `no-store`. Support can view professionals but cannot reveal.
- **Toggle only.** `PUT .../whatsapp` with `keepCurrentNumber` changes just the toggle, so no admin must see the number to switch notifications.
- **Audit.** Entries never contain the number, only what happened ("number changed", "notifications off").
- **Shop view.** `GET /shop/professionals` is read-only for the tenant, with no contact data. Shops cannot create, edit or disable professionals (403).

## D-068 — Map and geocoding adapters (implements D-007) — Accepted (Phase 06)
- **API port `IGeocoder`**, proxied at `GET /admin/geo/{search|reverse}` (`Admin.Shops.Edit`) and `GET /shop/geo/{search|reverse}` (`Shop.Location.Edit`). Rate limit policy `geocode` is 30 per minute per user.
- **Adapters:**
  - `Nominatim`: `Geocoding:Provider=Nominatim`, identifying User-Agent, requests serialized and spaced ≥ 1.1 s, results cached 24 h, failures degrade to "no result". It is the default for `dotnet run` in Development.
  - `Fake`: a gazetteer of seven Riyadh districts with real coordinates. It is the default everywhere else, including compose, tests and CI, so no automated run calls a third party. Set `TRIMME_GEOCODER=Nominatim` in compose for real lookups.
- **Web map.** MapLibre GL 6.11.2 with raster tiles from `NEXT_PUBLIC_MAP_TILE_URL` (default: OpenStreetMap's public tiles, with attribution, light development use only). It sits behind a `MapView` contract, loads only when the picker mounts, and uses a DOM marker that can be dragged.
  - When WebGL is missing, the picker falls back to typed coordinate and address fields. Those fields are also its keyboard alternative.
  - Geolocation is requested only when the user presses "use my current location".
- **Production** needs a self-hosted or commercial OSM-based tile host and geocoder (D-007). That choice is configuration, confirmed at Phase 17/18.

## D-069 — Phase 06 test infrastructure and gates — Accepted (Phase 06)
- **Test PostgreSQL connections.** The Testcontainers PostgreSQL runs with `max_connections=400`. Every test database has its own Npgsql pool, and the Phase 06 suites exhausted the default 100 ("too many clients").
- **Authorization matrix.** The endpoint matrix sends multipart requests to multipart endpoints. Those match only multipart at routing, so a JSON probe got 415 before authorization.
- **No-transfer grep gate.** Tests assert absence, so the gate excludes test files: `rg -i "transfer(Professional|Barber|Shop)|(professional|barber)\s*transfer|نقل حلاق|تنفيذ النقل" apps src -g '!**/*.test.*' -g '!**/node_modules/**'`. The unrefined gate already matched a Phase 05 test. Product code and comments avoid the term altogether (D-011).
- **E2E.** Map tiles are stubbed with a blank PNG, and the pin is dragged only after it stops moving.
- **Oversized uploads through the Next.js rewrite (local compose only).** A body over the limit gets a bare 500 from the rewrite rather than the API's 413, because the API rejects it before reading the body. Production routes `/api` through Nginx, which needs `client_max_body_size 6m` (Phase 17), and the upload control refuses files over 5 MB in the browser. Accepted for development.

## D-070 — Localized text of catalogue items — Accepted (Phase 07)
- **Shop services and packages:** the Arabic name is required and English is optional. The API returns `nameEn: null` rather than copying the Arabic, and every screen uses one web helper, `localizedName`, to fall back to the Arabic name in the English UI. Descriptions are optional in both languages.
- **Platform categories:** both languages are required, because admins manage a short list.
- This closes the Phase 07 risk "English-required policy for localized fields".

## D-071 — Shop services: rules and lifecycle — Accepted (Phase 07)
- **Ownership.** A service belongs to one shop (`IShopOwned`, `(shop_id, id)` key). The shop sets its own names, price and duration, and nothing global overrides them (spec §10, DV-S02/S03).
- **Price and currency.**
  - The price is `numeric(10,2)`, from 0 to 100,000. More than two decimals is rejected, never rounded; 0 is allowed (a free consultation).
  - The currency (`SAR`) is stored on the row so bookings can snapshot it (Phase 10).
  - The web input accepts Arabic-Indic digits and the Arabic decimal separator, typed left-to-right.
- **Duration** is a multiple of 5 minutes, from 5 to 480 minutes. A database CHECK constraint backs it up.
- **Lifecycle.**
  - Active/inactive is an idempotent toggle.
  - Archive is final: the item is never offered again but stays in history. An archived item cannot be edited or re-activated (409 `catalog.archived`).
  - Delete is allowed only when nothing uses the service. Every `IShopServiceUsage` is asked: package items now, bookings from Phase 10. Otherwise the answer is 409 `service.in_use`.
- **Order.**
  - `PUT /shop/services/order` must carry the full set of the shop's non-archived ids. A missing, foreign, archived or repeated id is refused and nothing changes.
  - Concurrent reorders are last-write-wins.
  - Reordering in the UI uses keyboard move-up/down buttons with an `aria-live` announcement. Drag-and-drop is deferred.
- **Concurrency and tenancy.**
  - Edits are optimistic (`xmin`, 409 on a stale version).
  - Every shop handler checks `ICurrentTenant` first, so a suspended shop gets 404, never a 500 from the stamping rule.
- **Online booking** is a per-service rule (`OnlineBookable`); walk-ins are always possible.

## D-072 — Packages (implements D-020) — Accepted (Phase 07)
- A package is shop-owned, with 2–10 distinct, non-archived services of the same shop, an explicit price and an explicit total duration (not the sum of its items).
- Items reference the package and the service through composite `(shop_id, …)` keys, so the database rejects another shop's service.
- **Item edits:**
  - Items keep their rows when reordered, because a key cannot be deleted and re-added in one save.
  - Any item change touches the package's `UpdatedAt`, so an items-only edit still runs the optimistic-concurrency check (tested: the second concurrent edit gets 409).
- **Item availability.** A service can be switched off, archived or hidden while a package uses it. The package then reports `isBookable: false` to the shop and is left out of the public list until every item is available again.
- `ExpandItems()` gives the ordered services for reporting and availability. The reporting part lands with bookings.
- Package moderation uses `Admin.ShopServices.Moderate`. `Admin.Packages.Manage` stays reserved for platform package templates, which v1 does not have.

## D-073 — Professional–service assignment — Accepted (Phase 07)
- **Table.** Assignments live in `services.professional_services`, owned by the Services module. This deviates from the phase plan's `professionals.professional_services`, because availability (Phase 09/10) asks the Services side which professionals do a service.
- **Foreign keys.** Both FKs are composite:
  - `(shop_id, service_id)` → `shop_services(shop_id, id)`;
  - `(shop_id, professional_id)` → `professionals(shop_id, id)`.
  The database therefore guarantees same-shop pairs.
- **No project reference to Professionals.** The professional FK names the entity type as a string (`HasShopScopedReference(entityTypeName, …)`), so modules keep talking only through contracts. `ProfessionalId` moved to the Domain building block, next to `ShopId`, so both sides share the key type.
  - An architecture test asserts that the name resolves to the real `Professional` entity (not a shared-type placeholder) and that the FK is the composite one.
  - The Professionals module is configured before Services (`ModuleCatalog` order), and the helper throws if not.
- **Who assigns.** Only admins, with `Admin.Professionals.AssignServices`. The API refuses another shop's services (400) and shop users get 403 (R-NEG-06). The admin reads the professional through `IProfessionalDirectory`.

## D-074 — Admin moderation and support override — Accepted (Phase 07)
- **Moderation** (`Admin.ShopServices.Moderate`): hide needs a reason of at least 5 characters, and unhide does not.
  - Hidden items are never published.
  - The shop sees the state and the reason, and can still edit, but cannot unhide.
  - Both actions are audited.
- **Support override** (`Admin.ShopServices.SupportOverride`) corrects one service of one shop:
  - it needs a reason and uses optimistic concurrency;
  - the audit summary lists what changed, for example "Price 60.00 → 55.00 SAR; Duration 30 → 25 min; Name changed";
  - there is no bulk or cross-shop edit and no global price.
- **Categories** (`Admin.ServiceCategories.Manage`, listed with `Admin.ShopServices.View`) are platform-owned, with no price or duration.
  - They are deactivated, never deleted. Existing services keep an inactive category, but it cannot be chosen again.
  - Active categories are published anonymously at `GET /public/service-categories`.

## D-075 — Web server keep-alive timeout — Accepted (Phase 07)
- The web image sets `KEEP_ALIVE_TIMEOUT=65000`, read by the Next.js standalone server. Node's default of 5 s closed idle sockets just as clients reused them, which gave intermittent "socket hang up" failures through the `/api` rewrite in E2E.
- **Production rule:** the upstream's keep-alive timeout must stay above the idle timeout of whatever proxies it (Nginx `keepalive_timeout` or `upstream keepalive`, and load balancers at about 60 s), so the server never closes first. Recorded for the Nginx example in Phase 17.

## D-076 — Platform settings — Accepted (Phase 08)
- **One typed row** in `administration.platform_settings` (a CHECK pins the single id; range CHECKs mirror the validator). Optimistic concurrency uses `xmin`, and each edit is audited as `platform_settings.updated` with the changed field names, for example "Changed: expiringSoonThresholdDays, expiredSubscriptionEnforcement".
- **Defaults** (spec §6, §12, §15, §16; D-013…D-017):
  - lead time 60 min, horizon 30 days, slot step 5 min (5/10/15/20/30/60), cancellation cutoff 120 min, review window 7 days;
  - reminder offset 30 min, expiring-soon threshold 14 days, enforcement `HideAndBlockNewOnlineBookings`, hide paused shops true;
  - locale `ar`, currency `SAR`, time zone `Asia/Riyadh`, country `SA`, map centre 24.7136 / 46.6753, zoom 11.
- **Who writes it.** The `migrate` command's `PlatformSettingsSynchronizer` inserts the defaults only when the row is missing and never overwrites an edit (tested by re-running migrate).
- **Access.** `Admin.Settings.View` reads and `Admin.Settings.Edit` writes (Operations Manager views only). Other modules read it through `IPlatformSettings` (building-block contract, cached per request scope).
- **Fixed in v1:** locale, currency, time zone and country are shown read-only. Changing them would re-date subscriptions and re-price catalogues, so it needs its own migration plan.

## D-077 — Subscription periods and dates — Accepted (Phase 08)
- **Calendar.** Subscription dates are days in the **platform** time zone (`IPlatformSettings.TimeZone`, Asia/Riyadh), not per shop. Subscriptions are platform-level commercial records, and one calendar lets the admin list filter statuses in SQL. All v1 shops are in Riyadh. The phase plan said "shop time zone"; this is the recorded deviation.
- **End date** = start + interval − 1 day, inclusive (design a-subs: 1 Oct 2026 + 1 year → 30 Sep 2027). When the target month lacks the start day (31 Jan + 1 month), the period runs to that month's end (28/29 Feb), so a renewal chain never loses a day. An explicit `durationDays` gives start + days − 1. The web preview mirrors this and is unit-tested against the same cases.
- **No future gaps.**
  - An activation may be back-dated but cannot start in the future.
  - A renewal starts the day after the current end, or, after a lapse, on any day from then up to today (default: today).
  - So "today" is always inside a period or after the last one, and the status depends only on the stored `EndDate` (plus the suspended flag).
- **Status:** None, then Suspended, Expired (today > end), ExpiringSoon (days left ≤ threshold, counting the end day), otherwise Active.
- **Plans usable:**
  - An activation needs a Published plan that is available to new shops.
  - A renewal may also use an Inactive (no longer offered) plan, so existing shops can stay on it; never a Draft or Archived plan.
- **Override** (SuperAdmin, reason ≥ 5): it changes the price and/or end of the period **in force** (or the latest one after a lapse). Only the latest period's end can move. The previous values are kept in `subscription_overrides`.
- **Stored only in v1:** plan limits (max professionals/services), trial days and grace days are plan data, but nothing enforces them yet. Grace does not delay `Expired`. They are shown with that note in the editor.
- **Recorded amount.** A standard period records the price version in force on its start date. Custom durations and back-dated starts are decided by the user in **D-081**.
- **Concurrency.** Renew, override, suspend, reinstate and price-add carry the version. Two concurrent activations are stopped by the unique `shop_subscriptions.shop_id` (409, see D-080).

## D-078 — Coverage read model and the bookability gate — Accepted (Phase 08; default confirmed by the user)
- `ShopSubscription`, its periods and overrides are `IShopOwned` (tenant-filtered; commercial data). A shop reads only its own subscription at `GET /shop/subscription`, without the override reasons.
- **Coverage row.** `subscriptions.subscription_coverage` (shop id, end date, suspended) is a platform read model with no prices. It is written in the same unit of work as every subscription change, and it is deliberately not shop-owned (it is on the reviewed allow-list of the tenancy architecture test). This lets discovery (Phase 11, including SQL joins) and booking creation (Phase 10) read any shop's status without a tenant scope.
- **`IShopBookability`** (building-block contract, implemented by Subscriptions) returns `AcceptsOnlineBookings`, `VisibleInDiscovery` and a reason code:
  - a shop that is not Active is always blocked (`shop.not_active`);
  - with enforcement `HideAndBlockNewOnlineBookings`, **no subscription** (`subscription.none`), `Expired` and `Suspended` are blocked, while `ExpiringSoon` still books;
  - with enforcement `None`, the status is informational.
- **Confirmed by the user (Session 4).** Keep the default: a shop without a subscription in force is hidden and takes no online bookings. `ExpiredSubscriptionEnforcement = None` stays available as a temporary, warning-only rollout setting.
- **Consequence for later phases.** A shop must have a subscription in force to appear in discovery. Test and E2E setup in Phases 10–12 must assign one (the demo shops have one). This is an overridable default: switching the enforcement setting to `None` removes it.
- Existing and future bookings are never changed, and walk-ins do not ask the gate (asserted again in Phase 10).

## D-079 — Plans and versioned prices — Accepted (Phase 08)
- **Management.** Plans are managed only with `SuperAdmin.SubscriptionPlans.Manage`. `Admin.Subscriptions.View` can read them (to pick a plan when assigning or renewing).
- **Plan data:** localized name and description, up to 12 bilingual feature lines (stored as JSON), billing unit Month (1–36) or Day (1–1095), optional limits, trial and grace days, available-to-new-shops, display order, status (Draft, Published, Inactive, Archived; archive is final and also closes the plan to new shops).
- **Prices** (`plan_prices`) are append-only versions:
  - the amount is 0–1,000,000 with at most 2 decimals, in the platform currency;
  - the effective date is today or later;
  - there is one version per date, and version numbers are sequential (both enforced by unique indexes);
  - "current" is the latest version that started on or before a date.
- **Periods.** A period stores the price version id, amount, currency and plan name at recording time, and never recomputes them (R-SUB-02).
- **Publishing** needs at least one price version (a scheduled one is enough).
- **No payment UI:** amounts are recorded values only (spec §15).

## D-080 — Unique-index races answer 409 — Accepted (Phase 08)
The global exception handler maps a PostgreSQL unique violation (`23505`) raised on save to **409 `resource.conflict`** instead of 500. Handlers still pre-check (for example `subscription.already_assigned`); the mapping covers only the race the pre-check cannot see (two concurrent activations, two price versions for the same date). It is tested by a concurrent-assign integration test.

## D-081 — Custom durations and back-dated starts need a SuperAdmin override — Accepted (user decision, Phase 08)
- **Standard period:** it starts today or later (platform calendar) and lasts exactly the plan's interval. It records the plan price version in force on its start date (D-079). Admins with `Admin.Subscriptions.Assign`/`Renew` record these.
- **Custom period:** a custom number of days, a start before today (including a lapsed renewal restarted in the past), or an explicit price.
  - It needs `SuperAdmin.Subscriptions.Override`; anyone else gets **403 `subscription.custom_pricing_required`**.
  - It needs an **explicit total price** (0–1,000,000, at most 2 decimals) and a **reason** (≥ 5 characters). Otherwise the answer is 400 with field errors.
  - The period stores the explicit total as its amount, the plan price it replaces (`standard_amount`, null when the plan had no price on the start date), the reason (`pricing_reason`), the price version in force for reference, and `is_overridden`.
- **Audit and history.**
  - The `subscription.assigned` or `subscription.renewed` entry says "SuperAdmin custom period, total X instead of Y" and carries the reason.
  - The admin history shows the custom price with its reason and the plan price. Nothing is deleted or rewritten.
- **Enforcement.** The domain refuses a non-standard period without custom pricing, so no caller can bypass the rule. The API checks the permission. The web form offers custom lengths and past dates only to SuperAdmin.
- **Seed.** Demo history is recorded as of each period's own start date (standard at the time), not as back-dated overrides.
- **Tests.** Unit `Assign_CannotStartInTheFuture_AndABackdatedStartNeedsCustomPricing`, `Renew_ContinuesTheDayAfter_…`; integration `NonSuperAdmin_CannotManagePlans_OrOverride` (Operations Manager: long custom duration, back-dated start, explicit price and plain custom days → 403) and `Assign_Renew_Override_Suspend_…` (price/reason required, explicit totals and standard amounts, audit text); web form tests; E2E flow 5.

## D-082 — Schedules and the availability engine — Accepted (Phase 09)
- **Model** (schema `availability`, every table `IShopOwned` with an `xmin` version):
  - `shop_opening_hours`: one row per shop. The week's intervals are a JSON list, edited as a whole week, so one version check covers the week. The phase plan named a row per interval; nothing queries single intervals in SQL. No row means the shop has not set its hours, so it is closed.
  - `professional_working_hours`: one row per professional, with a composite FK `(shop_id, professional_id)`. `follows_shop_hours` (the default when there is no row) means they work whenever the shop is open.
  - `shop_closures`: whole local days, inclusive.
  - `breaks`: a label; weekly on some weekdays or once on a date (a CHECK allows exactly one); within one day, on 5-minute steps. `professional_id` null means everyone (the design's shop-wide prayer breaks), otherwise a composite FK.
  - `professional_time_off`: UTC instants plus kind (Vacation/Sick/Other) and `all_day`. An all-day entry runs from the local start of its first day to the local start of the day after its last.
- **Intervals.** Minutes from the weekday's local midnight, on 5-minute steps. The start is 00:00–23:55; the end is after the start and at most 24 h later, so it may pass midnight (21:00–02:00 = 1260–1560). No two intervals of the week overlap, counting the part after midnight and Saturday night running into Sunday.
- **Engine** (`AvailabilityEngine`, pure, clock injected):
  - A professional is free where the shop is open (opening windows of business days that are not closed) ∩ they work (own hours, or the shop's), minus breaks, time off and existing bookings.
  - A slot needs the whole item `[start, start + duration)` free.
  - Starts sit on the slot-step grid counted from local midnight (5/10/15/20/30/60).
  - **Instants, not labels.** Everything is computed on UTC instants. A local time skipped by DST resolves to the first instant after the gap, and a repeated local time to its first occurrence; slots step in UTC and their labels come from the instant (tested with New York spring and autumn).
  - **Midnight.** A closure closes the *business day*: every window that opens on the closed date, including its hours after midnight. The previous day's window running into a closed date is not affected. Slots are dated by their local start (the date strip).
  - **Edges.** Bookable dates are today … today + horizon − 1, and a slot starts at or after now + lead time (both exact, tested).
  - **Periods** (for grouping, D-009): Morning 05:00–11:59, Afternoon 12:00–16:59, Evening otherwise (including after midnight).
  - `IsBookable(shop, professional, start, duration, now, policy)` applies the same rules to one start. It is the recheck Phase 10 runs inside the booking transaction. It includes the online policy (lead time, horizon, grid), so walk-ins need a collision-only check alongside it (Phase 10).
- **Any professional (refines D-012).** A slot carries every eligible professional free for the whole item. Eligible = active and assigned to the service; for a package, assigned to every item service. The tie-break stays in Phase 10.
- **Limits.** A zone whose DST shift is not a whole multiple of the step (for example 30 minutes with a 60-minute step) would shift the grid after the change. v1 shops are in Riyadh, which has no DST.

## D-083 — Pausing online booking — Accepted (Phase 09, applies D-013)
- The pause is its own row, `shops.online_booking_pauses` (shop id as the key, `paused_at`, `reason`); the row exists while the shop is paused, and `ShopSummary` exposes it. It is not tenant-filtered: the bookability gate and discovery read it for any shop. It is on the reviewed tenancy allow-list, next to the coverage read model, and only the shop's own pause and resume commands write it. `POST /shop/online-booking/pause|resume` needs `Shop.OnlineBooking.Pause` and is audited (`shop.online_booking_paused|resumed`). Pausing twice (or resuming a live shop) answers 409, so the original time is kept.
- **One gate.** `IShopBookability` now also answers `shop.paused`: `AcceptsOnlineBookings = false`, and `VisibleInDiscovery = !HidePausedShopsFromDiscovery`. A subscription block wins over the pause, so an expired shop stays hidden even if it is also paused. Existing bookings and walk-ins are never affected.
- The public shop page and catalogue stay reachable while paused (D-013). The availability API answers 200 with `bookable: false` and the reason.
- **Why its own row.** The first version stored the pause on the shop row. Then pausing changed the shop's `xmin`, so a profile form open in another tab failed to save (409). The E2E suite caught it when the schedule flow and the profile flow ran in parallel. Now pause and resume never touch the shop row (an integration test asserts the profile version is unchanged), and a form never blocks an emergency pause.
- The design has no confirmation or note. The web asks for confirmation with an optional note, because a mistaken tap would hide the shop (DV-A12).

## D-084 — Availability contracts and API shape — Accepted (Phase 09)
- **Building-block ports.** Availability references no other module.
  - `IBookableOfferCatalog` (Services): the duration, whether it is online-bookable, and the eligible professionals of a published service or package (a package is online-bookable when every item is).
  - `IBookedTimeReader` (Bookings, from Phase 10): busy intervals, and upcoming appointments for the conflict preview. The appointment shape has a customer name and the item name, and no phone field by construction. Until Phase 10 an empty reader is registered (`TryAdd`); Bookings replaces it.
  - `IProfessionalDirectory.ListByShopAsync`.
  - `ShopSummary` gains `TimeZone` and `OnlineBookingPausedAt`.
- **Shop API** (the paths differ from the phase plan: breaks can be shop-wide, so they are not under one professional):
  - `GET /shop/schedule` (`Shop.Schedule.Read`; staff read it, and the nav entry now needs Read);
  - `PUT /shop/schedule/opening-hours`, `PUT /shop/professionals/{id}/working-hours`;
  - `POST|PUT|DELETE /shop/schedule/closures|breaks|time-off`, and `POST …/preview` for each (all `Shop.Schedule.Manage`).
  - The professional id is resolved through the tenant-filtered directory, so another shop's is 404.
  - Opening and working hours are created by the first save (version null) and version-checked afterwards.
  - Time off keeps its professional (edit → 400 `validation.immutable`; delete and re-add instead).
- **Conflict preview.** It returns upcoming appointments overlapping the change: time off; a break's occurrences within the horizon; a closure's opening windows. Nothing is saved, cancelled or messaged (DV-S22). The web requires a second "save anyway" when some exist. Persisting a "flagged" state on bookings is a Phase 10 carry-over.
- **Public API** (anonymous, rate limit `availability`):
  - `GET /public/shops/{slug}/availability/dates?serviceId|packageId&professionalId&from&to` gives the slot count per date (default 14 days, at most 31);
  - `GET …/slots?…&date=` gives the starts with local time, period and candidate professional ids.
  - **Gate order:** active shop (else 404) → `IShopBookability`, including the pause (else 200 `bookable:false` + reason) → published item (404 `availability.offer_not_found`), online-bookable (422) → the chosen professional is active in the shop (404) and assigned (422 `availability.professional_not_eligible`).
  - All reads use the public scope for that one shop, so a signed-in shop user sees another shop like anyone else. Schedules and busy times are loaded once for the whole range.

## D-085 — Customer-owned rows (data-layer isolation for customers) — Accepted (Phase 10)
- **Marker.** `ICustomerOwned : IShopOwned` (`Guid? CustomerId`). `ICurrentCustomer` is read lazily from the session claims, like the tenant.
- **Read filter.** For customer-owned types, the named `tenant` filter gains one branch: the signed-in customer sees their own rows. It is one named filter, because named filters are combined with AND. Everyone else sees exactly what the shop rules allow.
- **Write rule** (`EnforceCustomerOwned`, in `SaveChanges`):
  - `CustomerId` never changes;
  - a customer with no tenant or scope may insert or change a customer-owned row only when it is theirs, and the shop id must be set explicitly;
  - anything else is left to the shop rules.
- **Recheck scope.** A customer's create and reschedule read the booked shop's schedule and bookings through the read-only public scope for that one shop, so the recheck sees every booking of the professional. The scope closes before saving, which throws inside it. The architecture rule that limits `IPublicDataScope` now allows `*.Application.Customer` as well as `*.Application.Public`.
- **Why not a bypass scope.** An unrestricted "customer scope" would depend on every handler remembering to filter by customer. The data layer enforces it instead (spec §7), and a raw attempt to change another customer's booking throws (integration test).

## D-086 — Booking model — Accepted (Phase 10)
- **Row.** A `Booking` (schema `bookings`) is shop-owned and customer-owned. It holds:
  - one concrete professional (composite FK);
  - the service or the package (exactly one, CHECK), with same-shop composite FKs added by SQL, because the snapshot keeps plain ids;
  - the snapshot: item name ar/en, price, currency, duration, package items (JSON), and the professional's and customer's names;
  - start/end as UTC instants (any client offset is normalized) and a generated `during tstzrange` column;
  - status, channel (Online/WalkIn), the customer's note, the cancellation reason;
  - the payment seam: `PaymentStatus = NotApplicable` and `AmountDue` = the snapshotted price (R-BKG-10);
  - a reference: 8 unambiguous characters, unique;
  - an `xmin` version.
- **History** is an owned collection in `booking_history`: kind (Created/StatusChanged/Rescheduled), from, to, previous start, actor id and type, reason, time. Being owned, it saves with the booking and needs no shop-owned write of its own.
- **Notes** (`booking_notes`) are shop-owned and never shown to the customer.
- **Names.** The customer's name is snapshotted from `ICustomerDirectory` (name only; Identity keeps the mobile). A customer with no name is refused (422 `booking.profile_incomplete`). A walk-in has a typed name and no phone field in v1.
- **Deletion guard.** A service in any booking, or inside a booked package, is in use (`IShopServiceUsage`): archive it, never delete it (R-SVC-02).

## D-087 — State machine, time rules and customer policy — Accepted (Phase 10; applies D-006, D-015, D-016, D-035)
- **Transitions.** Exactly D-016 (unit-tested for all 49 pairs). The shop moves Pending→Confirmed, Confirmed→Arrived/NoShow, Arrived→Completed, and Pending/Confirmed→CancelledByShop. Only the customer's own command cancels as the customer. Anything else is 409 `booking.invalid_transition`.
- **Time rules:**
  - Arrived from 60 minutes before the start;
  - NoShow only once the start has passed;
  - CancelledByShop needs a reason (≥ 3 characters);
  - responses carry `allowedTransitions` (DV-S08).
- **Initial status.** Online bookings are Confirmed, or Pending when the shop requires manual confirmation (D-006). A walk-in that starts now is Arrived; a later one is Confirmed (D-035).
- **Customer policy:**
  - cancel and reschedule are allowed until `CancellationCutoffMinutes` before the start (exact boundary, tested), else 422 `booking.cancellation_cutoff_passed`;
  - reschedule keeps the snapshot and the status and needs an offered slot;
  - while the shop is paused or its subscription is not in force, reschedule is refused (422 `booking.shop_not_accepting`) but cancel still works.
- **Admins** cancel on the shop's behalf (CancelledByShop, actor PlatformAdmin, reason), audited.

## D-088 — The recheck, walk-ins and "any professional" — Accepted (Phase 10; applies D-012)
- **`IAvailabilityChecker`** (implemented by Availability) answers which candidates can take an exact time.
  - `Online` is Phase 09's `IsBookable`: lead time, horizon, the slot grid and every collision.
  - `WalkIn` is `AvailabilityEngine.IsFree`: the same collisions (hours ∩ working hours, breaks, time off, closures, bookings) at any minute, including "now".
  - The booking being rescheduled is ignored (`BusyTime` now carries the booking id).
  - `OutsideScheduleAsync` flags active bookings that no longer fit the schedule. The flag is computed at read time, never stored (DV-S22).
- **Any professional.** Among the free eligible professionals, the one with the fewest active bookings that local day wins, then the stable id order. The booking stores that professional. If a concurrent booking takes the time first, the answer is 409 (no automatic retry with the next candidate in v1).
- **Gates.** Online create checks the shop is active, `IShopBookability` (subscription and pause), the published offer, online-bookable, an active and eligible professional, then the recheck. Walk-ins skip the bookability gate (D-014) but use the same collisions.

## D-089 — Integrity under concurrency: transaction, exclusion constraint, idempotency, outbox — Accepted (Phase 10)
- **One transaction per command:**
  1. claim the idempotency key and save;
  2. recheck (public scope for customers);
  3. insert or update the booking with its history and outbox row;
  4. save and commit.
- **Exclusion constraint.** `ex_bookings_professional_overlap EXCLUDE USING gist (professional_id WITH =, during WITH &&) WHERE status IN ('Pending','Confirmed','Arrived')`, added by SQL in the `Bookings` migration. Cancelled, completed and no-show bookings free the time.
- **Lost races are 409 `booking.slot_unavailable`.** That covers an exclusion violation (23P01) and a deadlock that PostgreSQL breaks between concurrent overlapping inserts (40P01). Both mean the request lost to a conflicting write, and EF's transient wrapping is unwrapped. The global handler also maps both to 409 as a fallback.
- **Idempotency** (`infra.idempotency_records`, key = user + scope + key, 24 h):
  - the `Idempotency-Key` header is required for customer create and reschedule, and honoured for walk-ins;
  - the same key with the same request replays the resulting booking (`Idempotent-Replayed: true`);
  - the same key with another request is 422 `idempotency.key_reused`;
  - a concurrent same-key request waits on the key, then replays (tested with 4 parallel requests: one booking);
  - a failed command rolls its claim back, so the key can be retried.
- **Outbox** (`infra.outbox_messages`). `booking.created`, `booking.rescheduled`, `booking.cancelled` and `booking.status_changed` are written in the same transaction. Payloads hold ids, times, status, channel and actor type only: no names, no phone numbers. The processor is Phase 15. A losing or refused command writes no row (tested).

## D-090 — A public scope for many shops at once — Accepted (Phase 11)
- **Why.** Discovery reads published offers, schedules and professionals of a whole result set. The D-066 public scope shows the shop-owned rows of one shop only, so a scope per shop would have been N+1 queries (spec §21).
- **What.** `IPublicDataScope.BeginMany(shopIds)` opens the same read-only view for a set of shops (at most `MaxShops` = 250).
  - While it is open, the tenant filter shows exactly the rows of those shops, through a separate filter branch (`PublicShopSet.Contains(ShopId)`).
  - The one-shop branch is unchanged. Saving throws, as in every public scope, and the caller's own tenant is ignored.
- **Rules.** Only `*.Application.Public` (and `*.Application.Customer`) types may use it (the existing architecture rule). The integration test `PublicScopeForManyShops_ShowsExactlyThoseShopsRows_AndIsReadOnly` proves that a shop outside the set stays invisible, that closing the scope restores isolation, and that saving inside it fails.

## D-091 — Discovery pipeline, text search and the bounded probe — Accepted (Phase 11)
- **Pipeline** (`DiscoveryCatalog`, Shops module):
  1. Active shops with a location, nearest first within the radius, using PostGIS `ST_DWithin`/`ST_Distance` on the GiST index. The default radius is 10 km and the maximum 50 km. Without a location the query takes a city, or everywhere. At most 200 candidates.
  2. Only shops that `IShopBookability` lets appear in discovery (subscription and pause, D-013/D-014).
  3. One multi-shop scope (D-090) for published offers (`IShopOfferReader`, Services), ratings (`IRatingReader`, Reviews) and opening status (`IShopOpeningReader`, Availability), with a fixed number of queries.
  4. A shop without any published service or package is not listed, because there is nothing to book.
- **Text search** (`SearchText`, building-block domain):
  - Normalization: case folding, no tashkeel or tatweel, أ/إ/آ/ٱ→ا, ى→ي, ة→ه, ؤ→و, ئ→ي, Arabic-Indic digits → Latin.
  - A shop matches when every query word appears in its name, slug or district, or in a published offer's name or category.
  - It runs in memory over the candidate set. That fits the v1 scale (one city, at most 200 nearest). When a city has more shops, add a normalized column with a trigram index.
- **Filters and sorts.**
  - Category: the shop has a published service in it.
  - Open now; verified.
  - Price, on the *pin price* (the matched offer, else the lowest price, mapRules #1). The response carries the price range computed before the price filter.
  - Sort by distance (the default with a location), rating (average, then count; the default without a location) or earliest slot. API enum values are PascalCase; the web maps its own URL values.
- **Bounded probe.** "Earliest slot" and "bookable today" run the real availability engine (`ISlotProbe`) for today and tomorrow on each shop's probe offer. That is the matched offer if online-bookable, otherwise the shortest online-bookable offer with an assigned professional.
  - Only the first 24 shops after the other filters are probed. With "bookable today", shops beyond them are left out; with "earliest", they follow in the base order.
  - The shops on the returned page are always probed, so every card shows its earliest time.
- **Other endpoints.** Popular categories ("from X" over the shops nearby, DV-S11), landing stats, areas for manual location (D-095), sitemap data, top professionals, the shop's live status (open now plus each professional's next time), professional detail and next slots. Opening status uses the engine's own rules (`AvailabilityEngine.OpenStatus`), including closures and windows past midnight; the web app never recomputes it.
- **Reserved slug.** `/public/shops/search` shadows a shop slug of `search`, so that slug is reserved (`Shop.ReservedSlugs`).

## D-092 — Reviews read side and rating aggregates — Accepted (Phase 11; review creation in Phase 12)
- **Review.** `reviews.reviews` is shop-owned and customer-owned (D-085), one per booking (unique `booking_id`). It keeps a snapshot of the author's public name (first name + surname initial, skipping «ال», D-017/DV-S15) and of the booked item's name. A review needs a completed booking and a rating from 1 to 5. The comment is optional, up to 1000 characters.
- **Keys.** A composite FK `(shop_id, professional_id)` references the professional. The same-shop booking reference `(shop_id, booking_id)` → `bookings.bookings` is added by SQL in the `Reviews` migration, because the booking's key type belongs to the Bookings module. The Reviews module reads bookings through `IBookingReviewSource`.
- **Aggregates.** `reviews.rating_aggregates` holds count, sum and per-star counts per shop and per professional. They are a platform read model with no personal data, on the reviewed tenancy allow-list, so discovery can sort any candidate by rating. `RatingBook` updates them in the same unit of work as the review (R-RVW-01).
- **Demo data.** Six completed demo visits (`DemoVisits`) get reviews: four at Al Asala, two at Barber House, none for Faisal (his exact free slots are asserted by the schedule E2E). The public list is paged and newest first; it can be filtered to one professional. The public professional list now carries each professional's rating.

## D-093 — Public response cache with eviction on save — Accepted (Phase 11)
- **Why not Next.js tag revalidation.** It would need the API to call the web app after each change: an extra route behind the `/api` rewrite and the locale middleware, a secret, and no durable delivery.
- **What.** The API caches anonymous public GET responses in memory (ASP.NET Core output caching, `CachePublicly()`, 5 minutes, varying by query).
  - Covered endpoints: shop page, services, packages, professionals, professional detail, reviews, categories, stats, areas and sitemap.
  - Search, status and next slots are never cached: they depend on location or time.
  - The framework's default policy skips authenticated requests and responses that set cookies, and the public handlers answer the same for everyone, so no authorization-sensitive data is ever shared.
- **Eviction.** Entities whose changes alter public pages carry `IPublicContent`: shop, pause, services, packages, categories, assignments, professionals, opening hours, reviews, aggregates, coverage and platform settings. After a successful save that touches one, an EF interceptor (`PublicContentInterceptor`) evicts the whole public tag. Inside an explicit transaction the eviction comes just before the commit, which can leave a page stale for at most the expiry.
- **Web.** Server-rendered public pages call the API through `getPublicApi()`: no cookies (so the cache applies), `no-store` in Next.js, and the visitor's address forwarded (D-094). Time-dependent and per-visitor parts are outside the cached payloads: open status, booking state and next times come from `/status`; distance is computed in the browser. The pages are rendered per request, so `next build` never needs the API; `sitemap.xml` is dynamic and lists the static pages when the API is unreachable.
- **Limit.** The cache is per API instance, so another instance can serve a page up to 5 minutes old. `PublicResponses_AreCachedForAnonymousReaders_AndEvictedByAnyPublicContentSave` proves that anonymous responses are cached, signed-in ones are not, and a save evicts.

## D-094 — Client address for server-rendered calls — Accepted (Phase 11)
- **Problem.** Server-rendered public pages call the API from the web server, so per-IP rate limits would see one client for everyone.
- **Web side.** `getPublicApi()` forwards the visitor's address as `X-Forwarded-For`: the **last** hop of the incoming header (appended by Nginx), else `X-Real-IP`, and only if it is a plain IPv4/IPv6 address.
- **API side.** The header is honoured only from configured proxies: `ReverseProxy:KnownProxies` (addresses) and the new `ReverseProxy:KnownNetworks` (CIDR, for the web container network).
- **Production (Phase 17 Nginx example).** Nginx must set `X-Forwarded-For $remote_addr` (overwrite, not append) to both the web and the API. The web server's network is listed in `KnownNetworks`.
- **Local compose.** Local compose raises the `search` and `availability` limits for development and E2E, as D-058 did for `otp` and `auth` (the E2E runs showed no 429s, but every call there comes from one container address).

## D-095 — The customer's location stays on the device — Accepted (Phase 11)
- **Where it lives.** The chosen location is a first-party cookie, `trimme-location`: `SameSite=Lax`, `Secure` on HTTPS, 30 days. It holds latitude and longitude rounded to 3 decimals (about 100 m), a district label and the source (device or district). Server-rendered discovery pages read it; the API receives it only as search parameters and never stores it (Serilog request logging records the path without the query string).
- **Asking.** The browser is asked for geolocation only when the customer presses "allow". Manual choice lists the districts that have listed shops (`GET /public/areas`, their average position), so there is no third-party geocoding call.
- **Distance on the shop page** is computed in the browser from the cookie, so the shared page never depends on who views it.
- **Configuration.** `TRIMME_SITE_URL` (the public origin for canonical URLs, hreflang, Open Graph and the sitemap) and `TRIMME_PARTNER_CONTACT_URL` (the optional landing "become a partner" link; the button is hidden when empty) are runtime settings of the web server.

## D-096 — Guests confirm through a sign-in round trip, not inline auth sub-steps — Accepted (Phase 12; supersedes the inline part of D-033)
- **What.** A guest goes through the whole wizard. At the review step "confirm" first tries a silent session refresh (the access cookie may just have expired). If there is no session it goes to the existing mobile sign-in with `returnTo` = the wizard URL at the review step. The OTP pages carry `returnTo` through sign-in → verify → complete profile (new customers), then back to the same review, where the customer confirms again.
- **Why.** The auth screens, their rate limits, CSRF handling and tests already exist (Phase 04). Embedding phone → code → name inside the wizard would duplicate them and mix auth state into booking state. The URL already holds every choice (D-028): service or package, professional, date and local time, step. The note is kept only in the page, so it is never put in a URL.
- **Safety.** The returned review re-checks the time against fresh slots, and moves to the time step with a notice if it is gone. `returnTo` stays under `safeReturnTo`'s 512-character limit (web test). `booking.profile_incomplete` also leads to complete-profile and back. Staff accounts see "customers only" and cannot confirm.
- **Idempotency.** The wizard keeps one idempotency key per request tuple (offer, professional, start, note): a double click or a retry after a network error replays the same booking, and any change gets a new key (R-BKG-05).
- **Conflict.** A 409 `booking.slot_unavailable` returns to the time step with "this time was just taken" (`notice=conflict` in the URL) and fresh slots (DV-A24).

## D-097 — Reviews by customers, and reschedule availability — Accepted (Phase 12)
- **Review command.** `POST /me/bookings/{id}/review`, customer only, rate-limited (`review`). The booking is read through `IBookingReviewSource`, and only the customer's own is found (404 otherwise).
  - Not completed → 409 `review.booking_not_completed`.
  - After `ReviewWindowDays` (platform setting, default 7) counted from the completion time → 422 `review.window_closed`.
  - A second review → 409 `review.already_exists`, also when two submissions race: the unique `booking_id` index decides.
  - Optional tags from a fixed list (`Punctuality`, `Quality`, `Cleanliness`, `Manners`, `Price`; design "ما الذي أعجبك؟"), stored once each and in order. Comment up to 1000 characters.
- **Aggregates.** `RatingBook` now upserts both aggregate rows with one atomic `INSERT … ON CONFLICT DO UPDATE` that adds in SQL, inside the review's transaction. Parallel reviews of one shop never lose an update, and the first two never race on creating the row (integration test: a double submit gives one 201 and one 409; six parallel reviews give exact totals). The public cache is evicted again after the commit.
- **Booking view.** `Booking.CompletedAt` is the time of the transition to Completed. The customer's booking response adds `Review` to `allowedActions` while the booking is completed, has a customer, is unrated and is within the window. It also carries `reviewRating` and `reviewDeadline`. Ratings come from `IReviewLookup`, filtered by the customer.
- **Reschedule availability.** `GET /me/bookings/{id}/reschedule/dates` and `/reschedule/slots` return the same online rules as the public slots (lead time, horizon, grid, every collision) for the booking's own professional and duration. The booking's own interval is ignored, so it can move a few minutes. They are refused after the cutoff or for a closed booking, and report `bookable: false` while the shop takes no online bookings. The move itself is the Phase 10 command (D-089).
- **Demo data.** Sara (`+966500100303`) has eight completed, unreviewed visits at Barber House with Majed, one to four days before seeding, and one upcoming booking. They serve the review E2E. On a database seeded more than seven days ago none is reviewable, so reset the volume.

## D-098 — Favorites — Accepted (Phase 12)
- **Model.** `customers.favorites` is customer-owned (D-085), so only its customer reads, adds or removes it; the data layer refuses anything else. Each row is one saved shop, or one saved professional with the professional's own shop as `shop_id`: composite FK `(shop_id, professional_id)`, so a favorite can never point at another shop's professional. Two filtered unique indexes make saving idempotent; at most 200 of each kind per customer.
- **API.**
  - `GET /me/favorites` returns the cards discovery would show. `IShopCards` runs the discovery pipeline on those shop ids, in chunks of the candidate cap. Professional cards come from `IProfessionalDirectory` in one multi-shop public scope, plus ratings.
  - The response also lists every saved id, so hearts can be filled.
  - Saved rows whose shop left discovery, or whose professional was disabled, are kept but not shown.
  - `PUT`/`DELETE /me/favorites/shops/{id}` and `…/professionals/{id}` (PUT takes `shopId`) are idempotent, rate-limited (`favorites`), and 404 for a shop discovery does not list or a professional not active in that shop.
- **Hearts.** Client islands on the shop and professional pages. They probe `GET /me/favorites` with a plain `fetch`, so an anonymous visitor's 401 starts no session refresh or redirect. Hearts that mount together share the request in flight, but no settled answer is kept: the heart's own sign-in round trip returns to the page by client-side navigation, sometimes in under a second. Signed out, the heart is a sign-in link with `returnTo`; staff (403) see none. Toggling is optimistic with rollback.
- **Module order.** Customers now comes after Professionals in `ModuleCatalog`, because the favorites model references the professional entity by name.

## D-099 — Live operations updates over SignalR — Accepted (Phase 13)
- **Hub.** `/hubs/operations` sits outside `/api/v1`, is cookie-authenticated and has no client-callable methods.
  - On connect the server picks the only group: `shop:{id}` for a shop user of an operable (not suspended) shop with `Shop.Bookings.Read`; `admins` for a platform admin with `Admin.Bookings.View`. Every other connection is closed at once (customers, anonymous, no permission).
  - The shop status and the permissions are read at connect time from the connection's own user. The endpoint permission handler reads the HTTP context, and with long polling the hub runs after the request that started it has ended.
  - `CloseOnAuthenticationExpiration` closes a connection when the access cookie expires. The web client then refreshes the session and reconnects with backoff (1 s … 30 s). Failures count across closes, so a connection the server drops right after opening backs off too; the count resets after 30 s connected. The client stops only when the server refuses the refresh (401/403); an unreachable API, 429 or 5xx is retried with the same backoff.
  - A revoked session or a suspended shop keeps receiving events only until the short-lived access cookie expires.
- **Origin.** Hub requests carrying an `Origin` other than the web app's (the CORS origins and `Web:PublicBaseUrl`) get 403: negotiate, WebSocket, SSE and long polling. This is our own middleware, because CORS does not cover WebSockets and the framework's allow-list is skipped under the test server. Local compose now derives the web origin from `TRIMME_WEB_PORT`.
- **Source of events.** An EF interceptor turns the outbox messages a unit of work wrote into events after the commit:
  - outside an explicit transaction, after the save;
  - inside one, on commit;
  - dropped on rollback or failure.
  Every booking writer is covered without calling anything. Modules project their own outbox payloads (`IOperationsEventProjector`; Bookings maps `booking.*`). A publish failure is logged, never thrown.
- **Contract.** `bookingChanged { kind, shopId, bookingId, professionalId, status, startsAt, endsAt }`: ids, times and status only, never a customer id, name or phone (R-NEG-04). The outbox payload's `customerId` is dropped. Clients refetch what they show through the API.
- **Transports.** WebSockets, SSE and long polling all deliver through the Next.js `/hubs` rewrite in local compose (spike: 58–720 ms, 34–80 ms and 52 ms). Production Nginx must forward `/hubs` with the WebSocket upgrade headers (Phase 17).
- **Scale.** A single instance needs no backplane; scale-out (Redis backplane or Azure SignalR) is a Phase 17 note.
- **Matrix.** The endpoint authorization matrix covers `/api/v1` only; the hub has its own tests (`RealtimeTests`: isolation, rejection, origins).

## D-100 — The shop operations board: business days, load and the week view — Accepted (Phase 13; refines D-034)
- **Business days.** A booking belongs to the day whose opening window contains its start, so 00:30 in a window that opened on Thursday is Thursday's. Outside every window, its local date decides.
  - The Availability module exposes each business day's plan (`IShopDayPlanReader`): opening windows, and per professional their working time (open ∩ their hours), breaks and time off, from the engine's own rules (`AvailabilityEngine.DayOf`).
  - The overview's "today", the calendar columns and the density strip all use it; the day view's axis runs past 24:00 when a window does.
- **Overview.**
  - KPIs: the day's bookings without cancellations, and the previous day's; bookings still pending from now on (any day); completed; no-shows; cancellations; free capacity.
  - Load is minutes-based: each professional's booked minutes against their available minutes (working time outside breaks and time off).
  - Free capacity is the sum of what remains, never below zero per professional. "On leave" means time off takes all of that day's working time.
  - The hourly chart covers the last seven business days by local starting hour, without cancellations.
- **Calendar.**
  - Day view: one minute-accurate column per professional (DV-S19), with off-shift, breaks and time off shaded. Free half-hours link to a prefilled walk-in; the walk-in page offers the exact free times.
  - Week view (refines D-034): seven day columns with every booking, overlapping ones side by side in lanes (one professional on demand), plus the design's density strip (day parts × days; closed days hatched).
  - Cancellations are not drawn.
- **Appointments list.** Any of several statuses. The chip counts use every other filter, and the cancelled chip counts both cancellation statuses (DV-S08). Search is by name or reference only (DV-S18). The detail drawer shows only the API's `allowedTransitions`, applied optimistically and rolled back on a refusal.
- **Walk-in options.** `GET /shop/availability/walk-in` returns the active professionals assigned to the item (every item of a package), whether each is free now for the whole duration, the next free start and the day's free starts on the slot grid. These are the command's own collision rules. Online-only rules (lead time, horizon, the online-bookable flag, the pause) do not apply at the desk. The date may be up to 60 days ahead.
