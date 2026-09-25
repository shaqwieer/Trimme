# TRIMME — Claude Code Full-Stack Implementation Prompt

## Mandatory execution model: phased work across multiple Claude Code sessions

This specification is intentionally larger than one context window. **Do not attempt to implement the whole platform in one session.** The first session is a planning/orientation session. Later sessions implement one approved phase at a time and resume from durable repository state rather than relying on chat memory.

The rules in this section have higher priority than any later instruction that appears to request all features at once.

### Session 1 — discovery and phase plan only

In the first session:

1. Inspect the repository, git status/history, existing instructions, and installed tooling.
2. Authenticate with `/design-login` and import/inspect the complete Claude Design project, especially the selected files listed below.
3. Read this complete specification and create a traceable screen, API, data, role, permission, integration, and test inventory.
4. Break the work into practical implementation phases that can each be completed and verified in a separate Claude Code session.
5. Create the durable planning and handoff documents described below.
6. Run read-only baseline checks when possible and record their results.
7. **Stop after presenting the phase plan and recommended first implementation phase. Do not start feature implementation in Session 1.** Wait for the user to approve or request the first phase.

### Required durable project-control files

Create and maintain these files in the repository:

```text
docs/implementation/
  MASTER_PLAN.md
  STATUS.md
  SESSION_HANDOFF.md
  DECISIONS.md
  TRACEABILITY.md
  phases/
    phase-00-discovery.md
    phase-01-....md
    phase-02-....md
    ...
```

Also add a concise “How to resume work” section to the repository's `CLAUDE.md`. If `CLAUDE.md` does not exist, create it without duplicating the whole product specification.

#### `MASTER_PLAN.md`

It must contain:

- Project objective and non-negotiable constraints
- Architecture summary
- Design-file inventory and screen-to-route map
- Role and permission summary
- Proposed phase sequence and dependencies
- A master progress table with phase, status, completed points/total points, prerequisites, verification evidence, commit hash, and next action
- Risks, external dependencies, and decisions still requiring the user
- Definition of done for the entire platform

Use this status vocabulary consistently:

- `[ ]` Not started
- `[~]` In progress
- `[x]` Completed and verified
- `[!]` Blocked
- `[-]` Explicitly deferred/out of scope

Never mark an item `[x]` merely because code was written. It is complete only after its acceptance criteria and required checks pass, with the evidence recorded.

#### Every phase file

Every `phase-XX-*.md` must include:

- Goal and user-visible outcome
- Prerequisites
- In scope
- Explicitly out of scope
- Numbered checklist with point values, normally totaling 100 points
- Files/modules expected to change
- Data model and migration impact
- API contracts and UI routes affected
- Security, tenancy, privacy, RTL, accessibility, and responsive considerations
- Tests and verification commands
- Acceptance criteria
- Rollback/recovery notes
- Completion evidence section
- Remaining risks and exact next phase

Keep phases small enough for one focused session. Split a phase if it cannot reasonably be implemented, tested, reviewed, documented, and handed off within one context window.

### Recommended phase shape

Claude Code must refine the final breakdown after inspecting the design and repository, but the plan should normally resemble:

0. Discovery, design import, requirements traceability, and master plan
1. Repository foundation, infrastructure, shared design system, i18n/RTL, and API skeleton
2. Identity, sessions, roles, permissions, shop tenancy, and privacy enforcement
3. Shops, locations, services, packages, professionals, per-shop configuration, and subscriptions foundation
4. Working hours, breaks, time off, availability engine, database overlap protection, and booking state machine
5. Public discovery and complete customer booking experience
6. Shop operational dashboard, calendar, walk-ins, schedule management, and scoped live updates
7. Admin dashboard and management workflows
8. WhatsApp/outbox/Hangfire, notifications, QR attribution, reviews, and subscription automation
9. SEO, localization completion, accessibility, security hardening, observability, and performance
10. Full regression, deployment/backup documentation, production-readiness review, and final handover

Do not blindly preserve this list if the imported design or existing repository calls for a safer split. Explain any change in `MASTER_PLAN.md`.

### Rules for every implementation session after Session 1

At the start of every new session:

1. Read `CLAUDE.md`, this specification, `docs/implementation/MASTER_PLAN.md`, `docs/implementation/STATUS.md`, `docs/implementation/SESSION_HANDOFF.md`, `docs/implementation/DECISIONS.md`, `docs/implementation/TRACEABILITY.md`, and the current phase file.
2. Inspect `git status`, recent commits, migration history, and the actual files changed in the previous session.
3. Do not trust the handoff blindly. Re-run the previous phase's smallest decisive verification and reconcile any discrepancy before continuing.
4. Select the first approved phase that is not `[x]`. Do not skip ahead because a later screen appears easier.
5. Restate the current phase goal, scope, already-completed checklist points, remaining points, and verification plan.
6. Implement only that phase. Do not begin the next phase in the same session unless the user explicitly asks.

