# TRIMME — Status

- **Updated:** 2026-09-25 (Session 2)
- **Current phase:** 02 is implemented and verified locally, including on a clean Linux clone. Items 1.12 and 2.12 wait for the first green GitHub Actions run, which needs a push. Next: Phase 03, the design-system component library.
- **Platform progress:** 289 / 1900 points. 11 points are withheld until CI runs green.

| Phase | Status | Points |
|---|---|---|
| 00 Discovery & plan | [x] | 100/100 |
| 01 Backend & infra foundation | [~] | 94/100 (CI run pending) |
| 02 Web foundation | [~] | 95/100 (CI run pending) |
| 03 Design-system components | [ ] | 0/100 |
| 04 Identity & sessions | [ ] | 0/100 |
| 05 Tenancy, privacy & audit | [ ] | 0/100 |
| 06 Shops, locations & professionals | [ ] | 0/100 |
| 07 Services & packages | [ ] | 0/100 |
| 08 Subscriptions & settings | [ ] | 0/100 |
| 09 Schedules & availability | [ ] | 0/100 |
| 10 Booking core | [ ] | 0/100 |
| 11 Public discovery | [ ] | 0/100 |
| 12 Customer booking & account | [ ] | 0/100 |
| 13 Shop dashboard | [ ] | 0/100 |
| 14 Admin dashboard | [ ] | 0/100 |
| 15 WhatsApp & notifications | [ ] | 0/100 |
| 16 QR & attribution | [ ] | 0/100 |
| 17 Hardening | [ ] | 0/100 |
| 18 Regression & handover | [ ] | 0/100 |

## Decisions resolved with the user (D-037)

| ID | Decision |
|---|---|
| D-004 | In-house command/query dispatcher; MediatR is not used |
| D-005 | Customers sign in passwordless with mobile + 6-digit OTP over WhatsApp (fake in dev). Staff sign in with email + password, with reset over email (Mailpit in dev) |
| D-006 | Per-shop `RequireManualConfirmation`, default off, so online bookings are auto-confirmed |
| D-007 | OpenStreetMap: MapLibre with OSM tiles and a Nominatim-compatible geocoder. Production needs a self-hosted or OSM-based host, because the public OSM services' usage policies forbid heavy traffic |

## Open decisions
None are blocking. The product assumptions D-012…D-035 remain overridable defaults.

## Blockers
Nothing is blocked. **Pending user approval: push to GitHub** so the first CI run can verify items 1.12 and 2.12.
