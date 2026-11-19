# After Phase 17 — D-152: the client's iPhone test (Safari's bar, sideways pages, the reward's price, «مواعيدي», the customer's cards, WhatsApp amounts, the barber's welcome)

**Status:** [x] except 1-1 and 2-1, which wait on a real iPhone (Session 29, 2026-11-19) · requested by the user after D-150 · decision: D-152
**Where:** worktree `.claude/worktrees/d152-client-iphone-review`, branch `worktree-d152-client-iphone-review`. Another session (trimme-30) is building D-151 in the main checkout, and trimme-e5 committed D-153 (`bc045f5`), which this branch is rebased on. Merge after D-151 lands.

## The request (user, Session 29: eight pages of the client's file «بعد ملف التحديث جربنا الموقع كزبون حقيقي من آيفون (Safari)»)
The client's priorities: ضروري (before launch), مهم (must change), تحسين (improves the experience). The numbers are theirs.

- **1-1 ضروري: Safari's bottom bar covers the site's bottom bar** on every page (customer, barber, owner, admin). Safe area at the bottom (`env(safe-area-inset-bottom)` with `viewport-fit=cover`), heights that follow Safari's bar (`dvh`), tested on a real iPhone with the bar showing.
- **1-2 ضروري: a 5-minute lead time** (Faisal's decision), per shop; «أقرب موعد» offers the truly nearest time; when a time became too close during confirmation, a proper message («الوقت صار قريب مرة، هذي أقرب الأوقات الحين») and the times refresh on the same page.
- **1-3 ضروري: the coupon doesn't lower the price.** The admin picks the free service from the shop's list; at booking the service's price becomes zero and the total follows; offered only when the booking has that service; the barber sees «مكافأة ولاء — حلاقة شعر مجانية» and zero; reports and expected income zero.
- **2-1 مهم: pages shift sideways on iPhone** («مواعيدي»: card edges, «احجز موعدك القادم», the pager; the booking's service step). No sideways swipe anywhere.
- **2-2 مهم: «مواعيدي»: cancel is hidden** (Faisal): «إلغاء الموعد» on the card beside «إعادة جدولة», in its shape and colour; «إعادة جدولة» becomes «تغيير الموعد» on the card and the details.
- **2-3 مهم: different status for the customer and the shop:** the customer read «بانتظار تأكيد المحل», the barber «مؤكد». The same status on both («مؤكد»), the reward on its own line.
- **2-4 مهم: the coupon card after booking:** the title breaks one word per line and the badges cover the code. The badge and «عرض الموعد» go under the text.
- **2-5 مهم: the loyalty card disappears after the coupon is used** («لا توجد بطاقات ولاء بعد»). The card stays (0 of 2) with a history line «استخدمت: حلاقة شعر مجانية — ١٩ نوفمبر».
- **2-6 مهم: the coupon code** (Faisal): remove it from the customer's screen; the reward is tied to the mobile and the barber applies it from the booking. Expiry stays as the admin sets it.
- **2-7 مهم: the barber's page:** colleagues' bookings today; the late block on top of the barber's home; the squeezed late card.
- **2-8 مهم: remove «رقم العميل غير متاح»** (Faisal) from every booking for the barber and the owner. Only «جوال العميل» on a booking the shop recorded with a number, until «انتهت الخدمة».
- **3-1 تحسين: the customer's WhatsApp confirmation** writes «60 ر.س» in Latin digits: «٦٠ ريال», the word, not the new sign.
- **3-2 تحسين: the barber's WhatsApp:** a «عرض الحجز» button and the booking's source.
- **3-3 تحسين: the barber's reschedule WhatsApp:** the old and new time and the reference.
- **3-4 تحسين: after «انتهت الخدمة»:** no WhatsApp; a free in-app notice instead, with thanks, «قيّم زيارتك» and how much is left for the reward.
- **3-5 تحسين: «احجز واستخدم الكوبون»** opens the booking with the reward's service chosen and the reward on.
- **3-6 تحسين: ratings on the shop page:** show what the customer picked in «ما الذي أعجبك؟» under each rating, and a summary on top («٨ أشادوا بجودة الحلاقة»).
- **3-7 تحسين: the admin's loyalty error** «هذه القيمة خارج النطاق المسموح» should say why («من ١ إلى …»).
- **3-8 تحسين: the barber's welcome** (Faisal's request): «أهلاً محمد، نورتنا» instead of the shop's name, with a shop icon and the shop's name without «صالون»; a «اسم مختصر» field in the shop's settings, the full name when it is empty.
- **3-9 تحسين: «مواعيدي»: the pager** («عرض ١-١ من ١») shows with one appointment; only with more than one page.
- **4 «ممتاز: خلها مثل ما هي»:** the rating screen, late bookings, the WhatsApp messages, the customer's cancel dialog, loyalty points after «انتهت», «سابقة», the coupon chip at the barber, «احجز موعدك القادم» and dark mode. Nothing to change.

## What the code says (three read-only searches, Session 29)
- **The loyalty screenshots show the build before D-150.** «كوبون الولاء», «لا أريد استخدامه الآن», «احجز واستخدم الكوبون» and «أكمل زيارتين…» were added by D-132 (`8c49603`) and removed by D-150 (`41f3a6f`, deployed this morning). D-150's points reward already zeroes the linked service and lowers `amountDue`, and the admin and the owner already pick the service from a list. Old D-132 coupons issued before today still exist; they have a typed title, no service, a code, and do not lower the price.
- **«بانتظار تأكيد المحل» is not the coupon.** It is D-148's label (4-14) for an open booking whose end has passed, until a barber closes it.
- **Sideways pages:** nothing clips the page (`overflow-x` appears nowhere), and `viewport-fit` is missing, so iOS reports every safe-area inset as 0 and the bars' existing padding did nothing.
- **«الوقت انحجز من عميل ثاني»:** there is no "too soon" error. A time that slipped inside the lead time is answered `booking.slot_unavailable` and the customer reads «سبقك عميل آخر». The wizard then sends them back to the date step. Its slots stay cached for 30 s with no refetch.
- **After «انتهت الخدمة»** no WhatsApp is sent at all. The only notice is D-150's `loyalty.visit_completed`, and only while the shop's add-on is on.
- **The public reviews API** has no tags and no tag counts.
- **WhatsApp amounts** are the full price (`Price`, not `AmountDue`), in Latin digits by design (`Placeholders.cs`).

## The user's answers (AskUserQuestion, Session 29)
1. **Parallel work:** a separate worktree; D-151 keeps the main checkout.
2. **2-3:** «مؤكد», as the client asks, on both sides until a barber closes the visit; then «مكتمل», «ما حضر» or «ملغي». This reverses D-148's 4-14 for that window only.
3. **Old D-132 coupons lower the price too:** at booking a free-service coupon makes the booking free and a percent coupon takes its percent off, the way the reports already counted them. The reports then read `amountDue`, so nothing is discounted twice.
4. **1-2's value:** not changed. The user already set the lead time with the client. No default, data migration or form change.
5. **3-2 and 3-3:** deferred (they need new Meta templates).

## Defaults (decided, not asked)
- **2-7 and 3-7 belong to D-151** (trimme-30): 2-7 is D-151's 1-1 (colleagues' schedules, the red block), and 3-7 is its 5-2 (the loyalty form with steppers). Both were forwarded to trimme-30 with the details: the late block on top of the barber's home, buttons under the card's text, and Arabic-Indic digits failing `/^\d+$/` in `LoyaltyRulesForm`.
- **1-1** cannot be verified here: Playwright draws no Safari toolbar. It waits on a real-iPhone check (bar showing; also the sticky headers clear of the status bar).
- **1-2:** a distinct `booking.slot_too_soon` (409) when the time fell inside the lead time; the wizard stays on the review step for both conflicts, shows the reason and the nearest times again, and one tap takes a new one.
- **2-2:** cancelling from the card stays on the list.
- **2-6:** the code is removed from the customer's screens. The barber never needed it: the reward travels with the booking.
- **3-4:** one notice per visit. A signed-in customer's completed online visit gets «شكراً لزيارتك» with «قيّم زيارتك». When the shop's loyalty add-on is on, the same notice carries the points, so nobody gets two.
- **3-5:** the card's link opens the wizard with the reward's service chosen and the reward ticked on the review step.
- **3-6:** each public review carries its tags, and the summary counts each tag over the shop's (or barber's) published reviews; the page shows the top ones.
- **3-8:** a `ShortName` on the shop (at most 40 characters, optional), which the owner edits on «إعدادات المحل» and the admin on the shop's profile. The welcome is on the shop's home for barbers and the owner: «أهلاً {first name}، نورتنا», with the shop icon and the short name (or the full name).
- **3-9:** the pager hides itself when everything fits on one page, everywhere (most lists already guarded it).

