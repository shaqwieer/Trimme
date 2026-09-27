# TRIMME — Master Implementation Plan

> Source specification: `TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md` (repo root) — the authoritative product spec.
> Design source: `design/source/TRIMME.dc.html` (+ analyses in `design/analysis/`). Design deviations: `docs/design-deviations.md`.
> Status vocabulary: `[ ]` not started · `[~]` in progress · `[x]` completed **and verified** · `[!]` blocked · `[-]` deferred/out of scope.
> An item is `[x]` only when its acceptance criteria and required checks pass **with recorded evidence**.

## 1. Objective

Build TRIMME: an Arabic-first (RTL), mobile-responsive marketplace where customers discover nearby salons/barbers and book real, server-validated appointments (`shop → service → professional → date → time → review → confirmation`). It serves three authenticated user types — **Customer**, **Shop/Business**, **Platform Admin** (SuperAdmin is the top permission set inside Platform Admin). It ships as one Next.js web app + one ASP.NET Core API + PostgreSQL/PostGIS, API-first for a future mobile app.

## 2. Non-negotiable constraints

1. **Tenant isolation** — a shop can never read, write, reference or infer another shop's data; tenant resolved from claims only; enforced in API + data layer; proven by IDOR tests.
2. **Customer phone privacy** — the customer phone number never reaches any shop-facing DTO, log, export, SignalR message, HTML or browser payload. Proven by DTO contract tests.
3. **No barber transfer** — each professional belongs to exactly one shop; `Professional.ShopId` is immutable; no transfer route, permission, UI, doc or scaffold.
4. **Shop-owned services** — shops set their own service names/descriptions/prices/durations; no global price/duration; history kept via booking snapshots.
5. **Server-calculated availability**; double booking impossible under concurrency (transactional recheck + PostgreSQL exclusion constraint + idempotency).
6. **SuperAdmin-only**, data-driven, versioned subscription plan pricing; no retroactive history rewrite.
7. **No online payment/checkout in v1** — only a documented future payment seam and neutral booking fields.
8. **WhatsApp via provider abstraction** with a dev fake; editable, versioned ar/en templates per audience/event; reminders exactly 30 min before (configurable setting, default 30) for customer and professional; outbox + Hangfire; never real credentials locally.
9. **Arabic RTL primary (`/ar`), English LTR secondary (`/en`)**, no hardcoded UI strings, key-parity tests.
10. **Never** run production migrations, deploy externally, create paid resources or use real WhatsApp credentials. Tokens never in `localStorage`.

## 3. Architecture summary

```
apps/web                Next.js 16 (App Router, RSC-first, strict TS, Tailwind v4 w/ design-token CSS vars, next-intl, TanStack Query, RHF+Zod, Vitest, Playwright)
apps/api                ASP.NET Core 10 host (composition root, /api/v1, OpenAPI, SignalR hubs, Hangfire server/dashboard (admin-only))
src/BuildingBlocks      Domain primitives (strong IDs, Entity/AggregateRoot, Result/Error codes, clock), Application abstractions (commands/queries, validation, current-user/tenant), Infrastructure (EF base, outbox, idempotency, audit, redaction)
src/Modules/<Module>    Domain / Application / Infrastructure / Api per feature: Identity, Shops, Services, Professionals, Availability, Bookings, Customers, Reviews, Subscriptions, Notifications, QrAnalytics, Administration
tests/UnitTests, IntegrationTests (Testcontainers PostGIS), ArchitectureTests (NetArchTest), apps/web tests (Vitest), tests/E2E (Playwright)
infra/                  docker-compose, Dockerfiles, nginx example, CI
```