During the session:

- Update checklist state as work progresses.
- Preserve unrelated user changes and keep the working tree understandable.
- Record material architectural or product decisions in `DECISIONS.md` as numbered entries.
- Update `TRACEABILITY.md` when a requirement is implemented, tested, deferred, or found absent from the design.
- Run focused tests during development and the phase gate checks before completion.
- If blocked, mark the exact checklist item `[!]`, record the blocker and safe recovery path, and continue only with independent in-scope items.

At the end of every session:

1. Run the phase's required test, lint, type-check, build, migration, and visual checks.
2. Update the current phase file with actual evidence, not planned evidence.
3. Update the master progress table and `STATUS.md` with completed points and remaining points.
4. Rewrite `SESSION_HANDOFF.md` so it describes the current repository state, not the beginning of the session.
5. If git is available and the phase is complete, create one clear phase commit after all gates pass. Do not commit unrelated changes. If the phase is incomplete, document the dirty files precisely and do not present it as complete.
6. Stop after reporting the phase outcome. Do not automatically begin the next phase.

### Required `SESSION_HANDOFF.md` format

```md
# TRIMME Session Handoff

- Updated at:
- Branch:
- HEAD commit:
- Working tree status:
- Current phase:
- Phase score: completed points / total points
- Last fully completed phase:

## Completed this session
- ...

## Verification evidence
- Command: ...
  Result: PASS/FAIL with a short factual summary

## Database and migrations
- Created/applied locally/not applied: ...

## Decisions added
- D-...: ...

## Known issues or blockers
- ...

## Exact next action
1. ...
2. ...

## Files intentionally left modified
- path — reason
```

The handoff must be sufficient for a fresh Claude Code session with no chat history.

Use the `claude_design` MCP (`https://api.anthropic.com/v1/design/mcp`, authenticate with `/design-login`) to import this project:

`https://claude.ai/design/p/af2d08aa-185e-42e6-be25-43d52be321f3?file=TRIMME.dc.html`

Focus on these files, while keeping the whole design project readable for context:

- `TRIMME.dc.html`
- `support.js`
- `trimme-logo.png`

Implement the selected files as a production-quality, full-stack TRIMME platform. Do not merely recreate a static HTML mockup. Use the imported design as the visual source of truth, extract its design tokens and reusable patterns, and connect the implemented screens to a real ASP.NET Core API and PostgreSQL database.

## 1. Operating instructions

1. Inspect the repository, its current branch, existing applications, configuration files, `README`, `CLAUDE.md`, `AGENTS.md`, package manifests, and working-tree state before changing anything.
2. Preserve valid existing conventions and user changes. Do not replace an existing application with a new scaffold unless the repository is genuinely empty.
3. Authenticate with `/design-login`, import the Claude Design project through `claude_design`, and inspect all three selected files before writing UI code.
4. Treat `TRIMME.dc.html` as a design specification, not as production code to paste blindly. Rebuild it using reusable React components, semantic HTML, responsive layouts, and real application states.
5. Inspect `support.js` to understand intended prototype interactions. Reimplement applicable behavior in React; do not inject the file globally or use unsafe DOM manipulation.
6. Preserve and optimize `trimme-logo.png`. Use it in the navigation, authentication, public pages, and suitable metadata. Do not redraw, stretch, recolor, or distort it.
7. Create and maintain the sessionized planning files required by the mandatory execution model above. In Session 1, stop after the plan; in later sessions, implement only the approved current phase.
8. Work in small coherent increments. After each increment, run the relevant tests, type checks, linting, formatting, and builds. Fix failures before moving on.
9. If the design conflicts with the business rules in this prompt, preserve its visual language but correct the workflow to match these rules. Document the difference in `docs/design-deviations.md`.
10. Make sensible documented assumptions when minor details are missing. Ask only when a missing decision would materially change security, money movement, or the core domain model.
11. Never run production migrations, deploy externally, create paid resources, or use real WhatsApp credentials. Local development, local migrations, mocks, and test containers are allowed.

## 2. Product definition

TRIMME is an Arabic-first, RTL, mobile-responsive marketplace for salons and barbers. It is not a generic marketplace.

Customers discover nearby participating shops, open a shop page, view its services and professionals, and book a real available appointment. The booking order is:

`shop -> service -> professional -> date -> time -> review -> confirmation`