## Checklist
- [~] 1-1 `viewport-fit=cover`, safe-area padding on every bottom bar and sticky footer — built; **waits on a real iPhone**
- [x] 1-2 `booking.slot_too_soon`; the review step refreshes in place with the nearest times
- [x] 1-3 the review's lines and footer net of the reward; the barber's amount due; old coupons lower the price; reports on `amountDue`; WhatsApp's amount is the amount due
- [~] 2-1 nothing wider than the screen (measured in WebKit and Chromium at 320 and 390 px); the cause, iOS's zoom on focus, fixed with 16 px fields on iOS; the page clipped as a backstop — **waits on a real iPhone**
- [x] 2-2 «إلغاء الموعد» on the card (it opens the dialog on the booking's page); «تغيير الموعد»
- [x] 2-3 «مؤكد» until a barber closes the visit
- [x] 2-4 the reward row: badge and link under the text
- [x] 2-5 the card stays after a reward is used, with «استخدمت: …»
- [x] 2-6 no code on the customer's screens
- [-] 2-7 → covered by D-151 (confirmed)
- [x] 2-8 no «رقم العميل غير متاح»
- [x] 3-1 «٦٠ ريال» in WhatsApp
- [-] 3-2, 3-3 deferred (new Meta templates)
- [x] 3-4 the completed visit's notice
- [x] 3-5 the card's link chooses the service and the reward
- [x] 3-6 tags under each review and their counts on top
- [-] 3-7 → covered by D-151 (confirmed)
- [x] 3-8 the welcome and the short name
- [x] 3-9 the pager hides on one page
- [x] Docs: D-152, TRACEABILITY, deviations (DV-A40), STATUS, MASTER_PLAN, handoff

## Previous phase's decisive check
- D-150 was verified and deployed this morning (`0f5d64b`); D-153 (`bc045f5`) was committed by trimme-e5 with its own gates. This branch's .NET build at `0f5d64b` was clean (0 errors).

## Evidence (Session 29, in the worktree, on top of `5acb369`)
- **Who did what:** a forked agent built the backend (B1–B7). It was reviewed here (the too-soon check, the coupon discount and the reports, the single notice) and re-verified by the runs below.
- **.NET:**
  - Release build: 0 warnings, 0 errors.
  - Unit 507/507; architecture 69/69.
  - **Full integration 273/273** in Release, 5 m 53 s. That is 270 before, plus `OwnerSettingsTests.ATimeInsideTheNotice_IsTooSoon_NotTaken_OnBookingAndRescheduling`, `LoyaltyTests.AnOldCoupon_LowersTheAmountDue_TheReportCountsItOnce_AndItsCardStaysWithAUsedLine` and `ShopProfileTests.ShortName_IsAlwaysTheOwners_UpToFortyCharacters_AndTheFrameReadsIt`, with tags and notices added to existing tests.
  - OpenAPI regenerated after the rebase on D-154; `openapi:check` clean.
- **Web:**
  - lint, typecheck and format pass;
  - `pnpm test`: 61 files, 581 tests;
  - `pnpm build` passes, and so does the bundle budget: 89 routes, the largest the admin shop page at 332.9 / 335 KB.
  - **Bundle regression found and fixed.** The «مواعيدي» card importing the cancel dialog made Turbopack regroup a chunk shared by every page with toasts: +12.5 KB on 70 routes, and the shop page at 232.1 / 220.
    - Found with a baseline build of `main`'s web, then by reverting one change at a time (Toast: no effect; the card's import: the cause).
    - The card now links to `?cancel=1` and the details page opens the dialog. Sizes are back to `main`'s: the shop page 219.6, «مواعيدي» 265.2.
    - The barber edit page was at 330.0 KB on `main`, and the notice renderer adds 0.2 KB. Before the rebase, «dashboards and account» was raised to 332; after it, D-151 gives that page its own 335 budget and the largest other dashboard is 328.1, so the budget is back to 330.