- **Modular monolith**, one PostgreSQL database, one EF Core `DbContext` per module sharing a connection (schemas per module) — final shape confirmed in Phase 1 (see D-001).
- **Dependency direction**: Api → Application → Domain; Infrastructure → Application/Domain; modules talk via public contracts/integration events only. Enforced by architecture tests.
- **Auth topology**: single public domain; Nginx routes `/api/*` and `/hubs/*` to the API and everything else to Next.js. API issues Secure/HttpOnly/SameSite cookies (short access + rotating refresh); CSRF token header for state-changing requests. Next.js server components call the API server-side forwarding cookies (D-027).
- **Tenancy**: `ICurrentTenant` from claims → EF global query filters on every shop-owned entity + server-side stamping + composite FKs `(ShopId, Id)`; explicit, isolated `IAdminDataScope` bypass.
- **Contracts**: OpenAPI generated from the API → TypeScript client/types generated into `apps/web` (drift check in CI).
- **Time**: UTC instants in DB; shop time zone (default `Asia/Riyadh`) used at boundaries and in the availability engine. SAR currency, Saudi Arabia demo data.

## 4. Design-file inventory

| File | Role |
|---|---|
| `TRIMME.dc.html` (4656 lines) | Prototype: 3 doc screens, 2 design-system screens, 10 customer, 7 shop, 8 admin, 3 review screens. Visual source of truth. |
| `support.js` | Generic Claude Design runtime — no product logic; not shipped. |
| `trimme-logo.png` | Logo (677×369). Used in nav, auth, public pages, metadata; never altered. |

Details: `design/analysis/01-design-system-and-docs.md`, `02-customer-screens.md`, `03-shop-admin-screens.md`.

## 5. Screen-to-route map

All routes are prefixed `/[locale]` (`ar` default, `en`). "Idx" = indexable.

### Customer (public + `customer` role)
| Design screen | Route(s) | Idx | Phase |
|---|---|---|---|
| c-landing | `/` | yes | 11 |
| (absent) public shop listing | `/shops` (+ city filter) | yes | 11 |
| c-auth (sign-up/verify/location) | `/auth/sign-up`, `/auth/sign-in`, `/auth/verify`, `/auth/complete-profile`, `/onboarding/location` | no | 4 (auth), 11 (location) |
| (absent) staff sign-in / forgot / reset | `/auth/staff/sign-in`, `/auth/forgot-password`, `/auth/reset-password` | no | 4 |
| c-home | `/discover` | no | 11 |
| c-map | `/search?view=list|map&…` | no | 11 |
| c-shop | `/shops/[shopSlug]` (+ `?tab=`) | yes | 11 |
| c-shop (pro profile) | `/shops/[shopSlug]/professionals/[proSlug]` | yes | 11 |
| c-booking | `/shops/[shopSlug]/book?step=…` | no | 12 |
| c-appointments | `/account/bookings`, `/account/bookings/[id]` | no | 12 |
| c-rate | `/account/bookings/[id]/reschedule`, `/account/bookings/[id]/review` (+ cancel dialog) | no | 12 |
| c-profile | `/account`, `/account/favorites`, `/account/notifications`, `/account/security` | no | 4 (security), 12, 15 (notifications) |
| c-qr | `/q/[code]` | no (canonical → shop/pro) | 16 |

### Shop dashboard (`shop` role)
| Design screen | Route(s) | Phase |
|---|---|---|
| s-overview | `/shop` | 13 |
| s-calendar | `/shop/calendar?view=day|week` | 13 |
| s-appointments | `/shop/appointments` (+ `/[id]` drawer) | 13 |
| s-walkin | `/shop/walk-in` | 13 |
| s-hours | `/shop/schedule` | 9 |
| s-services (corrected) | `/shop/services`, `/shop/services/new`, `/shop/services/[id]` | 7 |
| s-services (subscription column) | `/shop/subscription` | 8 |
| s-settings | `/shop/settings`, `/shop/settings/location` | 6 |
| (absent) notifications | `/shop/notifications` | 15 |

