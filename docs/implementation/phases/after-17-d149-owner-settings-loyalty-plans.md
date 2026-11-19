# After Phase 17 — D-149: the owner's dashboard (sections, barbers, booking settings), the paid loyalty add-on, expiry, plans

**Status:** [x] (Session 28, 2026-11-19) · requested by the user after D-148 · decisions: D-149 (slices 1–3), D-150 (slices 4–5)

## The request (user, Session 28: two screenshots of section 6, and a pasted list of five owner-dashboard changes)
- **Owner 1. Packages and service sections.**
  - A package is a name, a set of services and a special price («حلاقة شعر ودقن بـ٧٠ ريال»); D-147 already gives the owner packages.
  - Sections such as hair, beard and care, which the owner orders.
- **Owner 2. Barbers.** Add a barber by mobile. For each barber: their services, **their own duration per service** (a haircut takes Ahmed 30 minutes and Mohamed 45), languages, photo and working hours.
- **Owner 3. Service status.** The state and the button must say different things: «متوقفة» with «تفعيل الحجز», then «متاحة للحجز» with «إيقاف الحجز».
- **Owner 4. Shop type.** Fixed to men's barbershop. «صالون تجميل» and «للجميع» are removed.
- **Owner 5. Booking settings per shop**, instead of one set for every shop from the admin:
  - automatic or manual confirmation;
  - the start-time interval (10:00, 10:15, 10:30…);
  - the minimum notice;
  - the furthest bookable day;
  - a cleaning time after each service.
  - The start-time interval is not the service's length: starts every 15 minutes, a service of 45.