- **E2E** (api and web built from this worktree, `migrate` applied `ShopShortName`; codes on):
  - `customer-booking`, `shop-dashboard`, `tenancy`, `public-discovery` and the route audit (every role, light and dark, axe): **37 passed, 1 failed**.
  - The failure is E7. Sara's seeded visit is past the 7-day review window on the shared, days-old volume; the test's own message says to reset the volume. It is unrelated to D-152, and the volume is shared with two other sessions, so it was not reset.
  - E6 now checks that the loser stays on the review with times offered.
- **Measurements:**
  - **Overflow:** WebKit (public pages) and Chromium (signed in), at 320 and 390 px. No element is wider than the screen, `scrollWidth` equals the viewport, and that led to the zoom-on-focus finding.
  - **Live API:** `GET /api/v1/public/shops/barber-house/reviews` gives `summary.tags [{Punctuality: 8}]`, and each review its tags.
  - **Screenshots** at iPhone 13 size: the shop home «أهلاً مالك، نورتنا» with the icon, «باربر هاوس» and «نشط»; the shop page's tag chips and «٨ أشادوا بالالتزام بالوقت».
- **After the first commit (`028e62c`):**
  - **The coupon hint.** The review's hint still said the shop applies the coupon at the visit and that the amount shown is before it (D-132 wording, the line in the client's 1-3 screenshot). It now reads «يُستخدم الكوبون عند انتهاء الخدمة، ويعود إليك إذا أُلغي الموعد.» (ar, en, tr), with a test.
  - **`dotnet ef migrations has-pending-model-changes`:** none.
  - **The E2E specs not run here** (`whatsapp-notifications`, `admin-operations`, `shops-professionals`, `account-legal`, `subscriptions`, the rest) contain none of the strings this change altered: «ر.س», `visit_completed`, the pager labels, `noPhone`, «بانتظار تأكيد المحل», the coupon code label, «إعادة جدولة», `shop-name`, `slot_unavailable`. They are deferred to the gates after the merge with D-151, since the shared stack belongs to another session now.
