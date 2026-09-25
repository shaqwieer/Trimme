# Phase 16 — QR codes & attribution analytics

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
Admins, and optionally shops, generate unique QR codes for shops and professionals. Scanning one opens the designed QR landing page. Visits are counted, and bookings made from them are attributed without invasive tracking. Admins see the resulting QR analytics.

## Prerequisites
Phase 15 complete.

## In scope
- **`QrCodeLink`:** unique short code, target (shop or professional), campaign label, active flag, created by.
- **`QrVisit`:** code, timestamp, coarse user agent class, locale, and a salted hash of the IP truncated per day. Raw IP addresses are not stored, and there is no third-party tracking.
- **Attribution:** a first-party, HttpOnly attribution cookie holding the QR visit id, with a 7-day configurable TTL. It is attached to a booking's `Channel`/`AttributionQrLinkId` on create (R-QR-02).
- **API:**
  - `GET /public/q/{code}`: resolves the target, records the visit, and is rate-limited under `qr`.
  - `/admin/qr`: CRUD and analytics (visits, bookings and conversion by code, shop and period).
  - `/shop/qr` for downloads, if D-018 or the admin policy permits (open question 8 in `design/analysis/03`).
- **Generation:** server-side PNG, SVG and PDF, plus an A5 poster using the logo and TRIMME styling (c-qr print section).
- **Pages:**
  - `/q/[code]` (c-qr): the "entered via shop code" variant with today's first slots, services and a CTA into the wizard. It is noindex, with the canonical URL pointing to the shop or professional page.
  - The professional QR variant (DV-A14).
  - `/admin/qr` management and analytics.
- **Seed:** QR links and visits.
- **Tests:**
  - Code uniqueness.
  - The visit is recorded without a raw IP.
  - Attribution is set on booking, and is ignored after the TTL.
  - E2E: scan → book → attributed.

## Explicitly out of scope
Marketing campaigns beyond labels.

## Checklist (100 points)
- [ ] 16.1 (5) Re-validate; refine checklist.
- [ ] 16.2 (12) QR entities + unique codes + migration + tests (R-QR-01).
- [ ] 16.3 (14) Visit tracking (privacy-preserving) + attribution cookie + booking attribution + tests (R-QR-02).
- [ ] 16.4 (12) QR image/PDF/A5 generation.
- [ ] 16.5 (16) `/q/[code]` landing for the shop and professional variants.
- [ ] 16.6 (16) Admin QR management + analytics.
- [ ] 16.7 (5) Shop QR download (if permitted).
- [ ] 16.8 (8) Seed + E2E scan→book→attribution.
- [ ] 16.9 (12) Axe, viewports, gates, control files, commit.

## Files/modules expected to change
`src/Modules/QrAnalytics/**`, the Bookings attribution fields, web `/q/**`, `/admin/qr/**`.

## Data model and migration impact
- Schema `qr`.
- Booking attribution columns.
- Migration `0013_Qr`.

## API contracts and UI routes
As listed.

## Security, tenancy, privacy, RTL, a11y, responsive
- No invasive tracking; IPs are hashed.
- The attribution cookie is first-party and HttpOnly.
- Poster print styles support RTL.

## Tests and verification commands
```
dotnet test --filter Category=Qr
pnpm exec playwright test qr
```

## Acceptance criteria
- A scanned QR produces a visit.
- A booking within the TTL is attributed.
- Analytics numbers match the seeded data.

## Rollback / recovery
Revert the commit.

## Completion evidence
_(fill)_

## Remaining risks → next phase
Next: Phase 17 — Hardening.
