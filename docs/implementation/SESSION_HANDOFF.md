# TRIMME Session Handoff

- Updated at: 2026-09-25 (end of Session 2, after Phase 03)
- Branch: `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git)
- HEAD commit: docs commit recording the hash, on top of Phase 03 commit `610ebec`. Run `git log --oneline -6`.
- Working tree status: clean after those commits. **Phase 03 is not pushed**: pushing happens only when the user asks. The last pushed commit is `9bf12de`, and its CI run was green. The local Docker stack is **stopped**.
- Current phase: 03 is complete. Phase 04 has not started.
- Phase score: 100 / 100 (Phase 03)
- Last fully completed phase: 03, design-system component library

## Completed this session
- **Phases 01 and 02** are complete, with CI verified green on GitHub (runs `36134142951` and `36134661434`).
- **Phase 03, design-system component library:**
  - **Components** (`apps/web/src/components/ui/`):
    - icons;
    - Button, IconButton, the Field kit, Phone/OTP/Search/Select/Textarea, Checkbox, Switch, RangeSlider;
    - RadioCard, chips, SegmentedControl, Tabs, LinkTabs;
    - badges, StatusBadge, rating;
    - cards: shop, service, professional, appointment, KPI, avatar;
    - charts: rating distribution, bar chart, QR card;
    - booking controls: DateStrip, SlotGrid, Stepper, CalendarMonth;
    - overlays: Dialog, ConfirmDialog, Sheet, Tooltip, DropdownMenu, Toast;
    - data display: ResponsiveTable, Pagination, Breadcrumb, Timeline, UploadDropZone;
    - page states.
  - **Form kit** (`src/lib/forms/`): Zod schemas that return message keys, `useZodForm`, form fields and `applyProblemToForm`.
  - **Local-date helpers** in `src/lib/i18n/localDate.ts`.
  - **Shells:**
    - The DashboardShell drawer now runs on the Radix `Sheet`, and `useFocusTrap` was removed.
    - `CustomerShell` uses `IconButton`.
    - A Radix `DirectionProvider` wraps the locale layout and test renders.
  - **Gallery:** dev-only, at `/[locale]/dev/components`.
  - **Tests:**
    - 147 Vitest tests, including axe checks.
    - A Playwright gallery spec covering axe with contrast, RTL keyboard navigation, focus return, zero horizontal overflow at 390/768/1024/1440, and captures at three widths.
    - A Prettier config for E2E, with CI format and typecheck checks now covering E2E.
  - **Decisions and deviations:**
    - D-048 and D-049 added.
    - Design deviations DV-T04 and DV-T05 applied; DV-T07 to DV-T10 added.

## Verification evidence
- Command: `pnpm lint` / `pnpm typecheck` (web + E2E) / `pnpm format:check` (web + E2E)
  Result: PASS
- Command: `pnpm test`
  Result: PASS, 147/147 in 14 files
- Command: `pnpm openapi:check`; `pnpm build`
  Result: PASS
- Command: `TRIMME_WEB_PORT=3300 bash infra/scripts/compose-smoke.sh` (clean volume)
  Result: PASS. The API was ready, web `/ar` returned 200, and the proxy worked.
- Command: `E2E_BASE_URL=http://localhost:3300 pnpm e2e`, run twice
  Result: PASS, 28/28 both times
- Command: gitleaks `dir` scan
  Result: PASS, no leaks
- Visual comparison against `design/reference/1440/ds-components.jpg` at 1440, 768 and 390, in Arabic and English: matches, apart from the documented deviations.

## Database and migrations
- No new migrations. `Initial` is unchanged.

## Decisions added
- **D-048:** Radix (via `radix-ui`) is used only for Dialog/Sheet, DropdownMenu, Tooltip, Tabs and Slider. Forms use React Hook Form with Zod message keys; `tailwind-merge` is not used. Axe runs in jsdom (without contrast) plus Playwright. Components are RSC-safe where possible and follow D-009, D-016 and D-037.
- **D-049:** accessibility adjustments:
  - contrast fixes for the switch off-track and the segmented control;
  - bar-chart colour, plus alternative ways to read the data (values on hover, a data table) where bar contrast is low;
  - `sr-only` inputs contained inside their labels, and `min-w-0` on fieldsets;
  - Arabic initials that skip the definite article;
  - toast timing;
  - Radix focus return handled via the `trigger` prop.

## Known issues or blockers
- Phase 03 has not run on GitHub Actions yet because it has not been pushed. Every gate passed locally on the Docker stack.
- Port constraints on this machine are unchanged: web on 3300, DB on 5434, API on 8080.

## Exact next action
1. Start Phase 04 (`phases/phase-04-identity.md`). Re-validate first:
   - `git status`
   - `pnpm test`
   - `bash infra/scripts/compose-smoke.sh` with `TRIMME_WEB_PORT=3300`
   - `E2E_BASE_URL=http://localhost:3300 pnpm e2e`
2. Implement identity per D-037:
   - customers sign in passwordless with mobile + a 6-digit OTP (fake `IOtpSender`);
   - staff use email + password, with forgot/reset over email (`IEmailSender`, Mailpit in compose);
   - cookie sessions with refresh rotation;
   - CSRF protection;
   - roles and the permission catalogue (no transfer permission);
   - auth pages built from the Phase 03 form kit (`PhoneField`, `OtpField`, `useZodForm`, `applyProblemToForm`);
   - `ExpiredSession` and `PermissionDenied` wired to 401/403.

## Files intentionally left modified
- None.