### Admin (`admin` user type; permission-gated)
| Design screen | Route(s) | Phase |
|---|---|---|
| a-overview | `/admin` | 14 |
| a-shops | `/admin/shops`, `/new`, `/[id]` (tabs) | 5–6 |
| a-pros (corrected, no transfer) | `/admin/professionals`, `/new`, `/[id]` | 6 |
| a-services (corrected) | `/admin/services`, `/admin/services/categories`, `/admin/packages` | 7 |
| a-appointments | `/admin/bookings`, `/[id]`; `/admin/customers`, `/[id]` | 14 |
| a-subs | `/admin/subscriptions`; SuperAdmin `/admin/subscription-plans` | 8 |
| a-reviews (reviews) | `/admin/reviews` | 14 |
| a-reviews (QR) | `/admin/qr` | 16 |
| a-reviews (WhatsApp) | `/admin/whatsapp/templates`, `/admin/whatsapp/dispatches` | 15 |
| a-roles | `/admin/roles`, `/admin/audit` | 14 |
| (absent) platform settings | `/admin/settings` | 8 (core), 14 (full UI) |

Design-system and review screens (`ds-*`, `r-*`, `doc-*`) are not product routes; they feed tokens/components (Phases 2–3) and review criteria (Phase 17). A dev-only component gallery lives at `/[locale]/dev/components` (excluded from production builds).

## 6. Roles and permissions summary

Full matrix: `docs/permissions-matrix.md` (created in Phase 4). Seeds (D-018, D-019):

| User type | Seed roles | Scope |
|---|---|---|
| Customer | `Customer` | Own profile, own bookings, own reviews (completed, once), favorites |
| Shop | `ShopOwner`, `ShopStaff` | Own shop only (tenant from claims). Owner: services, hours, breaks/time off, pause, profile fields permitted by admin policy, location (if permitted). Staff: booking operations + walk-ins. Neither: professionals CRUD/assignment, customer phone, exports, other shops. |
| Platform Admin | `SuperAdmin` (مدير عام), `OperationsManager`, `Support` | Permission-based (`Admin.*`). `SuperAdmin.SubscriptionPlans.Manage` + subscription overrides are SuperAdmin-only. |

Explicitly **never** in the catalogue: any transfer permission, customer export, shop access to customer contact.

## 7. Phase sequence

The spec suggested 11 phases (0–10). This plan uses **19 phases (0–18)** because several suggested phases could not be implemented, tested, documented and handed off within one session:
- spec phase 1 → **1** backend/infra foundation, **2** web foundation, **3** design-system component library;
- spec phase 2 → **4** identity/sessions and **5** tenancy/privacy/audit core (tenancy needs real shop-owned entities, so Phase 5 introduces `Shop`/`ShopUser`);
- spec phase 3 → **6** shops/locations/professionals, **7** services/packages, **8** subscriptions foundation (+ platform settings core);
- spec phase 4 → **9** schedules + availability engine, **10** booking core (state machine, exclusion constraint, idempotency, **outbox records written in the booking transaction**);
- spec phase 8 → **15** WhatsApp/outbox/Hangfire/notifications and **16** QR/attribution;
- spec phases 5, 6, 7, 9, 10 → **11/12**, **13**, **14**, **17**, **18**.

Cross-cutting gates owned **from Phase 1/2 onward** (not deferred to the end): dev-only seed command (extended every phase), CI workflow, `docker compose up --build`, i18n key-parity test, Playwright harness, OpenAPI client drift check, architecture tests.

## 8. Master progress table

