# After Phase 17 — D-151: the barber's day screen, red at the booked end, «حسابي» for barbers, «بريكاتي», cancellation reasons, reports as cards on a phone, services as tiles, the admin's loyalty tab

**Status:** [x] (Session 29, 2026-11-19; verified and committed by trimme-8b after its session ended) · requested by the user after D-150 · decision: D-151

## The request (user, Session 29: nine pages of the client's comparison, the Groom Here app on the right and the site on the left)
The document's own rule: «خذ الفكرة والترتيب، مب التصميم حرفياً. ألوان الموقع وشكله ممتازة، خلها مثل ما هي». The app's arrangement and behaviour are used; the site keeps its tokens, components and look. The numbers are the client's item numbers in their update file.

- **1-1 The barber's day screen (ضروري).**
  - At the top, a red «حجوزات تحتاج إقفال» block for all the shop's barbers, each row naming the barber responsible.
  - Below it, «جدولي», «الكل» or any colleague's name, to see their schedule.
  - Quick buttons at the top: «زبون حضوري», «حجز بالتلفون», «بريك», «إيقاف الحجز».
  - The site today: a booking that ended at 6:00 still looks normal at 6:03, and a barber sees only their own bookings.
- **1-2 The red booking from inside (ضروري).**
  - A red bar at the top says what happened: «فات الموعد وما انسجل «وصل» ولا «ما حضر»». The body reads «خلص وقتها وما انقفلت. يقفلها حلاق المحل».
  - Below it the usual buttons («وصل», «ما حضر»); any barber of the shop can press them.
  - Beyond the app: once closed, «المسؤول: محمد — قفله: سعد».
- **2-2 «حسابي» for the barber (مهم).**
  - As in the app: the name (editable), the mobile, the calendar (ميلادي or هجري), the shop screens' language (Arabic or Turkish) and signing out.
  - Beyond the app: notifications, and deleting the account.
  - Today the site's menu has only the shop's name and «تسجيل الخروج».
- **2-3 Breaks (تحسين).** The site's minute grid stays. Change two things: show working hours only (the grid starts at midnight today), and title it «بريكاتي».
- **2-7 The cancellation reason (تحسين).** Ready reasons to tap: «الموظف تعبان», «ظرف طارئ», «المحل مسكّر (كهرباء أو غيره)», «سبب ثاني» (then typed). Reports can then group the reasons. Today it is free text.
- **3-1 Reports on a phone (مهم).**
  - Stacked cards, no wide table: expected income, each day, each barber, then «المحل كامل».
  - The site's extra columns stay (actual length, under 5 minutes, closed late). Show them as cards on a phone. Today it is a 14-column table that needs side-scrolling.
- **3-2 The owner's services by section (مهم).** The services are grouped by section (hair, beard…). Each service is a tile with its price and length, the length changed with + and −.
- **9-2 Adding a service, for the barber (مهم).** Each service shows its length and price. One that cannot finish before the barber's next booking is greyed and cannot be chosen.
- **5-2 Loyalty at the admin (مهم).**
  - «ميزة مدفوعة»: the shop cannot switch it on itself.
  - The settings: points for the first visit and the points a reward needs; the reward, a free service or a fixed amount off.
  - Expiry: points expire after a period without a visit, and then the card is wiped or a point is taken off.
  - The site in the client's picture is D-132's programme. D-150 (deployed today) already replaced it.
- **7 A shop whose booking is paused.** The client wrote «لا تغيّره لين يقرر فيصل» — **deferred, nothing built.**

## The user's answers (AskUserQuestion)
1. **Red:** at the booked end. «انقفل متأخر», which is left out of the speed figures, still starts 15 minutes after the end.
2. **Colleagues' schedules:** read-only for a barber, except the existing pending and red cases. The mobile of a colleague's walk-in or phone customer stays hidden.
3. **Deleting a barber's account:**
   - removes the login and its personal data;
   - the barber stays in the shop with their bookings and history;
   - the owner is told and can give them a new login.
4. **«تعديل الاسم»:** the barber's name everywhere: the public page, the owner's list and new log lines.

## Defaults (decided, not asked)
- **The day screen is `/shop/appointments`.** The site page in the client's picture replaces the week list with the app's day.
  - Day navigation: «اليوم · الخميس ١٩ نوفمبر» with previous and next.
  - Chips: «جدولي», «الكل» and each barber for a barber; «الكل» and each barber for the owner.
  - The red block «حجوزات تحتاج إقفال» and the pending block stay on top. The closed-late block stays at the bottom with «المسؤول: … — قفله: …».
  - The quick buttons are for barbers only, since the owner books no walk-ins (D-146) and does not pause (D-147). «حجز بالتلفون» opens the walk-in flow with the phone kind chosen; «بريك» and «إيقاف الحجز» open «الدوام والبريكات» at that card.
  - The owner's overview is unchanged.