- **6-1.** The furthest day is 14, as in the app, and the date strip always reads the setting. Today it is 30 days, against a strip of 14.
- **6-2. Loyalty** (Faisal's decision):
  - **Paid add-on:** the admin only switches it on or off, and it appears on the shop's subscription page. When it ends it stops by itself, and the customers' points are kept.
  - **Points:** per mobile number per shop. Walk-in and phone visits count when the number was typed, and two accounts with the same number find the same points.
  - **Per-shop settings:** points for the first visit (1 or more), a point for each later visit, and the points needed for the reward.
  - **Reward:** a free service, or a fixed amount off (5 to 1,000 riyals) instead of the coupon.
  - **Without visits:** depending on the shop, the card is wiped, or a point is taken off for each period without a visit.
  - **A full card doesn't stop:** «+٢» appears and the extra goes to the next card.
  - **Using a reward:** «استخدم مكافأتي» at booking. The points come off when the visit is finished and return on a cancellation or no-show. Not inside a package, and one reward per booking.
  - Points are added only at «انتهت الخدمة», with the barber's name.
  - After the visit: «خلصت زيارتك» with the points earned and how many are left.
- **6-4. Expiry hides the shop by itself** (Faisal's decision):
  - out of search, lists and the map, with no new bookings and no admin step;
  - the admin turns it back on after payment;
  - the shop link shows «المحل غير متاح حالياً»;
  - suggested: warn the owner a week and a day before; confirmed bookings stay.
- **6-5. Plans** (Faisal's decision): «الاحترافية» at 489 riyals a month (every shop feature) and «الاحترافية بلس» at 639 a month (the same plus up to 500 WhatsApp messages a month). Half-year and yearly have no approved price yet; section 8 was not provided.

## The user's answers (AskUserQuestion)
1. **Sections:** each shop's own: the owner adds, renames, orders and deletes them, and the tabs follow the shop's order. The search page's category filter is removed; text search still finds services.
2. **Durations:**
   - a package keeps the duration the owner set;
   - service tiles show the standard duration;
   - the barber's own duration applies once a barber is chosen, and for «أي مختص» each barber's own;
   - prices stay the shop's.
3. **WhatsApp:** «الاحترافية» has none (in-app notices only). «الاحترافية بلس» sends up to 500 customer messages per calendar month, then WhatsApp pauses until the next month while in-app continues, and the owner is told.
4. **Half-year and yearly plans:** hidden from new shops; current periods untouched. This is admin data, not code.

## Defaults (decided, not asked)
- **Loyalty:**
  - the owner sets the rules; the admin only switches the add-on on or off;
  - the add-on is a period the admin records, with an end date and an admin-set, versioned price, and no payment screen;
  - coupons already issued are honoured until they expire.
- **Booking settings:**
  - the furthest day moves to 14 for every shop by migration (the client's decision, like D-148's cancellation window);
  - the platform values become the defaults for new shops;
  - the cancellation window, review window and reminder stay platform-wide.
- **Shops:** every shop becomes «حلاقة رجالية» by migration. The JSON-LD type stays `HairSalon`: schema.org has no barbershop type.
- **Barbers:** languages come from a fixed list; a barber's mobile is their login (D-145/D-146).
- **«خلصت زيارتك»** is in-app until a WhatsApp template is approved by Meta.
- **Expiry:** the code already hides an expired shop (`HiddenBySubscription`, `HideAndBlockNewOnlineBookings` by default) and warns at 14, 7, 3 and 1 days. Missing: the shop page still renders (noindex) instead of «المحل غير متاح حالياً». The enforcement setting on groomhere.sa is checked at deploy time.

## Slices (each verified and committed locally before the next; no deploy until asked)
1. **Small items:** service status copy and buttons (3); shop type fixed (4); horizon 14 with the dates endpoint's default range equal to the horizon (6-1); the unavailable page for hidden shops (6-4); the owner's manual-confirmation switch.
2. **Per-shop booking settings (5):** confirmation, start interval, minimum notice, furthest day and cleaning time, set by the owner (and the admin on the shop's admin page).
3. **Barbers and sections (1, 2):** a duration per barber per service, languages, adding by mobile, and the shop's own sections.
4. **Loyalty add-on (6-2).**
5. **Plans (6-5):** a WhatsApp allowance on plans, enforced when messages are sent; the prices as admin data.

## Checklist
- [x] Slice 1
- [x] Slice 2
- [x] Slice 3 (slices 1–3 share the backend migration `OwnerSettings`, so they were verified and committed together)
- [x] Slice 4
- [x] Slice 5 (slices 4–5 share the backend migration `LoyaltyAddOnAndPlanMessages`, so they were verified and committed together)
- [x] Docs: D-149, D-150, TRACEABILITY (R-LOY-03…09, R-SUB-07…08), DV-A38, STATUS, MASTER_PLAN, handoff

## What was built (slices 1–3)
- **Backend** (a forked agent, reviewed): migration `OwnerSettings` (27 migrations); per-shop booking settings (`GET/PUT /shop/booking-settings`, `/admin/shops/{id}/booking-settings`); cleaning time through `blocked_until` in the engine, the exclusion constraint and the hold triggers; a barber's own length per service (`services: [{serviceId, durationMinutes}]`, `ownDurationMinutes`, `professionalDurations` on public services); languages; the owner's barber with an optional English name; shop sections (owner, admin and public endpoints; `sectionId` on services); shop type fixed; dates default to the shop's furthest day (cap 90).
- **Added on top:** the dev seed gives each demo shop its sections (only a shop with none; asserted in `ScheduleTests.DemoSeed…`); the platform's furthest day is capped at 90 in the API and the admin form.
- **Web:**
  - «إعدادات المحل» › «إعدادات الحجز» (`BookingSettingsForm`) and the admin's «إعدادات الحجز» tab;
  - «أقسام الخدمات» on «الخدمات» and on the admin's services tab (`ServiceSections`); «القسم» on the service form (the old category sent back unchanged);
  - the shop page, the booking's service step and `book/page.tsx` read `/public/shops/{slug}/service-sections`; catalog types renamed from categories to sections;
  - a length select per assigned service on the barber's services form; `durationFor` in the wizard (summary, end time, a barber card whose length differs);
  - languages on the admin's and owner's barber forms and the public barber page (with `knowsLanguage` in its JSON-LD); the owner's add-barber form starts with «جوال الحلاق»;
  - from earlier in the slice: the service status badge and button, no shop-type field, no search category filter, «المحل غير متاح حالياً» for a hidden shop, the platform settings named as defaults.
- **Fixed while verifying:** the admin booking page's intervention buttons now wrap («Customer didn't come (over a quarter hour late)» widened the page by 29 px at 390 px once a seeded booking was a quarter hour late).
- **Bundle budget:** the admin shop page carries the two new forms (330.7 KB); its "map pages" budget is now 335 KB. Loading them with `next/dynamic` from the server page was tried and grew every route by about 12 KB, so it was reverted. Every other route is unchanged in budget; the public shop page is 219.7 / 220 KB.

## Evidence (slices 1–3)
- **.NET** (re-run here, not only the fork's report): Release build 0 warnings; `has-pending-model-changes`: none; unit 483/483; architecture 69/69; `OwnerSettingsTests` 8/8; `ScheduleTests.DemoSeed…` 1/1 with the sections assertion; full integration 264/264 (Release, after the seed and validator changes).
- **Web:** lint, typecheck, format, `openapi:check` pass; `pnpm test` 61 files, 549 tests (new: sections add/move/delete/admin rename, the form's section, a barber's own length, `BookingSettingsForm` ×4, `durationFor` ×2, the wizard's barber length ×2, the add-barber form by mobile with languages); `pnpm build` and `bundle-budget.mjs`: 89 routes within budget.
- **E2E** on the rebuilt local stack (api and web together; `migrate` up to date at 27; seed ran; al-asala 5 sections, barber-house 4):
  - full run: 110 passed, 1 skipped, 3 failed. `services.spec` still clicked the old switch (now «Stop booking: …» and «Turn on booking: …»); the route audit (light and dark) found the admin booking page overflow above;
  - after the fixes and a web rebuild: `services.spec` and the whole route audit (every project, including `/shop/settings/booking`, `/admin/shops/{id}?tab=services` and `?tab=booking`) 23/23 passed;
  - `shop-barbers.spec` («Barber's mobile», Urdu) and `shops-professionals.spec` (no «Shop type») passed in the full run.
- **Manual (local stack):** `lamsat-al-rajul` (subscription ended 2026-11-03) answers 404 «المحل غير متاح حالياً» on its page and booking page; al-asala renders with its section tabs.

## What was built (slices 4–5, decision D-150)
- **Backend** (the same forked agent, reviewed):
  - migration `LoyaltyAddOnAndPlanMessages` (28 migrations; tables and columns only, no data changed);
  - the loyalty add-on in Subscriptions: versioned prices and periods;
  - points per mobile-number hash in Loyalty (settings, accounts, entries, redemptions), with earning at «انتهت الخدمة», overflow, inactivity rules, hold/use/return, the barber's lookup, «خلصت زيارتك», and customer and public endpoints;
  - `useLoyaltyReward` on bookings and walk-ins, with `amountDue` and the discount;
  - the plan's `monthlyWhatsAppMessages`, the allowance gate at dispatch, the owner's monthly notice, and `messageUsage` on both subscription pages.
- **Changed on top of the agent's work:**
  - the owner's subscription response leaves out the admin's notes on add-on periods (asserted in `LoyaltyTests`);
  - an amount-off reward is titled the way the app writes prices («خصم ٥٠ ⃁»; `LoyaltyBalanceTests` ×2).
- **Web:**
  - the loyalty parts: the rules in words, a points card with «+N» and a bar beyond 12 points, the history, the stats;
  - `/account/loyalty` and the shop page's card (heading and rules on the server, the points card lazy);
  - «استخدم مكافأتي» in the wizard (`rewardFit`, the discount and the amount due);
  - the walk-in lookup and reward;
  - `LoyaltyRulesForm` on «إعدادات المحل» › «برنامج الولاء» and on the admin's loyalty tab (replaces `LoyaltyProgramForm`);
  - `LoyaltyAddOnPanel` and `MessageUsage` on the admin's subscription tab, and both on the owner's subscription page;
  - the plan form's monthly WhatsApp field, the plan list's column, and the add-on's price versions on the plans page;
  - the two notices (`loyalty.visit_completed`, `whatsapp.allowance_reached`);
  - the reward discount on the customer's booking page and in the barber's drawer;
  - the reports' words: points rather than stamps.
- **Fixed while verifying:** a late booking's second line on the owner's dashboard was below 4.5:1 on the red card.

## Evidence (slices 4–5)
- **.NET** (re-run here, not only the agent's report):
  - Release build: 0 warnings.
  - `has-pending-model-changes`: none.
  - unit 498/498 (500/500 after the two title tests); architecture 69/69.
  - full integration 270/270 (264 before; `LoyaltyTests` 7 replaces D-132's 3, and `WhatsAppAllowanceTests` adds 2).
  - After the notes and title changes: `LoyaltyTests` and `SubscriptionTests` 15/15, then `LoyaltyTests` 7/7.
- **Review:**
  - no test removed or skipped;
  - the only guard change registers the barber's lookup in `BarberScopeTests`;
  - a refused message raises no admin alert (`IsNotADeliveryFailure`);
  - no number or member key in the payloads the shop reads (`LoyaltyTests`).
- **Web:**
  - lint, typecheck, format and `openapi:check` pass;
  - `pnpm test`: 61 files, 570 tests. New: `LoyaltyRulesForm` ×3, `loyalty.test.tsx` rewritten (9), the wizard's points reward ×2, `rewardFit` ×3, the walk-in reward ×2, the plan's allowance, `MessageUsage` ×2, `LoyaltyAddOnPanel` ×3, the two notices' wordings;
  - `pnpm build` and `bundle-budget.mjs`: 90 routes within budget, none raised. The public shop page is 219.6 / 220 KB (heading and rules on the server, the points card with `React.lazy`), the booking 237.1 / 260, the admin shop page 332.8 / 335.
- **E2E** on the rebuilt local stack (`migrate` up to date at 28; api and web healthy):
  - the full run was **stopped by the host for low memory** at 91 passed and 22 failed;
  - 21 of the failures are the crash itself («Target crashed», «worker process exited unexpectedly»): the whole route audit and one WhatsApp flow;
  - the one assertion failure is the late booking's contrast, now fixed (web image not yet rebuilt).
  - **re-run** (the user chose the affected specs, 3 workers) after rebuilding the web image with the fix: `routes.spec` in both the light and the dark project, `shop-dashboard` and `whatsapp-notifications`, **23/23 passed**. The route audit covers every role, including `/shop/settings/loyalty`, `/admin/shops/{id}?tab=subscription` and `?tab=loyalty`, `/account/loyalty`, `/shop/subscription` and `/admin/subscription-plans`.
  - Not re-run after the kill: the smoke and flow specs that had already passed in the stopped run (91 passed there).

## After the five slices (D-153)
- The two items left as they were are done: the walk-in form shows each barber's own length; the admin's platform categories page, tab and editor are removed (the data stays).