- **2-7 and 3-7:** forwarded to D-151's sessions and covered by D-151 (confirmed by the session finishing it): colleagues' bookings read-only with «جدولي / الكل / each colleague» on the barber's `/shop` and `/shop/appointments`; the barber's `/shop` is the day screen with the red «حجوزات تحتاج إقفال» block under the quick buttons; the late card's buttons under its text; `LoyaltyRulesForm` runs typed values through `toLatinDigits` and names the range («لازم تكون من … إلى …»). **Merge note:** D-151 makes the barber's `/shop` the day screen, so D-152's welcome line on `shop/page.tsx` must be carried into it.
- **Rebased onto D-151 (`abc6706`) as one commit, gates re-run on the merged code:**
  - Conflicts were in the shop home's imports (the welcome line now heads D-151's barber day screen too), `notices.ts` (both parameters kept) and the docs. The design deviation became DV-A40, since D-151 took DV-A39. Four traceability ids were renumbered: R-WEB-24, R-BKG-16, R-LOY-12, R-SD-30.
  - .NET: Release build 0 warnings; `has-pending-model-changes` none (both migrations, `BarberDayAndAccount` and `ShopShortName`); OpenAPI regenerated from the live API with no change; unit 508/508; architecture 69/69; **full integration 276/276**.
  - Web: typecheck, lint and format pass; `pnpm test`: 62 files, 586 tests; build passes; bundle 90 routes within budget (the shop page 219.6 / 220, «مواعيدي» 265.3, the barber home 296.1).
- **Waits on the user (a real iPhone, Safari, bar showing):**
  - 1-1: the bottom bars sit above the toolbar on the customer, barber, owner and admin pages, and the sticky headers clear the status bar;
  - 2-1: no sideways panning after typing in a field.
