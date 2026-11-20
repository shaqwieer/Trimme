# After Phase 17 — D-156: the client's iPhone screenshots (the booking days, the corner «أعد الحجز» notice, the sun and moon)

**Status:** [x] except the real-iPhone check after a deploy (the user or the client) · Session 32, 2026-11-20 · requested by the user · decision: D-156
**Where:** the main checkout, at the same time as trimme-44 (D-157, the booking wizard) and trimme-08 (D-158, in its own worktree). Only D-156's paths are staged; `PublicShell.tsx` is staged by hunk, because trimme-44 adds a `fill` prop to it.

## The request (user, Session 32: four WhatsApp screenshots from the client's iPhone, in Arabic)
1. «ليش يقدر العميل يحجز بالأيام الماضية» — the booking's day strip read «سبت ١٨ (مغلق)، أحد ١٩ نوفمبر، اثنين ٢٠ نوفمبر» on 20 November. Past days must not show, and the screen suggests booking in the past.
2. «نط شهر من ٢٠ نوفمبر إلى ٢١ ديسمبر» — the next cards read «ثلاثاء ٢١ ديسمبر، أربعاء ٢٢ ديسمبر، خميس ٢٣ ديسمبر».
3. «احجز مثل آخر مرة» (D-155) is fine, but it should be a small notice at the top corner (the logo's corner, drawn on the screenshot) with an ✕. It should take no room and make nothing harder. It is not in the bell's notifications. It must be clear and small («ضروري جداً»). The client's example: «أعجبك حجزك مع صالون الفلاني؟ أعد الحجز من جديد هنا», or any short, simple sentence.
4. The header's monitor icon turned out to be the day/night theme; the client had only just noticed it. Make it a moon and a sun.

## Findings
- **1 and 2 are one bug: Hijri day numbers.**
  - `formatDayNumber` used `QUANTITY_LOCALE` (`ar-SA-u-nu-arab`), which names no calendar. Safari takes plain `ar-SA` as the Umm al-Qura calendar.
  - The weekday and month come from `TIME_LOCALE`, which forces `ca-gregory`. So the strip showed the Hijri day under a Gregorian weekday and month.
  - The screenshot's cards are Saturday 28 November to Thursday 3 December, which are 18–23 Jumada II 1448. The strip had been scrolled one week along.
  - No past day was shown or bookable. `DateStrip` already starts at today (D-125), and the server refuses past times.
  - The same function draws the month grid's numbers (`CalendarMonth`), the date block of the customer's booking cards («مواعيدي»), and the admin overview's days. All three were wrong on an iPhone too.
- **Node and Playwright's WebKit build for Windows** (Safari 26.6 engine) default `ar-SA` to Gregorian, so neither reproduces it. The diagnosis rests on the exact match between the screenshot and the Umm al-Qura days. The fix names the calendar explicitly, which wins in every engine.

## Changes
- **Day numbers:** `formatDayNumber` uses `TIME_LOCALE` (`ca-gregory`, the same D-148 digits). `src/test/safariIntl.ts` makes `Intl.DateTimeFormat` behave like Safari in tests.
- **Corner notice** (`RebookNotice`, replaces `LastVisitCard`):
  - Fixed under the sticky header in the logo's corner (`start`, so right in Arabic and left in English), lined up with the header's edge, at most 17.5rem wide.
  - Reads «أعجبك حجزك في {الصالون}؟» and «أعد الحجز من هنا». Pressing it opens the booking at the time step with the same shop, barber and services (D-155's `rebookHref`).
  - The ✕ (44 px, «إغلاق») closes it and writes `trimme_rebook_dismissed=<booking id>` (30 days, first-party, not HttpOnly, a booking id only). The server reads that cookie, so the notice stays closed until a newer completed visit, or for 30 days, with no flash.
  - It drops in after 0.4 s (`animate-notice-in`, `motion-safe` only). It never hides by itself, takes no focus and is not a live region.
  - Not shown while the customer has an upcoming booking (`GET /me/bookings/active` 200): D-155 allows one, so «أعد الحجز» would only be refused.
- **Day/night switch** (`ThemeToggle`, replaces the D-124 `ThemeMenu` in the public, customer and dashboard headers and the dashboard drawer):
  - One button with a sun by day and a moon by night. Both icons are drawn and the `dark:` variant shows one, so «تلقائي» on a dark device paints the moon with no script.
  - One press flips what is shown (under «تلقائي» it reads `prefers-color-scheme`) and saves an explicit Light or Dark.
  - A fixed name «تبديل الوضع النهاري والليلي», also its `title`, and no pressed state: the server cannot know the device's theme under «تلقائي».
  - «تلقائي» stays in the account settings (`ThemeSettings`, unchanged).
- **Messages (ar, en, tr):**
  - `common.theme.menuLabel` became `toggle`.
  - `discover.lastVisit` is now `label`, `title`, `book` and `close`; `where` is gone.

## Verification (actual)
- **Web unit:** 64 files, 603 tests ✔ (with trimme-44's in-progress tree).
  - New: `format.test.ts` (Safari-like `ar-SA`: the bare locale reads «١٨» for 28 November; `formatDayNumber` gives «٢٨», «١», 28, 28) and `interactive.test.tsx` (the strip on 28 November–3 December reads «اليوم ٢٨ نوفمبر … الخميس ٣ ديسمبر»).
  - Both tests **fail on the old code** with «١٨», the screenshot's number, and pass with the fix.
  - `rebookNotice.test.tsx` (×3: the text and link, fixed and not focused or live, axe; the ✕ and its cookie; the cookie's format and parsing).
  - `theme.test.tsx` (the switch's fixed name in ar and en, both icons, flip and save, «تلقائي» on a dark and a light device, Enter and Space, axe; the account control, the other tab and the back/forward cache kept).
  - `DashboardShell.test.tsx` (the switch in the header from 768 px and in the drawer below, a press leaves the drawer open).
- **Static checks:** typecheck ✔; ESLint 0 warnings ✔; Prettier on D-156's files ✔. `shops/[slug]/page.tsx` is trimme-44's and left to them.
- **Build:** `next build` ✔; bundle budget ✔ (90 routes, largest `/[locale]/admin/shops/[shopId]` at 334.9 KB gzip).
- **E2E:** `flows/theme.spec.ts` 8/8 ✔ on the local stack, with the web container rebuilt from this tree (port 3300, Chromium).
  - Covered: System follows the OS live with the sun and moon; a press flips, survives navigation and reload, and beats the OS; «تلقائي» on a dark device goes to Light; the first paint with no JavaScript (the saved theme, and the moon on a dark OS); tab sync; print stays light; the keyboard; the account setting.
- **Manual (Playwright, Chromium with the iPhone 13 viewport, signed in through the dev OTP inbox):**
  - Noura, Khalid and Sara each had an upcoming booking, and the notice was not shown for any of them (the D-155 rule).
  - With Khalid's and Noura's upcoming bookings cancelled in the local data, the notice shows on the first paint (opacity 1 at 300 ms), top right in Arabic (280 px wide; 62 px tall with «باربر هاوس», three lines with «صالون الأصالة للحلاقة») and top left in English. Its link is `/ar/shops/barber-house/book?service=…&pro=…`. The ✕ hides it, a reload keeps it hidden, and the cookie holds the visit's id.
  - The entry animation reaches opacity 1 by 800 ms in Chromium and WebKit.
  - WebKit could not keep the `Secure` session cookies on plain `http://localhost`, so the signed-in checks ran in Chromium.
- **Not run:** the .NET suites (no API change), and the full E2E suite (only the theme flow changed; no E2E referenced the old card).
- **Pending (the user or the client):** a real iPhone after a deploy. The day strip should show 21, 22, … November after «اليوم», then 1 December.

## Deploy note (when asked)
Web image only. No migration and no API change: `build web` + `up -d web`. Nothing to back up for this change alone.
