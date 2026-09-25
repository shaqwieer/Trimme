# Phase 03 — Design-system component library

**Status:** [x] · **Score:** 100/100 · **Session:** 2 (2026-09-25)

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
- [x] 3.1 (5) Re-validate against repo state; refine checklist.
- [x] 3.2 (8) Icon mapping and directional mirroring.
- [x] 3.3 (14) Buttons, inputs, phone, OTP, select, checkbox, radio card, switch, range slider.
- [x] 3.4 (10) Chips, segmented control, tabs, badges, status chips (booking and subscription), rating.
- [x] 3.5 (12) Cards: shop, service row, professional, KPI, stat.
- [x] 3.6 (10) Date strip, slot grid, stepper, calendar cell primitives.
- [x] 3.7 (12) Dialog, drawer/sheet, toast, confirm dialog (DV-A20); focus management tests.
- [x] 3.8 (8) Table ↔ card responsive pattern, pagination, timeline.
- [x] 3.9 (8) State components: empty, error, permission-denied, expired-session, skeletons (R-WEB-08, DV-A25).
- [x] 3.10 (5) RHF + Zod form kit + problem-details field mapping test.
- [x] 3.11 (5) Gallery route (dev only) + 390/768/1440 screenshots compared with `design/reference`; deviations noted.
- [x] 3.12 (3) Axe: 0 serious/critical on the gallery; control files; commit.

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
pnpm lint && pnpm typecheck && pnpm --filter @trimme/e2e typecheck
pnpm format:check && pnpm --filter @trimme/e2e format:check
pnpm test                                    # Vitest (components, forms, tokens, i18n)
pnpm openapi:check && pnpm build
TRIMME_WEB_PORT=3300 bash infra/scripts/compose-smoke.sh
E2E_BASE_URL=http://localhost:3300 pnpm e2e  # incl. smoke/gallery.spec.ts
```
**Smallest decisive re-verification for the next session:** `pnpm test`, then `pnpm e2e` against the stack (includes the gallery axe and overflow checks).

## Acceptance criteria
- Every component card in ds-components has a React equivalent in the gallery.
- Axe reports no serious or critical issues.
- RTL and LTR screenshots are captured.

## Rollback / recovery
Revert the phase commit. The component library has no data or API dependencies.

## Completion evidence (observed 2026-09-25, Session 2)

**3.1 Re-validation:**
- `main` was in sync with `origin`.
- CI runs `36134142951` and `36134661434` were green.
- `pnpm test` passed 53/53, the Docker stack was healthy, and `pnpm e2e` passed 20/20.
- An advisor review set these constraints:
  - D-009, D-016 and D-037 are honoured in the components;
  - components stay safe to use as React Server Components;
  - there is a single dialog implementation;
  - a Direction provider supplies RTL/LTR;
  - no tailwind-merge;
  - axe run under jsdom has known limits.

**What was built** (`apps/web/src/components/ui/*`, `src/lib/forms/*`, `src/lib/i18n/localDate.ts`):

| Item | Components |
|---|---|
| 3.2 | `icons.tsx`: 46 design icons mapped to Lucide, stroke 1.75. Directional icons (`chevR`, `chevL`, `logout`, `play`) mirror in RTL. |
| 3.3 | `Button` (6 variants × 4 sizes; loading sets `aria-busy`), `ButtonLink`, `IconButton` (label required), `Field`, `TextField`, `PhoneField` (+966, LTR, accepts Arabic-Indic digits, outputs E.164), `OtpField` (6 digits, `one-time-code`), `SearchField`, `SelectField`, `TextareaField`, `Checkbox`, `RadioCard` (native radio), `Switch`, `RangeSlider` (Radix, RTL-aware). |
| 3.4 | `Chip`, `TagChip`, `RemovableChip`, `AddChip`, `SegmentedControl` (native radios), `Tabs` (Radix), `LinkTabs` (URL state), `Badge`, `StatusBadge` (D-016 booking enum + subscription enum), `RatingStars`, `RatingInput`. |
| 3.5 | `Card`, `ShopCard`, `ServiceOption`, `ProfessionalOption` (on-leave state), `AppointmentCard`, `KpiTile` (semantic delta), `Avatar` (Arabic initials skip the article), `ImagePlaceholder`, `RatingDistribution`, `BarChart` (follows the dataviz rules), `QrCard`. |
| 3.6 | `DateStrip`, `SlotGrid` (bookable slots only, grouped by Riyadh time periods, 44px targets), `Stepper`, `CalendarMonth` (weeks start Sunday, chevrons mirrored), plus `localDate` helpers (`YYYY-MM-DD` strings, no time-zone drift). |
| 3.7 | `Dialog`, `ConfirmDialog` (alertdialog; the safe option gets focus), `Sheet` (start/end/bottom, navy/light), `Tooltip`, `DropdownMenu`, `ToastProvider`/`useToast`. The DashboardShell drawer now uses `Sheet`, and `useFocusTrap` was deleted. |
| 3.8 | `ResponsiveTable` (captioned table with row headers, card list below 768px), `Pagination` (URL links, page window), `Breadcrumb`, `Timeline`. |
| 3.9 | `StateCard`, `EmptyState`, `ErrorState`, `PermissionDenied`, `ExpiredSession`, `InlineAlert`, `Skeleton`, `SkeletonList`. |
| 3.10 | Form kit: Zod schemas return message keys (`requiredText`, `saudiMobile`, `otpCode`, `email`); `useZodForm`; `FormTextField`/`FormTextareaField`/`FormPhoneField`; `useValidationMessage`; `applyProblemToForm`; `codeToMessageKey`. |
| 3.11 | Gallery at `/[locale]/dev/components` (dev-only, D-045), covering every card on the ds-components board. |
| 3.12 | Playwright axe: 0 serious or critical issues in ar and en, with contrast checked in a real browser. |

**Final gate run** (after the last change):

| Command | Result |
|---|---|
| `pnpm lint` (ESLint 10, `--max-warnings 0`) | PASS |
| `pnpm typecheck` + `pnpm --filter @trimme/e2e typecheck` | PASS |
| `pnpm format:check` (web + E2E) | PASS |
| `pnpm test` | PASS, **147/147** across 14 files |
| `pnpm openapi:check`, `pnpm build` | PASS |
| `TRIMME_WEB_PORT=3300 bash infra/scripts/compose-smoke.sh` (clean volume) | PASS: API ready, web `/ar` 200, proxy OK |
| `E2E_BASE_URL=http://localhost:3300 pnpm e2e` (run twice) | PASS, **28/28** both times |
| gitleaks `dir` scan | PASS, no leaks |

The 147 unit tests include:
- axe under jsdom, without the contrast check;
- RTL arrow-key behaviour of Radix Tabs;
- focus return for Dialog, Sheet and ConfirmDialog;
- toast timing using fake timers;
- `problemDetails_maps_to_field_errors`, using the exact API payload shape.

The E2E run adds `smoke/gallery.spec.ts`, which checks:
- axe with contrast, in ar and en;
- Tabs ArrowLeft in RTL;
- dialog focus trap and focus return;
- phone input stays LTR and accepts Arabic-Indic digits;
- tables become cards at 390px;
- **zero horizontal overflow on 6 pages × 4 widths**;
- 390/768/1440 captures in RTL and LTR.

**Defects the gates found, all fixed** (D-049):
- Real-browser axe flagged segmented-control contrast. Fixed with the tertiary text token, now covered by a token test.
- The Switch off-track was under 3:1, so it got a new token.
- `sr-only` inputs escaped the date strip scroller, causing 87–138px of overflow at 390px. Fixed with `relative` labels, and a permanent overflow test was added.
- Stepper labels overflowed at 768px. Fixed with `min-w-0` + `truncate`.
- The bar-chart peak label overlapped its bar. Fixed by capping bars at 85% and anchoring the label.
- Radix only returns focus to the element passed as `trigger`, so `Sheet` gained a `trigger` prop and DashboardShell uses it.
- Radix names menus after their trigger, so the redundant `label` prop was removed.
- **Flaky gallery tests:** under parallel load they hit 30s timeouts; layout was never the problem. They now use `test.slow()` and wait for `load` + `document.fonts.ready`. After the fix they passed 4/4 runs, then 2/2 on the Docker stack.
- **E2E formatting:** running Prettier over `tests/E2E` with defaults had reformatted it. The package now has its own Prettier config matching the web app, checked in CI.

**Visual comparison** against `design/reference/1440/ds-components.jpg` (at 1440 in Arabic, plus 390 and 768): these match the design:
- buttons, inputs, badges and tabs;
- the calendar (mirrored chevrons, closed days struck through) and the slot grid;
- service/professional cards and shop/appointment cards;
- rating with its distribution;
- KPI tiles and bar chart (navy peaks, RTL hour order);
- QR card and upload;
- hours editor, toast/dialog triggers and states;
- table with breadcrumb and pagination.

Intentional differences (D-049, DV-T07–T10):
- greys and the bar colour are contrast-corrected;
- weekday headers use full names from `Intl`;
- there are no disabled or reason-labelled slots;
- there is no "change barber" menu item.

**Commit:** `610ebec`.

## Remaining risks → next phase
- Screen-level responsive layouts and optimistic rollback are proven only once real screens use these components (Phases 11–14).
- Drawer and sheet motion is minimal; polish is scheduled for Phase 17.
- Next: Phase 04, identity, sessions, roles and permissions. D-005 is resolved as passwordless OTP for customers and email + password for staff (D-037).