The initial release has exactly three authenticated user types:

1. Customer
2. Shop / Business
3. Platform Admin

`SuperAdmin` is the highest permission set inside the Platform Admin user type, not a fourth public user journey. Only SuperAdmin can manage the subscription-plan catalogue and its pricing.

A professional/barber is a managed domain entity in v1, not a fourth independent account type. Professionals receive WhatsApp notifications but do not need a separate dashboard unless the imported design explicitly shows a read-only surface that can be safely mapped to the shop account.

The architecture must be API-first so a future iOS/Android app can reuse the same authentication, shops, services, professionals, availability, bookings, reviews, and notifications. A mobile application is not part of this implementation.

Online payment is not part of v1. Do not build checkout or charge customers. Define only a clean future-facing payment abstraction and neutral booking fields that will allow a gateway to be added later without redesigning the booking aggregate.

## 3. Required technology stack

Use the following baseline unless the existing repository already pins a compatible supported version:

### Frontend

- Next.js 16 with App Router
- React and strict TypeScript
- Tailwind CSS using CSS variables generated from the imported design tokens; do not allow generic Tailwind defaults to replace the visual identity
- `next-intl` or an equivalent App Router-compatible localization solution
- Arabic RTL as the primary locale and English LTR as the secondary locale
- TanStack Query for authenticated client-side server state where it adds value
- React Hook Form plus Zod for complex interactive forms
- Accessible headless primitives where useful, styled to match TRIMME rather than a generic component kit
- Lucide or another consistent outline icon set; no emoji used as interface icons
- Playwright for end-to-end tests

Prefer React Server Components for public/read-heavy routes. Use Client Components only where interactivity requires them. Keep public shop and service pages indexable and fast.

### Backend

- ASP.NET Core 10 Web API
- EF Core 10 with the Npgsql provider
- PostgreSQL with PostGIS for nearby-shop searches
- ASP.NET Core Identity for users, roles, password security, lockout, and account management
- Feature-oriented modular monolith with clear domain/application/infrastructure/API boundaries
- MediatR for application commands and queries where useful
- FluentValidation for request validation
- Hangfire with PostgreSQL storage for scheduled reminders and reliable background jobs
- SignalR for in-app operational notifications where needed
- OpenAPI documentation
- Structured logging with correlation IDs and redaction of personal data

Avoid unnecessary microservices. This release should deploy as one Next.js web application, one ASP.NET Core API, and supporting infrastructure.

### Local and deployment infrastructure

- Dockerfiles for web and API
- Docker Compose for web, API, PostgreSQL/PostGIS, and any development-only dependencies
- Nginx reverse proxy example for a single production domain
- HTTPS-ready configuration
- Health endpoints for liveness and readiness
- `.env.example` files with no secrets
- Database backup and restore instructions
- GitHub Actions or equivalent CI for lint, type check, test, build, and migration validation

## 4. Suggested repository structure

Adapt to the repository if it already has a sound structure. For an empty repository, use a structure close to:

```text
apps/
  web/                    # Next.js
  api/                    # ASP.NET Core host
src/
  BuildingBlocks/
  Modules/
    Identity/
    Shops/
    Services/
    Professionals/
    Availability/
    Bookings/
    Customers/
    Reviews/
    Subscriptions/
    Notifications/
    QrAnalytics/
    Administration/
tests/
  UnitTests/
  IntegrationTests/
  ArchitectureTests/
  E2E/
docs/
```

The exact folder names are flexible. The boundaries and dependency direction are not.

## 5. Design implementation requirements

Recreate the imported TRIMME design faithfully:

- Extract colors, typography, spacing, radii, borders, shadows, responsive breakpoints, icon treatment, card patterns, form patterns, navigation, status chips, and table styles into named tokens.
- Build a reusable component library instead of duplicating page markup.
- Preserve the premium steel-blue identity from the logo and design.
- Implement real responsive behavior at approximately 390px, 768px, and 1440px widths.
- Customer screens are mobile-first.
- Dashboards use a desktop sidebar and a usable mobile drawer or compact navigation.
- Convert dense tables into readable cards or controlled horizontal layouts on small screens.
- Use Arabic production-like content rather than lorem ipsum.
- Correctly mirror directional UI in RTL, including arrows, breadcrumbs, drawers, calendars, and progress indicators.
- Keep phone numbers, times, prices, and mixed Arabic/English text readable with correct bidi handling.
- Meet WCAG AA color contrast, keyboard navigation, visible focus, meaningful labels, accessible dialogs, and minimum touch targets.
- Implement loading skeletons, empty states, validation errors, API errors, permission-denied states, expired-session handling, and optimistic-action rollback where relevant.
- Use `next/image` and responsive image sizing. Do not hotlink fragile images from the design artifact.

