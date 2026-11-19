# After Phase 17 — D-155: always confirmed, one active booking, the barber's direct change, plans with features and the win-back allowance, pausing for the owner and admin, «غير متوفر اليوم», and the client's smaller fixes

**Status:** [~] (Session 29, 2026-11-19) · requested by the user after D-151 · decision: D-155

## The request (user, Session 29: twelve cards from the client's files)
The cards come from more than one client file, so their numbers collide (two 1-1, two 2-1 and two 2-2). Each card has a stable ID here (`I<image>-<client number>`). The client's tags are kept: ضروري (before launch), مهم (must change), تحسين (improves the experience).

- **I1-1-1 مهم: pausing online booking is for the admin and the owner only.**
  - It is removed from the barber's pages entirely: a barber could play with it and let only walk-ins in, while the site shows nothing free.
  - It stays with the admin and the owner, for big cases such as maintenance.
  - While paused, the shop stays visible to customers with «متوقف مؤقتاً», the reason (for example «تحت الصيانة») and when it returns. A customer can open the page and save it to favourites, but not book.
  - The admin setting «إخفاء المحلات التي أوقفت الحجز الإلكتروني» is removed.
  - Confirmed bookings are not cancelled by the pause. The shop gets a notice to act on them (change the time, or cancel with a reason), and the customer gets a message.
- **I1-1-2 مهم: an absent barber, «غير متوفر اليوم».** A barber who falls ill presses it themselves instead of calling the owner, and the owner sees it clearly.
  - **Who presses it:** the barber, for themselves only (never a colleague), or the owner. Who pressed it and when is recorded.
  - **The owner:** a clear notice on the dashboard («سعد غير متوفر اليوم — ضغطها الساعة ١:١٠ م»), and the reports count each barber's «غير متوفر» days.
  - **The customer:** the barber's card says «غير متوفر اليوم» and cannot be chosen, «أي مختص» never picks them, and their remaining times today disappear.
  - **Their remaining confirmed bookings today** are not cancelled. Their colleagues get a «حجوزات حلاق غير متوفر» notice and handle them in order: move to a free colleague, then change the time, then cancel with a reason.
  - **The barber's all-day break** becomes «غير متوفر اليوم»: the same idea with a clear name and the owner's notice. Short breaks stay as they are.
  - **Planned leave** (future days) and closing the shop stay with the owner.
