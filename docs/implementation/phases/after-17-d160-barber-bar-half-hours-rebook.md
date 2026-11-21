# After Phase 17 — D-160: a barber's three tabs, the calendar's half-hour rows, and a smaller «أعد الحجز» over the logo

**Status:** [x] except the client's phone check (Session 33, 2026-11-21) · requested by the user (three iPhone screenshots of groomhere.sa, 2026-11-20) · decision: D-160

## The request
1. **A barber's bottom bar** (screenshot 1, «الدوام والبريكات» circled): «hide breaks and work from bottom bar … need three only for barber».
2. **The calendar** (screenshot 2, the hour axis circled): «need toggle to can appear in half hours also instead of hours».
3. **The customer's home** (screenshot 3, the header's logo circled), the client's words: «ارفعها هنا و صغر النص — اعجبتك حلاقتك الاخيره؟ اعد الحجز هنا — عشان المربع يصير اصغر», then «الاشعار خله يجي بالزاويه و اصغر حجم … خله يغطي الصغير فوق احسن».

## Defaults (decided, not asked)
- **1.** The barber's bar is «النظرة التشغيلية», «حجز حضوري», «التقويم». «الدوام والبريكات» stays in the side menu; «بريك» and «غير متوفر اليوم» are also on «حجز حضوري» (D-158). The owner's bar is unchanged.
- **2.** A switch «كل نصف ساعة», on the day and the week views. On, every half hour is a 64 px row with its label and line, so the day is twice as tall; off, hour rows as before. Remembered on the device in a cookie. Half-hour labels squeezed into the 64 px hour were rejected: denser and no easier to press (one constant if the client prefers it).
- **3.** The client's text without the shop's name (kept in the accessible name), 12 px, 48 px tall. Below 1024 px it sits over the logo inside the header and stops short of the theme switch and the bell. From 1024 px the customer's menu sits next to the logo, so it stays under the header there.

## Checklist
- [x] `navigation.ts`: `barberTabKeys` = overview, walk-in, calendar; `DashboardShell` docstring
- [x] `lib/shop/calendarRows.ts`: the cookie (`trimme_calendar_rows`), its parser and writer
- [x] `ShopCalendar`: the switch; `Hours`, the day's lines and both views' heights by row (30 or 60 minutes, 64 px each); the cookie read on the client too
- [x] Calendar page: reads the cookie and passes `initialRowMinutes`
- [x] `RebookNotice`: the new text and size, over the logo below `lg`, under the header from `lg`; `discover.lastVisit` in ar, en, tr; `shopBoard.calendar.halfHours` in ar, en, tr
- [x] Unit tests: `DashboardShell.test.tsx` (three tabs, breaks in the menu), `shopCalendar.test.tsx` (new, three tests), `rebookNotice.test.tsx` (text, place, size)
- [x] Found on the way: the sticky time column covered the shell's header when the day scrolled under it; now `z-15` (over the bookings, under the header)
- [x] Gates: format, lint, typecheck, unit, build and bundle budget
- [x] Manual: Chromium at 390×844 on the rebuilt local web container (a barber's bar and calendar; a customer's notice)
- [ ] The client's phone after a deploy

## Evidence (Session 33, main checkout on `06a0eba`)
- **Web unit:** `pnpm test` on the final tree: 68 files, **618 passed**. D-160's tests:
  - `shopCalendar.test.tsx` (new): 2–6 pm gives four hour labels and 256 px; the switch gives eight half-hour labels (٢:٠٠ … ٥:٣٠ م) and 512 px, the 3:30 booking at 37.5 %, the cookie at 30, and back to four and 60; the sticky axis is `z-15`. The week opens on the remembered half hours. The cookie's values and parser.
  - `DashboardShell.test.tsx`: a barber's bar is «النظرة التشغيلية، حجز حضوري، التقويم»; «الدوام والبريكات» is once on the page, in the menu, to `/ar/shop/schedule`.
  - `rebookNotice.test.tsx`: «أعجبتك حلاقتك الأخيرة؟أعد الحجز هنا», no shop printed, named «أعد الحجز في …»; `fixed top-2 lg:top-18`, `h-12`, the width cap; the ✕ `w-11`; axe clean.
- **Static:** `pnpm typecheck` (next typegen + tsc) exit 0; `pnpm lint` (eslint, 0 warnings) exit 0; `pnpm format:check` clean.
- **Build and bundle budget:** `pnpm build` exit 0; `node scripts/bundle-budget.mjs` exit 0 (90 routes, largest `/[locale]/admin/shops/[shopId]` at 335.9 KB gzip). The web container was rebuilt from the final tree (`docker compose -p trimme build web`, `up -d --no-deps web`).
- **Manual, Chromium 390×844, `http://localhost:3300`** (script in the session's scratchpad):
  - **Majed (barber, by mobile):** the bar reads `["النظرة التشغيلية","حجز حضوري","التقويم"]`. The calendar has 12 hour labels (١٠:٠٠ ص … ٩:٠٠ م) at 768 px. With the switch on, 24 labels (١٠:٠٠ ص، ١٠:٣٠ ص … ٩:٣٠ م) at 1,536 px. It stays on after a reload, on the week (30 labels) and after a back navigation from «حجز حضوري». After scrolling, the point under the title is the header (before the fix the axis covered it). The switch was turned back off.
  - **Khalid (customer, completed visit, no upcoming booking):** the notice reads «أعجبتك حلاقتك الأخيرة؟ / أعد الحجز هنا», named «أعد الحجز في باربر هاوس». Its place in the 65 px header (left–right, top–bottom):
    - ar 390: 162–374 × 8–56 over the logo (275–374);
    - ar 375: 147–359;
    - ar 320: 120–304; the first line is cut with «…» at this width;
    - en 390: 16–240, over the logo on the left.
    - It never overlaps the theme switch or the bell.
    - At 1280 it is 72–120, under the header and clear of the logo.
    - Light and dark screenshots checked.
- **Local stack note:** `trimme-postgres-1` had exited (255) before this session; started again with `up -d --no-deps postgres` on the same volume.
- **Pending:** the client's phone after a deploy.