Do not ship one giant page that switches screens using local JavaScript. Every meaningful screen needs a real route and must obtain its state from the API or an explicit typed development fixture.

## 6. Localization and SEO

- Primary URL locale: `/ar`; secondary locale: `/en`.
- Add correct `dir`, language metadata, locale-aware date/time/number/currency formatting, and translated validation messages.
- Do not hardcode Arabic UI strings inside components. Keep complete Arabic and English message catalogs with key-parity tests.
- Public pages require localized metadata, canonical URLs, `hreflang`, Open Graph data, `robots.txt`, and a sitemap.
- Add appropriate JSON-LD for the platform, local businesses, breadcrumbs, and aggregate ratings only when backed by real stored data.
- Private customer/shop/admin dashboards must not be indexed.

Use the configured operating country, currency, and time zone. For seed/demo data, use Saudi Arabia, SAR, and `Asia/Riyadh`. Store instants in UTC and convert at system boundaries.

## 7. Authorization, tenancy, and privacy — non-negotiable

Authorization must be enforced by the API and data-access layer, not only by hidden frontend buttons.

### Customer

- May manage only their own profile and bookings.
- May rate only a completed booking they own, once per booking.

### Shop

- May access only its own shop, schedules, professionals, services, and bookings.
- May create services for its own shop and edit their Arabic/English names, descriptions, prices, durations, availability, and active state.
- May deactivate or archive one of its own services, but a service referenced by booking history must not be physically deleted or allowed to corrupt historical records.
- May see the customer name and appointment details required for operations.
- Must never receive the customer's phone number in any shop API DTO, log, export, SignalR message, HTML response, or browser payload.
- May not export customer data.
- May update operational booking states, manage working hours/breaks/time off, create walk-ins, and pause online bookings.
- May not create, delete, or move professionals.
- May not change a professional's core profile or assign services to a professional.
- Must never see, query, change, reference, or infer another shop's profile, services, professionals, schedules, bookings, customers, analytics, or subscription data, including by guessing or changing an identifier.

### Platform Admin

- Manages shops and creates their accounts.
- Manages professionals and assigns each professional to exactly one shop.
- When creating or editing a professional, can record the professional's mobile/WhatsApp number so the system can send automated booking notifications and reminders directly to that professional.
- Can enable/disable WhatsApp notifications for a professional and correct the number without exposing it on public pages or to customers.
- Can view and support shop-owned services across the platform, moderate or deactivate them when required, and manage professional-service assignments. Any admin change to a shop-owned service must be explicit, permission-gated, and audit logged.
- Manages platform-level service categories and any platform-controlled package definitions without imposing a shared service price or duration on shops.
- Manages all bookings, customers, reviews, subscriptions, WhatsApp templates/logs, QR analytics, roles, permissions, and settings.

### SuperAdmin subscription authority

- SuperAdmin can create, edit, activate, deactivate, archive, order, and publish subscription plans.
- SuperAdmin controls each plan's Arabic/English name and description, price, currency, billing duration/interval, features, limits, trial/grace settings when used, and availability to new shops.
- Subscription plans and their prices must be fully data-driven and editable from the SuperAdmin dashboard; do not hardcode plan names, prices, durations, or limits in frontend or backend code.
- SuperAdmin can assign plans, renew subscriptions, apply an audited shop-specific override, and view complete price/renewal history.
- Subscription-plan pricing is separate from the prices customers pay for salon/barber services.
- Ordinary shop accounts and non-authorized admins cannot alter subscription plans or plan prices.

There is no “transfer barber between shops” feature. Do not implement, display, document, or scaffold a transfer action or a shared professional across shops. Each professional belongs to exactly one shop, and the professional's shop association is fixed in v1.

### Technical enforcement

- Resolve the current shop/tenant from authenticated claims, never from a trusted client-supplied shop ID.
- Apply tenant-aware EF Core query filters or an equally strong centralized mechanism to every shop-owned entity.
- Stamp tenant identifiers server-side on writes.
- Use composite constraints/foreign keys where appropriate so a shop-owned row cannot reference another shop's data.
- Keep administrative bypass explicit and isolated; never disable filters casually inside ordinary handlers.
- Add integration tests for cross-shop read, update, delete, booking, SignalR, and enumeration/IDOR attempts.
- Add DTO contract tests proving customer phone numbers are absent from every shop-facing response.
- Treat phone numbers and WhatsApp identifiers as sensitive personal data; redact them from logs and protect stored values appropriately.

## 8. Core domain model

