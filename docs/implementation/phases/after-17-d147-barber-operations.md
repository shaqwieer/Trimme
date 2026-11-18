# After Phase 17 — D-147: the owner watches, the barbers operate; walk-in and phone bookings with several services; red bookings; reports and expected income; postponement offers

**Status:** [x] (Session 28, 2026-11-18) · requested by the user after D-146 · decision: D-147

## The request (user, Session 28: four screenshots of the client's document, sections 2-1 … 2-4)
- **Who does what** (the client's table):

  | | Owner | Barber |
  |---|---|---|
  | Appointments and calendar | view only | ✓ |
  | Walk-in or phone booking | ✗ | ✓ |
  | «وصل», «انتهت الخدمة», «ما حضر» | ✗ | ✓ |
  | Accept or reject a pending booking | ✗ | ✓ |
  | Move, postpone, cancel | ✗ | ✓ |
  | Breaks and the temporary booking pause | ✗ | ✓ |
  | Services, packages, barbers, shop settings | ✓ | ✗ |
  | Reports and expected income | ✓ | ✗ |
  | Customer's mobile | ✗ | bookings the shop registered only, until «انتهت الخدمة» |

- **2-1 The owner only follows.** Every action button leaves the owner's pages (finish, cancel, move to another barber, change the time, walk-in, breaks, pause). The owner keeps the overview, appointments, calendar, reports, and edits services, barbers and settings. «رقم العميل غير متاح» stays.
- **2-2 The barber's page.**
  - «وصل» then «انتهت الخدمة» (once; it may follow «وصل» at once); the booking leaves the list; the actual start and end are recorded.
  - «ما حضر» appears 15 minutes after the start: «الزبون ما حضر (تأخر أكثر من ربع ساعة)».
  - Red bookings: a booking whose time passed without being closed turns red and stays visible to all the shop's barbers for 3 days; any barber can close it, and it shows the responsible barber and who closed it. One closed late says «انقفل متأخر» and does not count in the speed figures.
  - Walk-in and phone: for themselves or a free colleague, several services, the customer's name and number.
  - A booking's services: adding a service only after «وصل» (the server refuses it before); removing until «انتهت الخدمة».
  - Breaks: the barber's own, every 5 minutes.
  - Pending bookings and the pause: any barber of the shop accepts or rejects a pending booking and can pause booking for a while.
  - Emergencies, in order: (1) move the booking to a free colleague; (2) postpone with the customer's consent, the offer held until one hour before the appointment; (3) cancel, with a reason required. A cancellation in the last 15 minutes is marked «إلغاء متأخر». A booking marked «وصل» cannot be cancelled.
  - Every action is logged with the name of who did it. The barber sees no statistics and no income. Arabic and Turkish.
- **2-3 Walk-in:** moves to the barber; several services or a package, durations and prices added; the step numbers fixed (they read ١، ٢، ٤); two kinds: «حضوري» (the customer is here, the time is taken at once) and «بالتلفون» (an appointment at another time).
- **2-4 Reports and expected income (owner):** per barber and for the shop, by day, week and month: moves to and from a barber, postponements, late cancellations, no-shows, breaks; each booking's actual start and end and each barber's average length, a booking under 5 minutes highlighted; expected income from completed bookings, labelled «متوقع» (no online payment), a free loyalty service counting zero; with loyalty on, the stamps given, the rewards redeemed and who gave them. The day's overview (capacity, each barber's load, new and returning, peak hours) stays.

## The user's answers (AskUserQuestion, Session 28)
1. **Postponement:** a held offer. The barber picks a new time; it is held for the customer, who accepts or declines on their booking page (in-app notice; a WhatsApp message would need a new Meta template). Without an answer by one hour before the **original** appointment the hold is released and the original booking stays (the barber can still move or cancel it). For online customers this replaces D-145's immediate change of time; for walk-in and phone customers the barber records the agreed time directly.
2. **The customer's mobile:** the table row is all of section 3. Barbers see the number of walk-in and phone bookings until any final status; the owner never does; online customers' numbers stay hidden; the number stays optional for both kinds.
3. **Breaks:** barbers set their own one-off breaks; the owner keeps the weekly breaks for the whole shop as a setting, like the opening hours.
4. **Packages:** the owner creates and edits packages (the admin keeps them too).