| # | Phase | Status | Points | Prerequisites | Verification evidence | Commit | Next action |
|---|---|---|---|---|---|---|---|
| 00 | Discovery, design import, traceability, plan | [x] | 100/100 | — | `phases/phase-00-discovery.md` §Evidence | `5cd09a9` | User approves plan + open decisions |
| 01 | Backend & infrastructure foundation | [x] | 100/100 | 00 approved; D-002, D-037 | `phases/phase-01-backend-foundation.md` §Evidence (build 0 warnings; 73 unit / 56 architecture / 27 integration tests; compose healthy; gitleaks clean) | `37500ce` | CI green: run 36134142951 |
| 02 | Web foundation, i18n/RTL, tokens, shells | [x] | 100/100 | 01 | `phases/phase-02-web-foundation.md` §Evidence (lint/typecheck/format clean; 53 unit tests; build; 20 Playwright on Docker stack; axe 0 serious; clean Linux clone green) | `9c1fcd2` | CI green: run 36134142951 |
| 03 | Design-system component library | [x] | 100/100 | 02 | `phases/phase-03-design-system.md` §Evidence (147 unit tests incl. axe; gallery axe 0 serious incl. contrast ar/en; zero overflow 390–1440; RSC proof route; Playwright 30/30 ×2 on Docker stack; CI green: run 36201825328) | `610ebec` | — |
| 04 | Identity, sessions, roles & permissions | [x] | 100/100 | 03 | `phases/phase-04-identity.md` §Evidence (build 0 warnings; 99 unit / 56 architecture / 66 integration tests incl. endpoint matrix; web 165 tests; E2E 36/36 ×2 on a clean stack; gitleaks clean) | `54cf79a` (+ follow-up fix) | CI green: run 36270344233 |
| 05 | Tenancy, privacy & audit core | [x] | 100/100 | 04 | `phases/phase-05-tenancy-privacy.md` §Evidence (124 unit / 61 architecture / 81 integration; web 170; E2E 38/38 ×2 on a clean stack; probes for every tenancy rule; gitleaks clean) | `6a65a9d` | CI green: run 36270344233 |
| 06 | Shops, locations & professionals | [x] | 100/100 | 05 | `phases/phase-06-shops-professionals.md` §Evidence (build 0 warnings; 153 unit / 62 architecture / 97 integration; web 179; E2E 40/40 ×2 on a clean stack; images stored in PostgreSQL (D-064); no-transfer gate clean; gitleaks clean) | `805313b` | CI green: run 36316322648 |
| 07 | Services, categories & packages | [x] | 100/100 | 06 | `phases/phase-07-services-packages.md` §Evidence (build 0 warnings; 171 unit / 63 architecture / 106 integration; web 196; E2E 42/42 ×2 on a fresh `down -v` stack after fixing a 41/42 first run; gitleaks clean) | `9cb2593` (+ follow-up) | Push when asked; CI to confirm |
| 08 | Subscriptions foundation & platform settings | [ ] | 0/100 | 07 | — | — | Start next: item 8.1 re-validation |
| 09 | Schedules & availability engine | [ ] | 0/100 | 08 | — | — | — |
| 10 | Booking core & integrity | [ ] | 0/100 | 09 | — | — | — |
| 11 | Public discovery & shop pages | [ ] | 0/100 | 10 | — | — | — |
| 12 | Customer booking & account | [ ] | 0/100 | 11 | — | — | — |
| 13 | Shop operational dashboard | [ ] | 0/100 | 12 | — | — | — |
| 14 | Admin operations dashboard | [ ] | 0/100 | 13 | — | — | — |
| 15 | WhatsApp, outbox, Hangfire & notifications | [ ] | 0/100 | 14 | — | — | — |
| 16 | QR codes & attribution analytics | [ ] | 0/100 | 15 | — | — | — |
| 17 | Localization, SEO, a11y, security, observability, performance | [ ] | 0/100 | 16 | — | — | — |
| 18 | Full regression, deployment docs, handover | [ ] | 0/100 | 17 | — | — | — |

**Platform total: 800 / 1900 points.**

## 9. Risks and external dependencies