Create a normalized schema and EF Core migrations for at least the following concepts. Names may change if the model remains clear:

- User, Role, Permission, UserRole, RefreshSession
- CustomerProfile
- Shop, ShopUser, ShopLocation, ShopOpeningHour, ShopClosure
- Professional, ProfessionalContact/WhatsAppSettings, ProfessionalWorkingHour, ProfessionalBreak, ProfessionalTimeOff
- ServiceCategory or optional catalogue taxonomy managed by platform admins
- ShopService owned by exactly one shop, with shop-managed localized name, description, price, duration, active/archive state, and booking rules
- ServicePackage and package items with explicit ownership and permission rules matching the implemented design
- ProfessionalService assignment
- Booking, BookingStatusHistory, BookingNote
- Review
- SubscriptionPlan, SubscriptionPlanPrice/PriceHistory, ShopSubscription, SubscriptionRenewal, SubscriptionOverride
- Notification, WhatsAppTemplate, WhatsAppDispatch
- QrCodeLink, QrVisit/Attribution
- AuditEntry
- OutboxMessage and idempotency records

Use strongly typed IDs or consistent UUIDs, UTC timestamps, audit fields, soft deletion only where its behavior is explicitly defined, and optimistic concurrency for bookings, schedules, services, subscriptions, and professionals.

Use PostgreSQL spatial types and a spatial index for shop coordinates and distance queries. Store map latitude/longitude through a proper geography point rather than comparing raw strings.

Shop location capture must follow the interaction shown in the imported design: location search/manual address, use-current-location when permission is granted, click or drag the map pin to the exact entrance, display the resolved address and coordinates for confirmation, and save the final point. Treat the design's location picker as the visual and interaction source of truth while keeping the map provider behind an adapter.

Professional mobile/WhatsApp numbers must be normalized to E.164, validated, stored as sensitive data, masked in administrative lists, redacted from logs, and available only to explicitly authorized admin commands and the notification worker. The number is not part of the public professional DTO and must never be sent to customers or other shops.

## 9. Authentication and session security

- Customers can self-register and sign in using the flow shown in the imported design, with a verified mobile number available for WhatsApp messages.
- Shop accounts are created or invited by the platform admin.
- Admin accounts are seeded only in development through explicit environment variables or a safe one-time bootstrap flow.
- Use short-lived access sessions plus rotating refresh sessions in Secure, HttpOnly, SameSite cookies. Never store bearer or refresh tokens in `localStorage`.
- Support sign-out, revoke-all-sessions, password reset, email/mobile verification as applicable, rate limiting, lockout, and secure password hashing through ASP.NET Core Identity.
- Protect state-changing requests against CSRF when cookie authentication is used.
- Configure strict CORS for known origins only.
- Add security headers, upload validation, request size limits, and centralized problem-details responses.

## 10. Services and packages — final business rule

Services must be flexible, not hardcoded in code or fixed by the initial design.

- Each shop can create, edit, activate, deactivate, and archive its own services.
- Each shop controls its own service names, descriptions, prices, durations, availability, and ordering.
- Service records are tenant-owned. There is no global price or global duration that silently overwrites a shop's values.
- A shop can manage only its own services and must never receive another shop's service records from the API.
- Platform admins can manage categories, moderate shop services, and assist with an audited override when authorized.
- The platform admin assigns which professionals provide each shop service unless a later approved permission explicitly delegates assignment to the shop.
- Existing bookings keep a snapshot of the booked service name, price, and duration so historical records do not change when the service configuration changes.
- Packages have a clear duration and price and expand into their configured service items for reporting without creating overlapping professional appointments.

## 11. Availability engine and booking integrity

Implement availability as a backend domain service. The frontend must never invent availability.

Availability must combine:

- Shop opening hours
- Shop closures and paused-booking state
- Professional working hours
- Professional breaks
- Professional time off and vacations
- Service duration
- Existing non-cancelled appointments
- Configurable minimum lead time and booking horizon
- A configurable slot step that supports times such as 8:05, 8:10, 8:15, and 8:30

The API returns only genuinely bookable slots. Past, conflicting, outside-hours, break, and time-off slots must not be returned as available.

Prevent double booking under concurrency at both application and database levels:

- Recheck availability in a transaction when creating/rescheduling a booking.
- Use an appropriate PostgreSQL exclusion constraint over professional and appointment time range for active booking statuses, or an equivalently strong database guarantee.
- Make create/reschedule commands idempotent.
- Return a typed conflict response if another request wins the slot.
- Add tests that issue concurrent booking requests for the same professional and time and prove that only one succeeds.

Use an explicit state machine, for example:

`Pending -> Confirmed -> Arrived -> Completed`

With valid side transitions to customer cancellation, shop cancellation, or no-show. Reject invalid transitions server-side and record every transition with actor and timestamp.

The shop may create a walk-in booking, but it must pass the same collision checks as an online booking.

## 12. Required customer experience

Implement the applicable imported screens and routes for:

- Landing/home page
- Sign up, sign in, verification, forgot/reset password
- Location permission/manual location selection
- Nearby shops and search
- List/map view with distance sorting
- Filters for service, rating, distance, open-now, date, and availability where supported by the design
- Shop page with gallery, description, rating, address, map, distance, opening status, services, packages, professionals, and reviews
- Professional profile within a shop
- Booking wizard: service/package, professional, date, slot, review, confirmation
- Upcoming and previous bookings
- Booking details, allowed cancellation, and rescheduling
- Post-completion review
- Notifications
- Favorites only if present in the imported design; otherwise keep them out of the MVP
- Profile, language, and security/session settings
- Public QR destination for a shop or professional with attribution

Do not add online payment or checkout UI.

## 13. Required shop dashboard

Implement:

- Operational overview
- Today's appointments
- Day/week calendar
- Appointment list and details
- Valid status changes
- Walk-in creation
- Shop opening hours
- Professional schedule visibility
- Breaks, vacations, and time off
- Temporarily pause/resume online bookings
- Create and edit the shop's own services, including localized name, description, price, duration, active state, and ordering
- Archive/deactivate services safely while preserving booking history
- Subscription status and expiry visibility
- Notifications
- Shop public-profile editing only for fields permitted by the admin policy
- Exact shop-location editing through the designed map/pin interaction when permitted

Keep phone numbers out of all shop surfaces and browser payloads. Do not add customer export.

## 14. Required admin dashboard

Implement:

- Overview with useful operational KPIs, not decorative charts
- Shops: list, create, edit, activate/suspend, location, account setup, public profile
- Professionals: list, create, edit, disable, assign to one shop, record/mask the professional's WhatsApp number, enable notifications, and assign services; no transfer action
- Services and packages: platform-wide visibility, category/package administration, moderation, audited support override, and professional-service assignment; ordinary service CRUD remains shop-owned
- Bookings: global search, filters, details, history, and authorized intervention
- Customers: booking count, upcoming/previous bookings, last booking, registration date, and protected contact data
- Reviews moderation
- Subscription plans and pricing for SuperAdmin, plus shop subscription assignment, renewal, overrides, status, and complete history
- QR generation and visit/booking attribution analytics
- WhatsApp templates, editable message content, dispatch log, retries, and failure status
- Roles and permissions
- Audit activity
- Platform settings, booking policy, locale, currency, time zone, reminder offset, and map defaults

## 15. Subscriptions

Each shop has a platform subscription based on a SuperAdmin-managed subscription plan:

- SuperAdmin can create multiple plans and control each plan's localized name/description, price, currency, duration or billing interval, features, limits, active/published state, and display order.
- Plan-price changes must be versioned and must not silently rewrite the commercial history of an already activated subscription.
- An authorized admin chooses the plan, activation date, and subscription duration, such as 30 days, and may apply an explicit audited shop-specific override.
- Record start date, end date, status, and every renewal.
- Supported statuses: Active, ExpiringSoon, Expired, Suspended.
- The expiring-soon threshold is configurable.
- Display warnings to the admin and the relevant shop.
- Do not silently delete or alter future bookings when a subscription expires.
- Keep enforcement behavior explicit in platform settings and covered by tests.
- Online collection of subscription fees is not part of v1; activation and renewal are recorded manually by authorized admins until a payment gateway is added in a later approved phase.

## 16. WhatsApp and background jobs

Create a provider abstraction and a development fake. Prepare the production adapter for the Meta WhatsApp Business Cloud API, but do not require real credentials for local development.

Message content must be editable by an authorized admin using separate, versioned Arabic and English templates for each audience and event. Do not hardcode the final message text in jobs or handlers.

The template editor must support preview, validation, activation, version history, and a safe whitelist of placeholders such as customer name, professional name, shop name, service name, booking date, booking time, and time remaining. Customer templates and professional templates are independent so the admin can change their wording separately.

Send or schedule messages for:

- Customer booking confirmation with the booking details
- Customer booking update/cancellation
- Customer reminder 30 minutes before the appointment
- Professional new/confirmed-booking alert containing customer name, date, time, service, and shop—but never customer phone number. Example intent: “You have a new booking with [customer] at [time].”
- Professional booking update/cancellation
- Professional reminder exactly 30 minutes before the appointment. Example intent: “You have a booking with [customer] in 30 minutes.”