- **I2-2-2 ضروري: how the site knows the plan and the 500 limit.**
  - **Admin → plans** (they exist with price and length): each plan gets features:
    - loyalty: on or off;
    - the win-back message: on or off, and after how many days (10);
    - the monthly marketing message limit (500).
    - The basic plan has loyalty and win-back off.
  - **Each shop follows its plan** (the shop page's subscription tab exists). Its features turn on and off with the plan, and change when the plan changes.
  - **A counter per shop per month.** Before any win-back message, the system counts the marketing messages sent for this shop this month (from the admin's message log).
    - Under 500: it sends and logs.
    - At 500: it does not send, and the log says «وصل الحد».
    - The counter returns to zero each month, from the shop's subscription date.
  - **The owner sees** «رسايل الدعوة هالشهر: ١٢٣ من ٥٠٠» on the subscription page or the reports. The admin sees the counter for every shop.
  - **Protection:** the limit is checked on the server at send time, not on the screen, so it holds even when many messages go out at once.
- **I3-1-1 ضروري: always confirmed.** Every online booking is «مؤكد» at once, in every shop.
  - The booking settings' «أؤكد الحجوزات بنفسي» is removed from the shop's settings and the admin's.
  - «بانتظار تأكيد المحل» is removed from the customer's screens.
  - Why: no customer waits, there are no rejection messages, and no booking is lost because a barber forgot to accept. The time is held as soon as it is booked, so there is no conflict.
- **I3-2-1 ضروري: one active booking per customer, across all shops.**
  - The client's test: one customer booked three times back to back at Al-Asala on Sunday the 22nd (GH-EFH7C 3:00, GH-W9494 3:30, GH-EXJMM 4:00), and all were accepted without a warning. One customer can hold a whole day.
  - **With an upcoming booking anywhere,** a second booking gets a warning before confirming: «عندك حجز قادم في صالون الأصالة الأحد ٣:٠٠ م. لازم تلغيه عشان تحجز موعد جديد», with a button to that booking.
  - **To change shop or barber:** cancel and book again. «تغيير الموعد» on the same booking is allowed, because it adds no booking.
  - **Once the barber records «انتهت الخدمة»,** the customer can book at once. The same once it is cancelled or recorded «ما حضر».
  - **A late booking the barber did not close** does not stop the customer from booking.
  - **The shop's own bookings** (walk-in or phone) are not limited.
  - **Checked on the server** at confirmation, not only on the screen.
- **I4-3-1 مهم: loyalty turns on with the higher plan, not as an add-on.**
  - A shop on the higher plan has loyalty by itself. If it moves down to the basic plan or its subscription ends, earning and using points stop, and the points are kept, as already built ✓.
  - The plan form needs a real «يشمل برنامج الولاء (نعم/لا)»; today the features are display text only.
  - The «إضافة برنامج الولاء» section (its price, and manual activation until a date) is removed from the plans page and the shop's subscription page.
- **I4-4-1 ضروري: the barber changes the time directly, without the customer's agreement.**
  - The client's test: Mohammed offered GH-EFH7C on Sunday at 5:00 instead of 3:00. The offer reached the customer on WhatsApp but was hidden inside «التفاصيل», and the booking did not change until the customer accepted.
  - **«تأجيل الموعد»:** the barber picks the new time and the booking changes at once. No offer waits for an answer.
  - **The customer** gets «تغيير موعدك» with the old and new times, and can cancel or change for free. The template exists and arrived in the test ✓.
  - **Removed:** the «الحلاق يقترح موعداً جديداً» box, the accept and decline buttons, and «الموعد محجوز لك حتى …».
  - **Logged** in the booking's history: who changed it and when.
  - **Limit:** the shop cannot change the same customer's booking more than once in a day. After the first change, the button explains: «غيّرت موعد هالزبون اليوم. إذا صار ظرف ثاني: انقل الحجز لزميل أو ألغه مع ذكر السبب». Checked on the server.
- **I5-1-6 مهم: the shop's cancellation gets its own message, with the reason.**
  - The client's test: Mohammed cancelled GH-EXJMM with «ظرف طارئ عند الحلاق», and the customer got the same message as when they cancel themselves. The customer: «أنا ما ألغيت!».
  - The shop's template: the shop apologises, gives the reason the barber wrote, and a «احجز من جديد» button ✓.
  - Example: «نعتذر منك، صالون الأصالة اضطر يلغي موعدك الأحد ٢٢ نوفمبر الساعة ٤:٠٠ م. السبب: ظرف طارئ عند الحلاق. رقم الحجز: GH-EXJMM. تقدر تحجز موعد ثاني من الزر تحت.»
  - It is one of the basic messages (cancellation), so no extra cost.
- **I5-1-7 مهم: a «اتصال بالمحل» button, an admin-only option.**
  - Today the customer has no call button, which is right to start with.
  - **Admin → shop:** «إظهار زر الاتصال للزبون» (off by default), and the admin sets the number.
  - **When on,** the customer sees «اتصال» on the shop's page.
  - **The owner** never sees the option and has no access; it appears nowhere on their pages. The admin adds it only for shops that ask.
- **I6-2-2 تحسين: booking settings, how lengths are written.**
  - «٥ دقائق» and «١٠ دقائق» instead of «٥ دقيقة».
  - «٦ ساعات» instead of «٣٦٠ دقيقة», «١٢ ساعة» instead of «٧٢٠ دقيقة», «٢٤ ساعة» instead of «١٬٤٤٠ دقيقة».
- **I7-2-3 تحسين: a barber's QR code is made by itself.** The shop's code exists ✓, but the new barbers (Saad and Mohammed) have no code until the platform team makes one. Better: the barber's code is made when they are added.
- **I8-3-5 تحسين: the admin's customers: «ما حضر» and cancellation counts.** Each customer shows how many times they did not come and how many they cancelled, so the ones who book and never come stand out.
- **I8-3-6 مهم: the activity log in Arabic.** It works in detail ✓, but shows English and code names: «loyalty.program_saved», «By the shop: 5 services assigned», «barber_login.enabled», «Login made with the barber, temporary password generated», «Created as Draft», «LoyaltyProgram».
  - Every action and its details in Arabic in the Arabic interface. Example: «حفظ برنامج الولاء: زيارتين، أول زيارة نقطة، المكافأة خدمة مجانية».
- **I9-3-7 تحسين: «المصدر: أونلاين».** The barber's booking details say «المصدر: تطبيق العميل» for a booking made on the site. «أونلاين» is clearer.
- **I9-3-8 تحسين: the owner's barber page has two save buttons.** «حفظ الإسناد» and «حفظ الملف»: saving one loses the other's unsaved changes without a warning. The client's test: they ticked Turkish and changed a length, saved the length, and the Turkish tick was gone.
  - One save button for the page, or a warning «عندك تعديلات ما انحفظت».
- **I9-3-9 تحسين: the barber's languages in the specialists list and the booking step.** They show on the barber's own page ✓, but customers choose a barber from the «المختصون» tab and the booking step without opening that page.
  - A languages line under the barber's name on both cards.
- **I10-5-1 مهم: the customer's bell keeps every message.** The new-time offer's notice arrived ✓, but today's three bookings got no confirmation notice, and the page holds only the offer.
  - Every WhatsApp message to the customer is also kept in the bell: confirmed, changed time, cancelled, barber changed, reward ready.
  - So the customer has a full record even if a WhatsApp message is lost.
- **I11-5-4 تحسين: «احجز مثل آخر مرة».** The customer cuts their hair about every 6 days, so booking again should be one tap.
  - **At the top of the home page:** a card of the last visit (shop, barber, services) and «احجز نفسها», which goes straight to choosing a time.
  - **In «سابقة»:** «إعادة الحجز» on every completed visit. Today it appears only after rating.
  - **In the favourites:** «احجز» beside each shop.
- **I12-2-1 ضروري: the WhatsApp limit applied to every message.** D-150's limit counts all customer messages, so a shop at 500 stops even booking confirmations. The client's fourth file meant the limit for the win-back message only.
  - Confirmation, change and cancellation messages always go out. They are not counted and never stop.

Numbers missing from the cards (1-5, 3-1 to 3-4, 5-2, 5-3 of the main file) were not sent; if they exist, they are not part of this request.

## The user's answers (AskUserQuestion, Session 29)
1. **D-151 first:** finish D-151's remaining gates, record the evidence and commit it alone; D-155 builds on top.
2. **The client's text contradicts itself** (I1-1-1 and I1-1-2 say «بموافقة الزبون», I4-4-1 says no agreement): **a direct change everywhere.** The shop moves the time at once; the customer gets «تغيير موعدك» and can cancel or change for free; once per customer per day.
3. **The pause's return time:** optional; when set, online booking **reopens by itself** at that time, and the owner or admin can resume earlier.

## What the code says (three read-only searches, Session 29)
- **Pausing:**
  - today only a barber can pause; the owner and the admin cannot (`SystemRoles.OperatedByBarbers` holds `Shop.OnlineBooking.Pause`), and no admin route exists;
  - with `HidePausedShopsFromDiscovery` (default true) a paused shop gets a 404 «المحل غير متاح حالياً» on its page, its booking page, its barbers' pages and QR links, and drops out of favourites;
  - the reason is never public, and there is no return time.
- **«بانتظار تأكيد المحل»** is D-148's label for an open booking past its end (`CustomerStatusBadge`), not the Pending status. D-152 (worktree, 2-3) already replaces it with «مؤكد». The Pending status itself shows as «تم إرسال طلب الحجز» after booking and «بانتظار التأكيد» in the lists.
- **Postponement offers** are a table (`bookings.postponement_offers`) with hold triggers and functions. Nothing makes an offer lapse: it is derived from `expires_at`. An offer is never sent on WhatsApp; it is an in-app notice only.
- **No hold step before an online booking.** `POST /bookings` books in one transaction.
- **No shop-cancellation template.** Every cancellation sends `BookingCancelled`, and the outbox payload carries no reason. A ready reason (D-151) is never shown to the customer.
- **The rescheduled template** has the new time only (no old time). The client marked it ✓, so it stays; the bell's copy adds the old time.
- **The D-150 allowance** counts every customer message (`CountsAgainstAllowance`) over the shop's calendar month. Taking a slot is an atomic upsert (`used < cap`). A refusal is stored as `Failed`.
- **No win-back message, job or marketing opt-out exists.** Every Meta template is UTILITY.
- **Loyalty "active"** is the add-on's coverage dates (`SubscriptionCoverage.LoyaltyAddOnStart/End`), read through `ILoyaltyAddOnStatus`. The plans' "features" are display text only.
- **An all-day break** is the barber's remaining free slots filled with one-off breaks. It is not time off, has no marker, and the owner's dashboard shows such a barber as working with 0 minutes free.
- **Barber QR codes** are made only by an admin (`POST /admin/qr/codes`).
- **The admin customers list** computes cancelled and no-show counts and drops them (`CustomerAdmin.cs:62`).
- **The activity log:**
  - action keys map through `adminAudit.actions` (71 keys; 16 current keys are missing);
  - summaries are English text shown as is;
  - the booking settings' sentence is stored as the reason.
- **Languages** are already in `PublicProfessionalResponse`; the web drops them on the cards. `ProfessionalOption` already has an `unavailable` state.
- **The customer's home** is `/discover`, with no last-visit card. «سابقة» shows rebook only after rating, and the favourite shops have no book button.
- **`Shop.PublicPhone`** exists, but the owner can edit it and D-129 removed it from the public page.

## Defaults (decided, not asked)
- **Pausing:**
  - the owner (`Shop.OnlineBooking.Pause` moves from the barbers to the owner) and the admin (`POST /admin/shops/{id}/online-booking/pause|resume`, `Admin.Shops.Manage`);
  - a reason (shown to customers) and an optional return time; a recurring job resumes it, and the bookability gate treats a pause past its return time as over;
  - `HidePausedShopsFromDiscovery` is removed: a paused shop stays listed, its page and favourites work, and it books nothing;
  - on pausing, the shop gets one in-app notice with the count of confirmed bookings inside the pause («عندك N حجوزات مؤكدة وقت الإيقاف»), which leads to the day screen. The customers get the normal change or cancellation message when the shop acts.
- **«غير متوفر اليوم»:**
  - a time-off kind `Unavailable` from now to the end of the shop's day, made by the barber for themselves (`POST /shop/schedule/unavailable-today`) or the owner for any barber, with who and when stored on the row and audited. Either can undo it the same day.
  - The public shapes get `unavailableToday`. The card is disabled with «غير متوفر اليوم», «أي مختص» never picks them, and their times are gone (it is time off).
  - The owner gets an in-app notice and a dashboard line. The shop's barbers get a notice; the attention list gets a block of that barber's open bookings today.
  - Any barber of the shop may change the professional, change the time or cancel those bookings: a fifth reviewed `BarberScope` exception, today's bookings of a barber marked unavailable only.
  - The reports count each barber's «غير متوفر» days. «استراحة طوال اليوم» on «بريكاتي» becomes this action.
- **Always confirmed:**
  - `RequireManualConfirmation` is dropped. The migration confirms open Pending bookings (a history line by the system, no message) and removes the `BookingPending` opt-out rows.
  - `BookingStatus.Pending` stays in the enum for history, with no way into it.
  - Removed: the pending block, «قبول»/«رفض», the switch and the «تم إرسال طلب الحجز» banner.
  - The `BookingPending` template stays registered but is never sent.
- **One active booking:**
  - an open online booking of the customer's (Confirmed or Arrived; Pending is gone) whose booked end has not passed, at any shop, refuses a new one: 409 `booking.active_booking_exists`;
  - checked outside the shop scope in the create transaction, under a per-customer advisory lock;
  - `GET /me/bookings/active` lets the wizard warn before confirming: «عندك حجز قادم في … لازم تلغيه عشان تحجز موعد جديد», with «عرض الحجز»;
  - walk-in and phone bookings are never counted or limited; the customer's own reschedule is not limited.
- **The barber's direct change:**
  - `POST /shop/bookings/{id}/reschedule` moves an online booking at once (`booking.consent_required` goes). `/postpone`, the customer's accept and decline, the offers table, its triggers and functions are removed; open offers are dropped with it.
  - Once per customer per shop per shop-local day for the shop's users: 409 `booking.changed_today`, and `ShopBookingResponse.changedToday` so the drawer explains it.
  - Walk-in and phone bookings and the admin are not limited.
  - The customer gets the existing «تم تغيير موعد حجزك» message; the bell's copy names the old and new time.
- **The shop's cancellation:**
  - a new event `BookingCancelledByShop` with `{{reason}}` (the ready reason's words or the typed text), sent when the shop or the admin cancels;
  - a new Meta UTILITY template `trimme_customer_booking_cancelled_by_shop` (ar, en), with the «احجز من جديد» button;
  - the customer's own cancellation keeps `BookingCancelled`. The customer's booking page shows a ready reason's words too.
- **Plans:**
  - `includesLoyalty`, `winBackEnabled`, `winBackAfterDays` (1–90, default 10) and `monthlyMarketingMessages` (D-150's `MonthlyWhatsAppMessages`, renamed: null = unlimited, 0 = none);
  - loyalty is on while the shop's subscription is in force (not expired or suspended) on a plan that includes it;
  - the add-on's tables, coverage columns, endpoints, panel and price form are removed (check on production that no add-on period exists before migrating).
- **The win-back message:**
  - a daily job. For each shop whose plan has it on, each account customer whose last completed visit there was at least N days ago, has no open booking anywhere, has not opted out, and has not had a win-back for that visit.
  - One WhatsApp (new MARKETING template `trimme_customer_win_back`, ar and en, «احجز من جديد») and a bell copy.
  - Counted against the plan's monthly marketing limit, per shop, in windows that start on the subscription's start day. The slot is taken atomically on the server at send time.
  - At the limit the dispatch is `LimitReached` («وصل الحد») and nothing is sent. The owner sees «رسائل الدعوة هالشهر: ١٢٣ من ٥٠٠»; the admin sees it on the shop's subscription tab.
  - A customer can turn win-back messages off on «حسابي» (on by default).
- **Only win-back counts:** confirmations, changes, cancellations and reminders are never counted and never stopped.
- **The bell:** every WhatsApp a customer gets also lands in their bell (confirmed, changed, cancelled, cancelled by the shop, barber changed, reminder, win-back), including their own actions. Walk-ins have no account, so nothing.
- **Call button:** admin-only `callButtonEnabled` and `callButtonPhone` on the shop (not `PublicPhone`, which the owner edits), shown as «اتصال» (`tel:`) on the public page only when on. Never on a shop-facing payload.
- **Lengths:** `formatDurationMinutes` follows Arabic plural rules (دقيقة، دقيقتان، ٣–١٠ دقائق، ١١+ دقيقة); the booking settings write whole hours as hours («٦ ساعات», «١٢ ساعة», «٢٤ ساعة»).
- **Barber QR:** a barber gets a code when added (owner or admin) or re-enabled without one, through a BuildingBlocks port implemented in QrAnalytics. A reference-data synchronizer backfills active barbers without one at `migrate`.
- **The admin customers list** gets «ما حضر» and «ألغى» columns.
- **The activity log:**
  - every action key and entity type gets ar, en and tr words;
  - summaries become a code with arguments (`summary_code`, `summary_args`) rendered in the reader's language;
  - old rows keep their English summary, marked as such;
  - the booking settings' sentence moves from the reason to the summary.
- **«أونلاين»** replaces «تطبيق العميل».
- **The barber page:** one «حفظ» for the profile and the services (two requests in order), and a warning before leaving with unsaved changes.
- **Languages** under the name on the specialists tab and the booking step.
- **«احجز مثل آخر مرة»:**
  - a last-visit card at the top of `/discover` for a signed-in customer, with «احجز نفسها»: the wizard with the shop, barber and services, at the date step;
  - «إعادة الحجز» on every completed visit in «سابقة»;
  - «احجز» beside each favourite shop.

## Slices (each verified before the next; one commit at the end if all gates pass; no push or deploy until asked)
1. **Backend A** (a forked agent, reviewed here; one migration): always confirmed; one active booking; the direct change and once a day; the shop's cancellation message; plans' features and loyalty from the plan (the add-on removed); win-back with the marketing counter, `LimitReached` and the opt-out; only win-back counted; the bell's copies.
2. **Backend B** (a forked agent; one migration): pausing (owner and admin, return time, resume job, stays listed); «غير متوفر اليوم»; barber QR on add with the backfill; the call button; the customers' counts; the activity log's codes.
3. **Web**, for every item.
4. **Gates:** .NET build, unit, architecture and integration; OpenAPI; web format, lint, typecheck, test, build and bundle budget; E2E and the route audit on the rebuilt local stack, at `--workers=1`.

## Checklist
- [x] I3-1-1 always confirmed
- [x] I3-2-1 one active booking
- [x] I4-4-1 the direct change, once a day
- [x] I5-1-6 the shop's cancellation message
- [x] I2-2-2 + I4-3-1 plans' features; loyalty from the plan; the add-on removed
- [x] I2-2-2 + I12-2-1 win-back, the marketing counter, «وصل الحد», only win-back counted
- [x] I10-5-1 the bell keeps every message
- [x] I1-1-1 pausing for the owner and the admin; stays visible; return time
- [x] I1-1-2 «غير متوفر اليوم»
- [x] I7-2-3 barber QR on add
- [x] I5-1-7 the admin's call button
- [x] I8-3-5 the customers' counts
- [x] I8-3-6 the activity log in Arabic
- [x] I6-2-2 lengths in words
- [x] I9-3-7 «أونلاين»
- [x] I9-3-8 one save on the barber page
- [x] I9-3-9 languages on the cards
- [x] I11-5-4 «احجز مثل آخر مرة»
- [x] Docs: D-155, TRACEABILITY, deviations, permissions matrix, security, STATUS, MASTER_PLAN, handoff

## Previous phase's decisive check
- D-151's remaining gates were run here before its commit (see its phase file).

## Progress (2026-11-19)
- Backend A done by a forked agent and reviewed here.
  - Migration `AlwaysConfirmedPlanFeaturesWinBack` (20261119171144).
  - Release build: 0 warnings.
  - Unit 525/525, architecture 69/69, full integration 276/276; after its last three fixes the touched namespaces were re-run (94/94 and 17/17).
  - The model check shows no changes.
- Web for backend A wired:
  - plan form features and the 500 default; the plan list's features column;
  - `PlanUsage` («رسائل الدعوة هالشهر: ١٢٣ من ٥٠٠», «وصل الحد») on the owner's and the admin's subscription pages; the add-on panels removed;
  - the loyalty form's plan wording; the pending UI removed (board block, KPI, chip, legend, drawer «قبول»/«رفض», created banner);
  - the drawer's direct «تأجيل الموعد» with the once-a-day explanation; the postponement offer removed;
  - the wizard's «عندك حجز قادم» with «عرض الحجز»;
  - «ندعوك للعودة» on/off on the profile; the bell's `customer.*` and `marketing.limit_reached` notices;
  - the WhatsApp admin's new events and the «وصل الحد» status.
- Web checks so far:
  - typecheck, lint and format are clean;
  - web unit tests 588 (one stale pending test was rewritten and passes);
  - E2E specs updated: E1/E6 use their own customers, E1 checks the warning, the barber page's single save, and the plan's features on the subscription tab.
- Backend B is running as a forked agent.

## Evidence (2026-11-19, Session 30)
- **Backend:**
  - Slice A (`AlwaysConfirmedPlanFeaturesWinBack`) and slice B (`PauseUnavailableCallAuditCodes`), built by forked agents and reviewed here.
  - Slice B's final full integration run covered both slices: **280/280**, after a targeted 72/72.
  - Re-run here: Release build of `Trimme.slnx` with **0 warnings**; unit **527/527**; architecture **69/69**.
  - `has-pending-model-changes`: none (slice B).
  - OpenAPI regenerated and `pnpm openapi:check` up to date; the Meta snapshot regenerated, with 26 templates.
- **Web:**
  - format, lint and typecheck pass;
  - `pnpm test`: **63 files, 595 tests**;
  - `pnpm build` passes;
  - `bundle:check` passes over 90 routes; the largest is `/admin/shops/[shopId]` at 335.6 KB against the map pages' budget, raised from 335 to 337 KB (D-155).
- **Local stack:**
  - recreated **fresh** (`down -v`, `up -d`): **32 migrations** from an empty database, 5 synchronizers, all 13 seeders;
  - then the web image rebuilt after the last message fix.
- **E2E** (`--workers=1`, smoke, flows, a11y and a11y-dark): **108 passed, 5 failed, 1 skipped** (15.4 min).
  - The 4 admin route audits (ar and en, light and dark) failed on a missing label for the new `{{reason}}` placeholder (`adminWhatsApp.placeholders.reason`), now added.
  - `account-legal` missed «حُفظ النص ونُشر.» within 5 s although the save answered 200, so a timing flake. It passed in the first run.
  - Re-run on the rebuilt web image: **5/5 passed**.
  - Earlier runs on the old volume found and fixed three spec issues:
    - E1 now waits out the 30-second code limit after its own sign-up;
    - the favourites check is exact beside the new «احجز في …» link;
    - E7 needed a fresh volume.
- **Not checked here:**
  - the real Meta templates (two new ones to submit);
  - the win-back job against real customers;
  - an iPhone pass;
  - production data (the add-on check before migrating).