- **«وصل» on a red booking** comes back for any barber of the shop (the client: «الأزرار العادية (وصل، ما حضر)»). This overrides D-147's change that left a red row with only «انتهت الخدمة» and «ما حضر». «انتهت الخدمة» stays as well.
- **Colleagues' bookings:** a barber's list reads their own bookings by default; `professionalId` of a colleague or `all=true` widens it. The details open read-only. A fourth reviewed exception in `BarberScope`.
- **Names:** the barber's rename goes through the same path as the owner's edit, and the account's display name follows it.
- **Calendar:** Hijri is a display preference only. It is a cookie, read for the shop's pages. It changes how dates are written on the barber's screens: the day screen, details, breaks, walk-in, notifications and «حسابي». Date inputs, the month grid, the API and the owner's reports stay Gregorian.
- **Language:** the account page offers the site's languages (العربية, Türkçe, English) for the shop's pages. The URL keeps the locale, as everywhere else on the site.
- **Notifications:** a link to `/shop/notifications`.
- **Delete:** barbers only. The owner's and the admin's accounts are managed by the platform. The owner gets an in-app notice.
- **Cancellation reason:** a code is stored beside the text; the text is required only for «سبب ثاني». A pending booking's rejection keeps free text. The owner's reports count the shop's cancellations by reason.
- **Reports:** per-day figures come from the server and cover every booking, not the capped table. Below `md` the page shows cards; from `md` it shows the tables. Both are rendered on the server with CSS breakpoints.
- **Services tiles:** each tile saves on its own button with the service's version. Its checkbox is the service's booking status (D-149's «متاحة للحجز»). «تعديل» opens the full form.
- **Add-service list:** the server says which services fit, using the add's own rule: the barber's own length (D-149), and the next open booking's start after this booking's blocked time.
- **Loyalty admin tab:** keeps the rules form, which the owner and the admin both use. It adds:
  - the «ميزة مدفوعة» line, with the add-on's state and a link to the subscription tab's period panel (D-150), which is not bypassed;
  - + and − steppers for the points;
  - expiry presets (لا تنتهي، شهر، شهرين، ٣ شهور) mapped onto the days;
  - the cards and rewards counts.

## Slices (each verified before the next; one commit at the end if all gates pass; no push or deploy until asked)
1. **Backend** (a forked agent, reviewed here): red at the end; colleagues' read scope; «وصل» on a colleague's red booking; cancellation reason codes and their report counts; per-day report figures; add-service candidates; the barber's account (read, rename, delete). One additive migration.
2. **Web, independent of the new API:** «بريكاتي» with working hours only; the services tiles; the admin loyalty tab; the calendar preference plumbing.
3. **Web on the new API:** the day screen; the red drawer; the reasons dialog; the reports cards; the add-service list; «حسابي».
4. **Gates:** .NET build, unit, architecture and integration; OpenAPI; web lint, typecheck, format, tests, build and bundle budget; E2E and the route audit at 390 px on the rebuilt local stack.

## Checklist
Built and verified (see Evidence):
- [x] 1-1 day screen (chips, day navigation, quick buttons, red block on top; also the barber's home `/shop`)
- [x] 1-2 red booking (red at the end, the bar's text, «وصل» for any barber, «المسؤول — قفله»)
- [x] 2-2 «حسابي» (name, mobile, calendar, language, notifications, sign out, delete)
- [x] 2-3 «بريكاتي» (working hours only)
- [x] 2-7 cancellation reasons (codes, chips, report counts)
- [x] 3-1 reports as cards on a phone (per-day figures)
- [x] 3-2 services as tiles by section
- [x] 9-2 add-service list with lengths, prices and greyed services that don't fit
- [x] 5-2 admin loyalty tab gaps (with the client's 3-7 via D-152: Arabic-Indic digits, the range in the error)
- [-] 7 paused shop in discovery — waits on Faisal (decided since: D-155, a paused shop stays listed as «متوقف مؤقتاً»)
- [x] Docs: D-151, TRACEABILITY (R-SD-23…29, R-SVC-09, R-LOY-10, R-NEG-04 narrowed), DV-A39, security.md and MASTER_PLAN §2 (the phone exception: own bookings only); STATUS, MASTER_PLAN table and handoff at the end

## Previous phase's decisive check (re-run at the start of this session)
- `dotnet test --project tests/Trimme.IntegrationTests --filter-class "*LoyaltyTests" --filter-class "*OwnerSettingsTests" --filter-class "*BarberOperationsTests"`: **25/25 passed** (58 s) on `0f5d64b`, with a clean tree.

## Evidence (so far)
- **.NET** (the forked agent, on its final tree):
  - Release build of `Trimme.slnx`: 0 warnings, 0 errors;
  - `has-pending-model-changes`: none;
  - unit 501/501, architecture 69/69;
  - full integration suite 273/273, on the second run: the first was 272/273, with `ShopBookingChangeTests` still expecting a colleague's booking to answer 404 on read; updated to read-only, with changes still 404;
  - OpenAPI regenerated, `OpenApiContractTests` 4/4.
  - **Not yet re-run here.**
- **Web** (here):
  - format, lint, typecheck (with `next typegen`) and `openapi:check` pass;
  - `pnpm test`: 62 files, 576/576;
  - `pnpm build` passes; `bundle-budget.mjs` puts 90 routes within budget.
    - The owner's barber page measured 330.2 KB against 330 (329.4 before), because every shop page carries the shared Hijri date code. The stepper's − was moved out of the shared icon map, which recovered 0.1 KB. That page alone now has 335 KB.
- **Local stack:** api, web, migrate and seed rebuilt from this tree. `migrate`: 29 migrations; `bookings.cancellation_reason_code` is present. The seed ran.
- **E2E, first full run** (3 workers): 91 passed, 1 skipped, 22 failed.
  - Every route audit (light and dark, every role, 20 tests) failed with `worker process exited unexpectedly (code=3221225794)`. That is Windows failing to start a process: the machine was out of memory, and Claude Code then stopped the run for low memory. Not a page failure.
  - `services.spec` (from its `error-context.md`): the tile's save worked (it showed «55 min»), but `getByRole('status')` matched two elements, because the stepper's `<output>` is a status region too. The spec now waits for the message's text. It also puts the new service in the shop's first section, so its tile has neighbours for «Move up».
  - `shop-schedule.spec` (from its `error-context.md`): it expected the old 24-hour grid and got 10, Sultan's noon-to-10-PM working hours. It now checks those hours, the hours outside them, and the «بريكاتي» title.
  - **Passed in this run:** `shop-dashboard.spec`, and every flow except those two.
    - Omar's home with the quick buttons and «جدولي»; the owner moving two days ahead to watch the phone booking live; cancelling with «سبب ثاني»;
    - no overflow on `/ar/shop`, `/ar/shop/appointments` and `/ar/shop/reports` at 390, 768 and 1440 px.
  - **Never run in a browser yet:** the route audit (axe, language, indexing, overflow) on `/shop/account`, the barber's day screen, `?kind=phone`, the service tiles and the loyalty tabs.
  - **To re-run:** the `a11y` and `a11y-dark` projects, `services.spec` and `shop-schedule.spec`, at `--workers=1`; then the .NET integration suite here, not at the same time.

## Evidence (closing run, Session 29, by trimme-8b on the same tree, after the D-151 session ended)
- **.NET**
  - Release build of `Trimme.slnx`: 0 warnings, 0 errors.
  - Unit tests: 501/501. Architecture tests: 69/69.
  - Full integration suite: **273/273** (4 min 51 s), run alone.
  - `pnpm openapi:check`: up to date.
- **Web**
  - `pnpm format:check`, `pnpm lint` and `pnpm typecheck` all pass.
  - `pnpm test`: 62 files, **576/576**.
- **Local stack**
  - api, web and migrate rebuilt from this tree, after another session (D-152) had run its own images on it.
  - `migrate`: up to date at 30. The 30 are D-151's `BarberDayAndAccount` plus D-152's `ShopShortName`, which that session applied from its worktree; this tree does not know it, and it adds two nullable columns.
- **E2E, at `--workers=1`**
  - `services.spec` and `shop-schedule.spec`: 5/5.
  - **Route audit, light and dark, first run: 17 passed, 8 failed.** Every failure was horizontal overflow at 390 px. This design's `sm` breakpoint *is* 390 px (`--breakpoint-sm: 24.375rem`), so an `sm:grid-cols-*` already applies on a phone. A temporary probe (deleted) found the overflowing elements:
    - `/shop/services`: the tiles were two to a row (`sm:grid-cols-2`), so the length stepper's «55 min» and its + spilled out (24 px in Arabic, 48 px in English). Now one per row below `md`.
    - `/shop` and `/shop/appointments` for a barber, in English: the four quick buttons were four to a row (`sm:grid-cols-4`), and «Phone booking» and «Pause booking» did not fit (6 px). Now two per row below `md`.
    - `/shop/settings/loyalty` and the admin's loyalty tab, in English: the five expiry presets were five to a row (`sm:grid-cols-5`, 1 px). Now two per row below `md`. The loyalty counts list (`LoyaltyHistory`, D-150) was five to a row too, with its labels running over each other; now two per row below `md`.
  - **Route audit after the fixes (web rebuilt): 25/25** (light and dark, every role, both locales: axe 0 serious, language, indexing, overflow).
  - **`shop-dashboard.spec`** then failed once. The walk-in form offered only Majid, because «تحديد لحية» was ticked as well as «قص وتصفيف». `services.spec` reorders the first section and leaves it reordered, so «تحديد لحية» had become the first service, which is ticked by default, and Omar does not do it. The spec now unticks every service but «قص وتصفيف», which is what it meant. After that: 2/2.