The professional receives the same relevant booking lifecycle notifications as the customer, but through professional-specific templates and wording. A professional notification is sent only when that professional has a valid enabled WhatsApp number.

Use Hangfire for scheduled reminders and retries. Use an outbox pattern so a committed booking and its notification intent cannot drift apart. Jobs must be idempotent, observable, retry safely with backoff, and record provider response/failure without logging sensitive tokens or full phone numbers.

When an appointment is changed or cancelled, cancel obsolete reminder jobs and schedule the correct replacements. Do not send messages for rolled-back transactions or seed data unless explicitly running a notification test.

Store which template version rendered each dispatch. A later template edit affects only future messages and must not rewrite historical dispatch content. Provide an admin preview/test action that uses an explicit safe test recipient and never a production customer's number by accident.

## 17. Ratings, QR, notifications, and analytics

- A review is allowed only once, by the booking's customer, after completion.
- Maintain rating aggregates transactionally or through reliable projection jobs.
- Generate unique QR destinations for shops and professionals.
- Track QR visits and attribute bookings when possible without invasive tracking.
- Provide in-app notifications for operational events and mark-read behavior.
- Use SignalR for live shop/admin appointment updates, scoped so no shop receives another shop's events.
- Admin dashboard KPIs may include appointments today, completion rate, cancellations, no-shows, active shops, expiring subscriptions, active professionals, new customers, popular services, and top shops.

## 18. API conventions

- Version the public API under `/api/v1`.
- Use resource-oriented endpoints plus explicit action endpoints where a state transition is clearer.
- Use RFC 7807 Problem Details with stable application error codes.
- Add pagination, filtering, sorting, search, and safe maximum page sizes.
- Use request/response DTOs; never expose EF entities.
- Generate and consume an OpenAPI client or shared contract so frontend types do not drift.
- Support cancellation tokens end-to-end.
- Add idempotency keys for booking creation/rescheduling and externally triggered message operations.
- Add rate limits for authentication, search, availability, booking, review, and QR analytics endpoints.
- Return ETags or concurrency versions for hot editable resources where appropriate.

## 19. Testing requirements

Do not mark the implementation complete without automated coverage.

### Backend unit tests

- Availability calculations across opening hours, breaks, time off, service duration, and midnight/date boundaries
- Booking status state machine
- Subscription status calculation
- Subscription-plan price versioning, permissions, and non-retroactive history
- Review eligibility
- Template placeholder validation
- Professional WhatsApp number normalization/masking and notification eligibility
- Separate customer/professional template rendering

### Backend integration tests using real PostgreSQL/PostGIS

- EF migrations from an empty database
- Customer, shop, and admin authorization
- Cross-shop isolation and IDOR attempts
- Customer phone absence from shop DTOs
- Spatial nearby-shop query
- Shop-owned service create/edit/archive flows, including price and duration changes
- Cross-shop service isolation for reads and every mutation
- SuperAdmin-only subscription-plan and plan-pricing operations
- Booking/rescheduling concurrency and overlap constraint
- Outbox and reminder idempotency
- Professional new-booking and 30-minute reminder dispatches with the correct audience/template
- Rescheduling/cancellation removes obsolete customer and professional reminder jobs
- Template edits apply to future dispatches while preserving historical template-version evidence
- Rating uniqueness and completed-booking requirement

### Frontend tests

- RTL and LTR rendering
- Permission-aware navigation
- Forms and API error mapping
- Booking step state
- Loading, empty, error, and unauthorized states
- Responsive critical components

### Playwright flows

1. Customer registers/signs in, discovers a shop, selects service/professional/date/slot, books, views, and cancels or reschedules according to policy.
2. Shop signs in, creates and edits its own service price/duration, sees only its own bookings and services, creates a walk-in, changes valid statuses, and cannot access another shop or a customer phone number.
3. Admin creates a shop, sets its exact location using the designed map pin, creates a professional with a masked WhatsApp number, assigns the professional to that one shop and its service, and manages the shop's subscription without any barber-transfer action.
4. Admin edits separate customer/professional WhatsApp templates; a new booking produces correctly rendered fake-provider dispatches and 30-minute reminder jobs for both audiences without exposing either phone number in unauthorized UI/API payloads.
5. SuperAdmin creates a subscription plan and price, assigns it to a shop, changes the plan's future price, and proves the existing subscription history did not change retroactively.
6. Two concurrent customers attempt the same slot and exactly one succeeds.
7. Completed booking permits one review; incomplete or foreign booking does not.

## 20. Seed and demo data