## Defaults (decided and recorded, not asked)
- **The pause** is the existing shop-wide online pause (D-013), moved from the owner to the barbers. A per-barber pause is described nowhere.
- **«وصل» is required before «انتهت الخدمة»** (it records the actual start). This reverses D-135's one-tap finish from Confirmed, except to close a red booking (below).
- **Red:** an open booking (Pending, Confirmed or Arrived) whose booked end passed more than 15 minutes ago (the document's quarter hour; without it every service that runs a little over would be "closed late" and leave the speed figures). Barbers see the shop's red bookings started within the last 3 days; older ones leave their lists and count as «لم يُقفل» in the owner's reports. Closing a red booking: «انتهت الخدمة» (also from Confirmed), «ما حضر», or for a pending one «رفض» (with a reason). Closed late = closed while red; it carries who closed it and leaves the speed figures.
- **Late cancellation** = cancelled by the shop or the customer less than 15 minutes before the start; derived from the history, no column.
- **Actual start and end** are read from the history (the «وصل» entry, or the creation of a walk-in that starts now; the «انتهت الخدمة» entry). No new columns.
- **Names in the log** are a snapshot on each history row (`actor_name`): barbers are renamed and deleted (groomhere.sa's demo barbers were deleted on 2026-11-18). One additive migration.
- **Phone bookings** get their own channel value (`Phone`) beside `Online` and `WalkIn`; both are «سجّله المحل».
- **Packages in a walk-in or phone booking:** several services, or one package on its own (as the customer's booking, D-125).
- **Adding a service** extends the booking's end; if the next booking is in the way the server answers 409 `booking.professional_busy` and the barber moves one of them first.
- **Expected income** = the booked price of completed bookings, minus a loyalty reward: a free service counts zero, a discount takes its percentage off.
- **Reports** are tables and CSS bars (no chart library): the largest route is 329.3 / 330 KB.

## Design notes (advisor-reviewed)
- **Rules on the server, not only the UI:** the owner gets 403 on every transition, note, reschedule, move, item change, break and pause; «ما حضر» from start + 15 min in `ApplyShopTransition` and `AllowedShopTransitions`; the barber's overview has no figures.
- **`BarberScope` gets three reviewed exceptions:** the shop's pending bookings, the shop's red bookings (3 days), and walk-in or phone bookings made for a colleague. `BarberScopeTests` lists them.
- **The number for barbers:** only `WalkIn`/`Phone` bookings, only while not final, decided by the server per request; never in an outbox event, SignalR, a notification or a log; a narrow, named exception in `ShopFacingContractTests`.
- **The offer's hold must be race-free in the database.** It lives in `bookings.booking_offers` and is checked against active bookings (and bookings against open offers) under a per-barber transaction lock taken by triggers on both tables, so the recheck cannot race; the availability engine treats an open offer as busy.
- **Deploy notes for the client:** groomhere.sa has real data and no seed: migrations stay additive. After this change a shop whose barbers have no login can do nothing with its bookings.

## Checklist
- [x] 1. Permission split: `ShopOwner` without status changes, notes, reschedule, move, barber-day breaks and pause; `ShopBarber` with the pause; navigation (`schedule` for either permission); the barber's schedule page with the pause card; the owner's schedule page without the day view; the owner's drawer, list and calendar read-only — with tests
- [x] 2. Visit rules: «وصل» before «انتهت الخدمة»; «ما حضر» from +15 min; late cancellation; actual start and end in the shop responses; any barber confirms or rejects a pending booking — with tests
- [x] 3. Names in the log: `actor_name` on the history (migration), filled for every actor; the drawer's «السجل» shows who did what
- [x] 4. Walk-in and phone: several services or a package; for a free colleague; the kind; the step numbers; the number shown to barbers until final
- [x] 5. Red bookings: the shop-wide list for barbers, closing late, closed-by, the 3-day window
- [x] 6. A booking's services: add after «وصل», remove until «انتهت الخدمة», per-item prices in the snapshot
- [x] 7. The barber's overview without figures or income
- [x] 8. The owner's packages
- [x] 9. Reports and expected income (owner): day, week, month; per barber and shop; loyalty
- [x] 10. Postponement offers: held slot, the customer's accept and decline, expiry one hour before the original start; walk-in and phone recorded directly
- [x] 11. E2E, route inventory, docs (D-147, TRACEABILITY, deviations, permissions matrix, STATUS, handoff)

## Changes found while verifying
- **The fork's review** (a forked agent rewrote and extended the integration tests): the new `Phone` channel was treated as online by Notifications (no «تأكيد الحجز» for the caller, a manage link to an account they don't have, the owner's inbox told of the shop's own booking) — fixed with `DispatchFactory.IsShopRecorded` and `ManageUrl`; the owner was not told when a barber acted on a colleague's booking — fixed with `IStaffDirectory.IsBarberAsync`; the owner could still add a barber's one-off break through `/shop/schedule/breaks` — now weekly only.
- **CA1720** on `ReportVisitResponse.Short` → `UnderFiveMinutes`.
- **The no-transfer guard** (`ProfessionalTests.OpenApi_HasNoTransferOperation`: no path contains «move») caught `…/services/{id}/remove`; the route is `…/services/{id}/delete` and the guard is unchanged.
- **Step numbers:** quantities use Latin digits (`formatNumber`), but the design numbers steps «١.»; every step now renders, so the numbers live in the copy again.
- **The advisor's review before the commit:** the report capped its bookings at 1000 and computed every figure from that list (a busy month would under-report) — figures now cover every booking, only the table is capped (500, `visitsTruncated` and a note); `/shop/loyalty` gave barbers the programme's statistics — null for barbers now (asserted); the Down migration dropped a trigger's function before the trigger; red rows offered «وصل» — now only «انتهت الخدمة» and «ما حضر»; CLAUDE.md, MASTER_PLAN, TRACEABILITY (R-NEG-04) and security.md now state the one phone exception, so a later session does not "fix" it.
- **Route audit:** axe `scrollable-region-focusable` on the reports' wide tables — each is now a focusable, labelled region (the admin roles matrix's pattern).

## Evidence
- **.NET** (Debug, analyzers on): build 0 warnings, 0 errors. Unit **478/478** (`BookingDomainTests` reworked + `RedBookings_ActualTimes_LateFlags_AndNames_D147`). Architecture **69/69**. Integration, full suite on the final code: **250/250** (new `BarberOperationsTests`; 16 test classes moved to the barbers' rules; OpenAPI regenerated with `TRIMME_UPDATE_OPENAPI=1`). After the route rename and the guard restore: OpenApi, Professional, BarberOperations, BarberScope and AuthorizationMatrix classes 29/29, then the full suite 250/250.
- **Final .NET, after the review fixes:** Release build 0 warnings, 0 errors; `has-pending-model-changes`: none; integration full suite **250/250** (BarberOperations + Loyalty + OpenApi + Migrations + ShopFacingContract classes 18/18 first).
- **Web:** `openapi:generate`, `typecheck`, `lint`, `format:check`, `openapi:check` clean; `pnpm test` 534/534 (new: drawer «ما حضر», the number for a barber, the owner's read-only drawer; list red/pending/closed-late sections and «قبول»; the owner's list without buttons; walk-in steps ١–٥, several services, a phone booking for a colleague; `PostponementOfferCard`; `reportPeriod`); `build` + `bundle:check`: 88 routes within budget, largest `/[locale]/shop/barbers/[professionalId]` 329.4 KB (budget 330).
- **Local stack:** rebuilt from this tree (`up -d --build`): migrate applied `BarberOperations` (25 migrations), seed ran, all services healthy; codes **on**.
- **E2E** (3300, 2 workers): full run 109 passed, 1 skipped (the codes-off spec, by design), 4 failed — the owner's route audit (ar, en; light and dark) on the reports' scrollable tables; fixed (focusable regions), web image rebuilt, route audit and keyboard pass light and dark **25/25**. The reworked flows passed in the full run: `shop-dashboard` (Omar's phone booking two days ahead, the number in his drawer only, the owner's read-only drawer and no number in the owner's traffic or live frames, `/ar/shop/reports` at 390/768/1440 without overflow) and `shop-schedule` (the owner's read-only pause, weekly-only breaks dialog, 403 on pause; the barber pauses and resumes).
- **The hold under concurrency** (added after the final review): `BarberOperationsTests.Postponement_AndABookingOfTheSameTime_SentTogether_NeverBothWin` fires an offer and another customer's booking of the same time together, five rounds; never both win (3 runs, 3/3), and the class 10/10. The full suite was 250/250 before this test was added (251 tests now).
