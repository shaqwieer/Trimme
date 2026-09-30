# TRIMME — Status

- **Updated:** 2026-09-30 (Session 12)
- **Current phase:** 16 is complete and verified (Session 12). It delivers:
  - unique QR codes for shops and barbers, created and switched off/on by admins (audited, never deleted);
  - the QR landing `/q/{code}` (shop and barber variants), with the locale-less printed URL redirected by language (D-115);
  - privacy-preserving scan counting (no IP address; a per-day keyed hash) and first-party booking attribution within 7 days (D-114);
  - `/admin/qr` analytics (scans, credited bookings, conversion, per shop) with PNG/SVG/PDF files and the A5 poster;
  - `/shop/qr`, read-only for the owner.

  It is committed locally and not pushed. Next: Phase 17, hardening.
- **Platform progress:** 1700 / 1900 points (Phases 00–16)

| Phase | Status | Points |
|---|---|---|
| 00 Discovery & plan | [x] | 100/100 |
| 01 Backend & infra foundation | [x] | 100/100 |
| 02 Web foundation | [x] | 100/100 |
| 03 Design-system components | [x] | 100/100 |
| 04 Identity & sessions | [x] | 100/100 |
| 05 Tenancy, privacy & audit | [x] | 100/100 |
| 06 Shops, locations & professionals | [x] | 100/100 |
| 07 Services & packages | [x] | 100/100 |
| 08 Subscriptions & settings | [x] | 100/100 |
| 09 Schedules & availability | [x] | 100/100 |
| 10 Booking core | [x] | 100/100 |
| 11 Public discovery | [x] | 100/100 |
| 12 Customer booking & account | [x] | 100/100 |
| 13 Shop dashboard | [x] | 100/100 |
| 14 Admin dashboard | [x] | 100/100 |
| 15 WhatsApp & notifications | [x] | 100/100 |
| 16 QR & attribution | [x] | 100/100 |
| 17 Hardening | [ ] | 0/100 |
| 18 Regression & handover | [ ] | 0/100 |

## Decisions resolved with the user (D-037)

| ID | Decision |
|---|---|
| D-004 | In-house command/query dispatcher; MediatR is not used |
| D-005 | Customers sign in passwordless with mobile + 6-digit OTP over WhatsApp (fake in dev). Staff sign in with email + password, with reset over email (Mailpit in dev) |
| D-006 | Per-shop `RequireManualConfirmation`, default off, so online bookings are auto-confirmed |
| D-007 | OpenStreetMap: MapLibre with OSM tiles and a Nominatim-compatible geocoder. Production needs a self-hosted or OSM-based host, because the public OSM services' usage policies forbid heavy traffic |

## Decisions made at the user's request
| ID | Decision |
|---|---|
| D-064 | Photos and other media are stored in PostgreSQL (`media.media_files`), not on disk (Session 4) |
| D-078 | Keep the default: shops without a subscription in force are hidden and take no online bookings; the warning-only setting stays available for a temporary rollout (Session 4) |
| D-081 | Custom durations and back-dated starts need a SuperAdmin override with an explicit total price and a reason, fully audited (Session 4) |

## Open decisions
None are blocking. Phase 09 recorded D-082…D-084, Phase 10 D-085…D-089, Phase 11 D-090…D-095, Phase 12 D-096…D-098, Phase 13 D-099…D-100 and Phase 14 D-101…D-107, Phase 15 D-108…D-113 and Phase 16 D-114…D-115 as their own design decisions (D-096 supersedes the inline part of D-033; D-100 refines D-034); D-012 and D-015 are now Accepted; D-017 (reviews) is completed by D-102. The product assumptions D-012…D-035 remain overridable defaults. Phase 08 accepted D-013/D-014 as settings (D-076, D-078); the user confirmed D-078 and decided D-081.

## Blockers
None.
