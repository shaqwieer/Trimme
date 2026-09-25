# TRIMME — Status

- **Updated:** 2026-09-25 (Session 1)
- **Current phase:** 00 is complete. Phase 01 has not started and is waiting for the user to approve the plan.
- **Platform progress:** 100 / 1900 points (Phase 00 only)

| Phase | Status | Points |
|---|---|---|
| 00 Discovery & plan | [x] | 100/100 |
| 01 Backend & infra foundation | [ ] | 0/100 |
| 02 Web foundation | [ ] | 0/100 |
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

## Open decisions (user)

| ID | Question | Blocks |
|---|---|---|
| D-004 | MediatR or an in-house dispatcher | Phase 01 |
| D-005 | Customer authentication model | Phase 04 |
| D-006 | Whether online bookings are auto-confirmed or start as Pending | Phase 10 |
| D-007 | Production maps/geocoding provider | Production config for Phase 06; the dev adapter is not blocked |

Each has a recommended default in `DECISIONS.md`.

## Blockers
None. Phase 01 can proceed under the D-004 default once the user approves the plan.
