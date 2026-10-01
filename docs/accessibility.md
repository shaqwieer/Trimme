# Accessibility

Target: WCAG 2.2 AA in Arabic (RTL) and English (LTR), on phones and desktops (R-WEB-09). Decisions: D-021 (contrast tokens), D-048 (Radix direction), D-122 (language and the route audit).

## 1. Automated checks

| Check | What it covers | Where |
|---|---|---|
| Route audit | Every page route (81) as the role that opens it, in both locales. axe with WCAG 2.0/2.1/2.2 A and AA rules, contrast and `target-size` included: no serious or critical finding. Also `lang`/`dir`, untranslated strings and overflow at 390 px | `tests/E2E/audit/routes.spec.ts` (Playwright project `a11y`) |
| Keyboard pass | The skip link reaches `#main`; every focus stop on the public pages shows an indicator; Tab cycles without a trap; staff sign-in by keyboard alone; a dialog takes focus, keeps it, closes on Escape and returns focus; reduced motion; touch targets of 44 px on a touch screen | `tests/E2E/audit/keyboard.spec.ts` |
| Component tests | axe in jsdom on the component gallery and forms; dialog focus trap and return; labelled controls; RTL/LTR rendering | `apps/web/src/**/*.test.tsx` |
| Flow tests | axe on the screens each E2E flow visits | `tests/E2E/flows/*.spec.ts` |

## 2. Conventions

- **Focus.**
  - Every interactive element shows the shared focus ring.
  - A dialog, sheet or confirmation returns focus to whatever opened it, even a plain button that is not the Radix trigger (`useReturnFocus`, Phase 17).
  - Forms that open with a field focused (sign-in) are the only pages where the skip link is not the first stop.
- **Touch targets.** Controls are at least 44 px. Compact sizes (`xs` buttons, `sm` icon buttons, breadcrumb links) grow to 44 px under `pointer: coarse`, so desktop layouts keep the design's density. Links inside running text are exempt (WCAG 2.5.8).
- **Language of parts (WCAG 3.1.2).** Text in the other language carries `lang`: language names, Arabic-only descriptions shown in English, WhatsApp template and message bodies, and audit summaries (D-122).
- **Direction.** `chevR` means forward in the reading direction and `chevL` back, and both mirror in RTL. Numbers, times, prices and phone numbers sit in isolated LTR runs (`ltr-isolate`, `<bdi>`).
- **Motion.** `prefers-reduced-motion: reduce` turns transitions and animations off.
- **Contrast.** The tokens meet 4.5:1 for text and 3:1 for UI parts (D-021). axe checks contrast on every route in both locales.
- **Forms.** Every field has a visible label. Errors are announced next to the field, and a form-level error goes in a live region. Messages come from the catalogues, never from Zod's or the API's English text.

## 3. Screen-reader pass (manual, NVDA)

A person must run this pass. Use NVDA (latest) with Firefox or Chrome on Windows, first in Arabic (`/ar`, NVDA's Arabic voice), then in English (`/en`). For each step, record pass or fail and notes in the table at the end.

**Customer**
1. Landing `/ar`: press `H` to move by heading. The page has one `h1`, and the section headings are in order. Press `D` for landmarks: banner, navigation, main and contentinfo are all announced.
2. Skip link: the first Tab announces «تخطَّ إلى المحتوى». Enter moves to the main content.
3. Language switch: it is announced as a link with its language («English» is read with an English voice).
4. Shop page `/ar/shops/barber-house`: tabs are announced as tabs, with the selected one. Service rows read name, duration and price. The heart is announced as a toggle with its state, or as a sign-in link when signed out.
5. Booking wizard: each step's heading is announced on arrival. Service and barber choices are radio groups with their labels. On the date strip and time grid, the chosen value is announced; unavailable times are announced as unavailable. The review step reads the summary, including «الدفع في المحل».
6. Sign-in: the phone field reads its label and the +966 prefix. An error is announced at once. The OTP field reads its label and the resend countdown.
7. My bookings: each card reads the shop, barber, date and time and the status. The cancel dialog announces its title and returns focus to the cancel button when closed.

**Shop owner**
8. Dashboard: the sidebar is a navigation landmark with the current page marked. The live-update region announces a new booking without moving focus.
9. Appointments: the table has headers, and the status change menu announces its options and the result.
10. Walk-in: the form fields and the time grid are announced as in step 5.

**Admin**
11. Tables (shops, bookings, audit): header cells are announced with each cell, and the pagination announces the current page.
12. Dialogs (new QR code, switch off): title announced on open, focus kept inside, Escape closes, focus returns.
13. Charts (overview): the screen-reader table behind each chart is read.

| Step | `/ar` | `/en` | Notes |
|---|---|---|---|
| 1–13 | | | |

## 4. Known limits

- Short Arabic-only names (shops, services, people) are not marked with `lang` in the English UI (D-122).
- The map (MapLibre) is a visual aid. Every map task has a keyboard and screen-reader alternative: typed coordinates and address search in the picker, and the list view in search.