| Risk / dependency | Impact | Mitigation |
|---|---|---|
| Customer auth model conflict (design: passwordless WhatsApp OTP; spec: Identity passwords + reset) | Resolved | D-005/D-037: passwordless OTP for customers |
| Online booking confirmation mode (design contradicts itself) | Resolved | D-006/D-037: per-shop setting, default auto-confirm |
| MediatR v13+ commercial licence (free Community tier < $5M revenue, key required; missing key only logs warnings) | Resolved | D-004/D-037: in-house dispatcher |
| Production map tiles / geocoding provider; design has **no** location pin picker | Production needs a self-hosted or OSM-based host (public OSM services forbid heavy use) | D-007/D-037/D-068: OpenStreetMap behind `MapView`/`IGeocoder` adapters (built Phase 06); picker designed in the TRIMME visual language (DV-A02, applied); tile/geocoder hosts are configuration |
| Images stored in PostgreSQL (user request, D-064) | DB size and backup volume grow with photos; no CDN yet | Immutable, cacheable media URLs (Nginx/CDN caching in Phase 17); `IMediaStore` isolates a later move to object storage |
| OTP delivery channel (WhatsApp auth template / SMS fallback provider) | Phase 4 uses a fake sender; prod needs Meta auth template approval | Adapter + fake; recorded in `docs/whatsapp-integration.md` |
| Email/SMTP provider for staff invitations and password resets | Mailpit locally (Phase 04); production needs a provider | `IEmailSender` (MailKit SMTP); `Email__Smtp__*` env vars only; API refuses to start in production without it (D-056) |
| Meta WhatsApp Business account, phone number ID, approved templates | Phase 15 production readiness only | Fake provider locally; config documented |
| Design gaps: many desktop layouts, English screens, admin editors absent (see deviations A-items) | Extra design work each phase | Compose from the design-system components; record each in `design-deviations.md` |
| AA contrast failures in design tokens (`#4A7FB5` links 4.20:1, tertiary greys 2.1–2.8:1) | a11y gate | Token fix D-021 |
| Machine default .NET SDK is a preview (`10.0.300-preview`) | Build reproducibility | `global.json` pin to 10.0.1xx stable (D-002) |
| Next.js 16 / next-intl / Tailwind v4 API changes | Web foundation | Verify against current docs during Phase 2 |
| Docker Desktop required for Testcontainers + compose | Integration tests | Verified running in Phase 0 |

## 10. Decisions resolved with the user (Session 1)

| ID | Question | Decision | Affects |
|---|---|---|---|
| D-004 | Dispatcher | **In-house dispatcher** (no MediatR) — D-037 | Phase 1 |
| D-005 | Customer authentication | **Passwordless mobile + OTP** for customers; email + password for staff — D-037 | Phase 4 |
| D-006 | Initial booking status | **Per-shop `RequireManualConfirmation`, default auto-confirm** — D-037 | Phase 10 |
| D-007 | Maps/geocoding | **OpenStreetMap** (MapLibre + OSM tiles, Nominatim-compatible geocoder; self-hosted or OSM-based host in production, per the usage policies) — D-037 | Phase 6 (host choice is config, Phase 17/18) |

Other product assumptions (D-012 … D-035) are recorded in `DECISIONS.md` with overridable defaults.

## 11. Definition of done (whole platform)

Mirrors spec §23 — all must hold with evidence recorded in phase files:
- Design imported; relevant screens implemented as real reusable Next.js routes/components; deviations documented.
- Arabic RTL and English LTR both work (key parity, bidi, mirrored UI).
- Customer, shop and admin journeys connected to the real API and database.
- Tenant isolation and customer-phone privacy proven by automated tests.
- Availability server-calculated; overlapping bookings prevented under concurrency (test proves exactly one winner).
- Shops manage only their own services/prices/durations; cross-shop access blocked and tested.
- Barber transfer does not exist (tests assert absence).
- WhatsApp works through the fake provider locally; Meta adapter ready; professional numbers admin-managed, protected; new-booking, update/cancel, 30-minute reminder tests for both audiences with editable versioned templates.
- Exact shop location captured/edited via map pin and stored as PostGIS geography used by nearby search.
- SuperAdmin plan management, price versioning, assignment, renewal, overrides and statuses work with permission tests.
- No v1 payment UI or charge flow; payment seam documented.
- Migrations, seed, tests, lint, type check, production builds and `docker compose up --build` pass.
- No real credentials, no production PII, no known critical/high security issue.
- Final implementation report delivered (spec §23 list).
