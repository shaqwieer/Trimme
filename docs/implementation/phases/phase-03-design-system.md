# Phase 03 — Design-system component library

**Status:** [ ] · **Score:** 0/100

## Goal and user-visible outcome
A reusable, accessible TRIMME component library that faithfully reproduces `ds-foundations` and `ds-components`, together with a development-only gallery at `/[locale]/dev/components`. Later phases compose screens from these components instead of duplicating markup.

## Prerequisites
Phase 02 complete: tokens, fonts, shells and reference screenshots.

## In scope
Components, with anatomy, variants and states per `design/analysis/01-design-system-and-docs.md` §2:
- **Buttons:** primary, secondary, ghost, danger, icon; sizes; all 8 states.
- **Inputs:** text, phone with a fixed +966 prefix and LTR isolation, OTP boxes, select, textarea, checkbox, radio card, switch, dual range slider.
- **Selection and navigation:** chips (filter and category), segmented control, tabs.
- **Status and feedback:** badges, booking status chips from the STATUS map (with the split cancelled statuses), subscription status chips, rating stars (display and input), toast, inline alert/WarnNote.
- **Cards:** shop card, service row, professional card, KPI tile, stat, empty state, error state, permission-denied state, expired-session state, skeleton shimmer.
- **Booking and calendar:** date strip, slot grid (period groups), stepper/progress (RTL-mirrored), calendar cells.
- **Overlays:** dialog/modal and side drawer/bottom sheet with focus trap, restore and Escape handling.
- **Data display:** table with responsive card fallback, pagination, timeline (audit/history), avatar and upload drop zone (cover/avatar; validation UI only).
- **Brand and icons:** logo, and a Lucide icon mapping for all 46 design icons with stroke 1.75 and directional mirroring for chevrons/arrows.

Supporting work:
- Headless primitives (e.g. Radix/Ariakit) styled with TRIMME tokens only.
- Form helpers: React Hook Form + Zod integration, a field-error component, and a problem-details → field mapping (R-WEB-14).
- Axe accessibility checks in Vitest (jsdom) and a Playwright axe pass on the gallery.

## Explicitly out of scope
Page screens and data fetching.

## Checklist (100 points)
- [ ] 3.1 (5) Re-validate against repo state; refine checklist.
- [ ] 3.2 (8) Icon mapping and directional mirroring.
- [ ] 3.3 (14) Buttons, inputs, phone, OTP, select, checkbox, radio card, switch, range slider.
- [ ] 3.4 (10) Chips, segmented control, tabs, badges, status chips (booking and subscription), rating.
- [ ] 3.5 (12) Cards: shop, service row, professional, KPI, stat.
- [ ] 3.6 (10) Date strip, slot grid, stepper, calendar cell primitives.
- [ ] 3.7 (12) Dialog, drawer/sheet, toast, confirm dialog (DV-A20); focus management tests.
- [ ] 3.8 (8) Table ↔ card responsive pattern, pagination, timeline.
- [ ] 3.9 (8) State components: empty, error, permission-denied, expired-session, skeletons (R-WEB-08, DV-A25).
- [ ] 3.10 (5) RHF + Zod form kit + problem-details field mapping test.
- [ ] 3.11 (5) Gallery route (dev only) + 390/768/1440 screenshots compared with `design/reference`; deviations noted.
- [ ] 3.12 (3) Axe: 0 serious/critical on the gallery; control files; commit.

## Files/modules expected to change
`apps/web/src/components/**`, `apps/web/src/app/[locale]/dev/components/**`, `apps/web/src/lib/forms/**`, tests.

## Data model and migration impact
None.

## API contracts and UI routes
`/[locale]/dev/components`, excluded from production builds by an environment guard.

## Security, tenancy, privacy, RTL, a11y, responsive
- The phone input never logs its value.
- All components use logical properties and are tested in both directions.
- Minimum 44px targets.
- Colour is not the sole carrier of status: every status has a label and an icon.

## Tests and verification commands
```
pnpm -C apps/web test
pnpm -C apps/web lint
pnpm -C apps/web typecheck
pnpm exec playwright test --project=gallery
```

## Acceptance criteria
- Every component card in ds-components has a React equivalent in the gallery.
- Axe reports no serious or critical issues.
- RTL and LTR screenshots are captured.

## Rollback / recovery
Revert the commit.

## Completion evidence
_(fill)_

## Remaining risks → next phase
Next: Phase 04 — Identity, sessions, roles and permissions. It needs D-005.