Create deterministic development seed data behind an explicit development-only command:

- One platform admin
- At least two separate shops in Riyadh
- Separate professionals for each shop
- Different services, packages, prices, and durations per shop
- Working hours, breaks, time off, and sample appointments
- Active, expiring, expired, and suspended subscription examples
- Multiple subscription plans and versioned price examples
- Customer accounts and completed/upcoming bookings
- Reviews, QR links, notification history, and fake WhatsApp dispatches
- Valid fake E.164 professional numbers and editable customer/professional template versions

Document demo credentials in a local development file that is excluded from production builds. Never commit real passwords or API credentials.

## 21. Quality and performance gates

- Enable nullable reference types, warnings-as-errors for project code, strict TypeScript, ESLint, formatting, and architecture tests.
- Avoid N+1 database queries and unbounded list endpoints.
- Add database indexes for tenant/shop ownership, booking time ranges, statuses, normalized search fields, subscriptions, outbox processing, and spatial location.
- Cache only public/read-mostly data with explicit invalidation; never cache authorization-sensitive responses across tenants.
- Use pagination or virtualization for large admin lists.
- Do not expose stack traces or secrets.
- Do not log raw authentication tokens, full phone numbers, or message payloads containing personal data.
- Ensure `docker compose up --build` can start the development system from documented prerequisites.

## 22. Required documentation

Create or update:

- Root `README.md` with architecture, prerequisites, setup, migrations, seed, run, test, and deployment instructions
- `docs/architecture.md`
- `docs/domain-model.md`
- `docs/permissions-matrix.md`
- `docs/availability-and-booking.md`
- `docs/whatsapp-integration.md`
- `docs/deployment.md`
- `docs/backup-restore.md`
- `docs/design-deviations.md`
- OpenAPI output or instructions to generate it

Include a Mermaid diagram for the deployed topology and another for the booking/reminder lifecycle.

## 23. Definition of done

The work is complete only when:

- The selected Claude Design files have been imported and their relevant screens are implemented as real reusable Next.js routes/components.
- Arabic RTL and English LTR both work.
- Customer, shop, and admin journeys are connected to the real API and database.
- Tenant isolation and phone privacy are proven by automated tests.
- Availability is server-calculated and overlapping bookings are prevented under concurrency.
- Shops can create and manage only their own services, prices, and durations; cross-shop access is blocked and tested.
- The barber-transfer feature does not exist.
- WhatsApp workflows work through a fake provider locally and are ready for Meta configuration.
- Every professional can have an admin-managed protected WhatsApp number, and verified tests cover new-booking, update/cancellation, and 30-minute reminder messages using editable professional-specific templates.
- Exact shop location can be captured and edited using the imported design's map/pin workflow and is stored spatially for nearby search.
- SuperAdmin subscription-plan management, pricing, price history, assignment, renewal, overrides, and statuses work with permission tests.
- No v1 payment UI or charge flow exists, while the future payment seam is documented.
- Migrations, seed, test, lint, type check, production builds, and Docker Compose validation pass.
- The repository contains no real credentials, no production PII, and no known critical/high security issue.

At the end, provide a concise implementation report containing:

1. What was implemented
2. Important assumptions and documented design deviations
3. Database migrations created
4. Tests and build commands run with their results
5. Local run instructions
6. Remaining external configuration, especially domain/DNS, SMTP if used, maps provider, and Meta WhatsApp credentials/templates
7. Any intentionally deferred item, clearly separated from completed scope

Do not claim a screen, workflow, integration, or test is complete unless it exists and has been verified.

## Prompt to use at the beginning of every new Claude Code session

After Session 1 has created the planning files, use the following short prompt for each new session:

```text
Continue the TRIMME implementation from the repository's durable state.

Before changing code, read CLAUDE.md, the full TRIMME implementation specification, docs/implementation/MASTER_PLAN.md, docs/implementation/STATUS.md, docs/implementation/SESSION_HANDOFF.md, docs/implementation/DECISIONS.md, docs/implementation/TRACEABILITY.md, and the current phase file. Inspect git status, recent commits, migrations, and the real changed files. Re-run the previous phase's smallest decisive verification so you do not rely only on the prior chat or handoff.

Then continue the first approved phase that is not completed. Show me its current checklist score, completed items, remaining items, and verification plan. Work only on that phase. Update the checklist and all durable handoff files as you work. Do not start the next phase automatically.

At the end, run the phase gates, record factual evidence, update the files under docs/implementation/, and commit only if the phase is fully verified and the repository's git workflow permits it. Report what is complete, what remains, exact test results, migration status, commit hash, and the exact first action for the next session.
```
