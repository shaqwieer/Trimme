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

## D-004 — Application dispatcher: MediatR vs alternative — **Open** (blocks Phase 1)
Spec lists MediatR "where useful". MediatR ≥ 13 requires a commercial licence key (Community licence free for orgs < US$5M revenue and < US$10M outside capital; a missing key logs warnings, no runtime limits). **Recommended default:** a thin in-house `ICommandHandler<TCommand,TResult>` / `IQueryHandler<,>` + pipeline behaviours (validation, logging, transaction) registered via DI — no licence dependency, same architecture. Alternative: MediatR 14 with a Community key supplied via configuration (never committed).

## D-005 — Customer authentication model — **Open** (blocks Phase 4)
Design (3501, 918–970): passwordless mobile + 4-digit OTP via WhatsApp (SMS fallback text), "no passwords in v1". Spec §9/§12: ASP.NET Core Identity with password security, forgot/reset. **Recommended default:** customers = passwordless OTP on ASP.NET Core Identity (phone user, custom token provider, **6-digit** code (DV-C01), ≤5 min expiry, ≤3 attempts/code, per-phone + per-IP rate limits, lockout); shop and admin accounts = email + password with forgot/reset, lockout, invite flow. Forgot/reset is N/A for customers under this default. OTP sender is an adapter (fake in dev; WhatsApp authentication template in prod; SMS provider optional later).

## D-006 — Online booking initial status — **Open** (blocks Phase 10)
Design contradicts itself: success copy "تم تأكيد حجزك" (1586) vs lifecycle "بانتظار التأكيد" + shop confirm (3575, 4024). **Recommended default:** per-shop setting `RequireManualConfirmation` (default **false** → online bookings are created `Confirmed`; when true → `Pending` and the shop must confirm). Copy and WhatsApp templates vary by resulting status ("confirmed" vs "request received").

## D-007 — Maps and geocoding provider — **Open** for production (blocks Phase 6 production config only)
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
