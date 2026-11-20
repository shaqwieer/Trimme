# After Phase 17 — D-158: «حجز حضوري» opens on its four choices, hour-then-minutes walk-in times, the barbers' calendar tab, red «ما حضر», the shop's owner and a temporary password, terms accepted once, no win-back switch

**Status:** [x] except the client's phone check (Session 32, 2026-11-20) · requested by the user (one message, eight screenshots) · decision: D-158
**Deployed:** 2026-11-20 with D-156, D-157 and D-159 (`a49bedb`, fast-forwarded into `main`), api and web, no migration; backup `/root/backups/trimme-before-d156-d159-20261120-154159.sql.gz`; smoke and evidence in `SESSION_HANDOFF.md` (Session 32, trimme-b7).
**Where:** worktree `.claude/worktrees/d158`, branch `d158-shop-admin-terms`, rebased onto `main` at `c79725a` (D-156, D-157 and D-159 by trimme-0c and trimme-44, committed while this was built). E2E on its own compose project `trimme-d158` (web 3400, API 8180, DB 5435, Mailpit 8425).

## The request (the user's words, numbered)
1. **Admin → المحلات → a shop:** «where i can see the owner of shop and also i need option to reset password manual and give to the barber owner to login again and change it».
2. **Terms** (image 1, the sign-up page with its box): «when client login if one time approve on terms it appear to him that he is agree on terms previous and not need to again approve».
3. **Win-back** (image 2, «رسائل «ندعوك للعودة»» on the customer's profile): «please hide this from ui in customer».
4. **The four buttons** (image 3, on «النظرة التشغيلية»): «this box appear on حجز حضوري only».
5. **«حجز حضوري»** (image 4): «مجرد ما اضغط عليها تدخلني كذا، لا خلها أول شي يختار واحد من الأربعة الي فوق (بريك، حضوري، الخ) بعدين تتعدل الشاشة بما يوافق الحاجة».
6. **The times:** «أول ما يحاول يسجل زبون يطلع له الأوقات المتاح فيها والي مو متاح تكون مغلقة، نفس الزبون».
7. **Hour then minutes** (image 5, a wall of 5-minute chips): «خيار يبدأ الآن جميل… اتفقنا الساعة بعدين الدقايق عشان تصير القائمة أصغر وأسهل».
8. **The bottom bar** (images 6–7): «بدل بين الصفحتين: التقويم هذا حلو مرة، مفروض يكون هو مكان ذا المواعيد، والمواعيد هي مخفية في القائمة الجانبية».
9. **The calendar** (image 8, its legend): «need red indicator for client absence also».

## What the code said
- **1.** The admin's «الحسابات» tab could invite or create an owner (D-139), but nothing listed the shop's existing owners, and an owner's password could only be reset by the emailed link (Resend is not set up for the client). A barber's «كلمة مرور مؤقتة جديدة» (D-146) and the staff sign-in's change step (D-139) were ready to reuse.
- **2.** Two places asked:
  - sign-up's box;
  - profile completion's box, unticked by default and required even when the account already held an acceptance. The server keeps the earlier date anyway (`account.TermsAcceptedAt ?? …`).
  - A returning customer on the same phone was asked on sign-up again.
  - The sign-in page sent `termsAccepted: true` without showing the terms.
- **4.** `SimpleAppointments`' `quickActions` rendered the four buttons on the barber's home and on «المواعيد». «بريك» and «غير متوفر اليوم» linked to `/shop/schedule#…`.
- **6–7.** The walk-in API returned only the free starts, and `WalkInFlow` drew them all as chips. The customer's `HourMinutePicker` needs each start's local time and the open windows.
- **9.** The calendar includes no-show bookings (only cancelled ones are left out). `blockTone.NoShow` was grey, and the legend had no entry for it.

## Defaults (decided, not asked)
- **The bottom bar:** the barbers' only. The user asked trimme-44 the same day for the owner's bar («الخدمات» instead of «المواعيد», D-159). The two sessions agreed that the owner's list is D-159's and the barbers' «التقويم» is D-158's.
- **The four choices are links** (`?mode=walkIn|phone|break|unavailable`), so Back returns to the previous choice and each screen has an address. Before a choice: four equal buttons and «اختر واحداً من الأربعة فوق». After one: the chosen one is filled, and the others stay to switch to.
- **«بريك» and «غير متوفر اليوم»** reuse the hours page's `BarberDayCard` (`section="breaks"` or `"unavailable"`), the barber's own day only. «غير متوفر اليوم» alone shows no hours and no day arrows: it is today only.
- **Today's walk-in hours start from now.** The barber works, say, 10:00–22:00. At 15:10, hours 10–14 are not listed, and 15 opens at :10. A later day lists the whole working day. The customer's booking keeps its own behaviour (past hours greyed).
- **Wording on the walk-in page:** an empty walk-in today says «لا أوقات في هذا اليوم», not the customer's «… جرّب يوماً آخر»: a walk-in cannot move to another day. «بريك» and «غير متوفر اليوم» are the page's title on their own screens; the others keep «حجز حضوري أو بالتلفون».
- **The step order stays.** The barber is preselected, so the times show as soon as the walk-in opens («أول ما يحاول يسجل زبون يطلع له الأوقات»).
- **Red for a no-show uses the cancelled tokens**, in the calendar only. Status badges elsewhere keep their colours.
- **The admin's owner card is on every tab**, under the shop's facts, for those with `Admin.Shops.ManageAccount` (as the accounts tab).
- **Terms in this browser:** a cookie flag (`trimme-terms=1`), not the account's: before the number is typed, the page cannot know who it is, and a lookup by number would tell anyone whether that number has an account. The page sends no acceptance on the flag's behalf.

## Not changed (to raise with the user)
- **Win-back opt-out:** customers can no longer turn «ندعوك للعودة» off in the app. Earlier opt-outs still hold. WhatsApp marketing usually needs a way to stop it; Meta's template may carry one. The client should confirm this is acceptable.
- **A shared phone:** the flag belongs to the browser, so another person signing up on the same phone also reads «سبق أن وافقت…» on sign-up. Their account records nothing, and profile completion asks them to tick the box.
- **«Manual» reset:** the server generates the temporary password (as for barbers, D-146). If the user meant typing a password of their own choosing, D-139's create form already has that pattern; it is a small follow-up.
- **The phone booking's date** stays a date field (no day strip like the customer's). Not asked.
- **«المواعيد»** is still reachable from the menu, and the notices still open bookings there.

## Checklist
- [x] API: `MeResponse.termsAccepted`; walk-in `times` and `open`; `GET /admin/shops/{shopId}/users`, `POST …/users/{userId}/temporary-password` (owner of that shop only; sessions revoked; lockout lifted; audited)
- [x] OpenAPI regenerated; `schema.d.ts` regenerated
- [x] Web: `WalkInChooser`; the walk-in page by `mode`; `WalkInFlow` with a fixed kind, steps ١–٤, `HourMinutePicker`; `BarberDayCard section`; `SimpleAppointments` without quick buttons; barbers' `calendar` tab; calendar NoShow red and its legend; `ShopOwnerCard` and the dialog's owner wording; `CompleteProfileForm termsAccepted`; `PhoneStep termsRemembered` and sign-in without an acceptance; `lib/auth/terms.ts`; the profile page without `WinBackSetting`; copy in ar, en, tr; the audit action's name
- [x] Integration tests: `ShopOwnerPasswordResetTests` (2), `PhoneSignInTests` (the flag; a remembered sign-in; a new number asked), `ShopDashboardTests` (times and open windows)
- [x] Web unit tests: chooser, calendar legend, `BarberDayCard` sections, `WalkInFlow` (four steps, greyed hours and minutes), `ShopOwnerCard`, `CompleteProfileForm`, `PhoneStep`, `DashboardShell`
- [x] E2E updated: `shop-dashboard`, `shop-schedule`, `phone-sign-in`, every sign-up's profile step; new `admin-owner-reset.spec`; the route audit's walk-in modes
- [x] Gates: see Evidence
- [ ] The client's phone after a deploy

## Evidence (Session 32, the worktree at `c79725a` plus D-158)
- **.NET:**
  - Release build: 0 warnings.
  - Unit 527/527; architecture 69/69.
  - **Integration 282/282** (full suite, 6 m 38 s). It includes `ShopOwnerPasswordResetTests` 2, `PhoneSignInTests`, `ShopDashboardTests`, `AuthorizationMatrixTests` and `OpenApiContractTests` (regenerated with `TRIMME_UPDATE_OPENAPI=1`).
- **Web:**
  - `vitest run`: 67 files, **615 passed** (after the sign-in and wording fixes).
  - `tsc --noEmit` clean; eslint 0 warnings; prettier clean; `openapi:generate` matches `v1.json`.
  - `next build` passes. Bundle budget passes on all 90 routes:
    - admin shop page 335.9/337 KB;
    - `/shop/walk-in` 293.0/330;
    - `/shop/calendar` 297.9/330;
    - `/account/profile` 314.4/330;
    - `/auth/complete-profile` 253.5/260.
  - E2E project: `tsc --noEmit` clean.
- **E2E** on `trimme-d158`, a fresh volume (`up -d --build`: migrate and seed), then the web image rebuilt after the sign-in fix. All at `--workers=1`:
  - **Codes on:**
    - `shop-dashboard` (the chooser, then the phone booking's last free hour and its last minute), `shop-schedule` («غير متوفر اليوم» from «حجز حضوري»), `auth`, `signed-in-gate`, `account-legal`, `qr`, `customer-booking`, `shop-barbers`: **23 passed**. `phone-sign-in` was skipped there: it needs codes off.
    - The new `admin-owner-reset` failed first on the «Create manually» tab's role, then **1/1**.
  - **Codes off** (API restarted with `TRIMME_OTP_ENABLED=false`): `phone-sign-in` (the remembered line on sign-up after sign-out, no box), `signed-in-gate`, `account-legal`: **5/5**.
  - **Every flow and smoke test**, after the two wording fixes, with the web image rebuilt and codes on: **91 passed, 1 skipped**. The skip is `phone-sign-in`, which needs codes off and passed in the codes-off run.
  - **Route audit** (`a11y` and `a11y-dark`, every role, ar and en, the walk-in's four modes added):
    - the first run had 21 passed and 4 failed;
    - one failure was a trace file deleted mid-run: a screenshot run shared `test-results`, which Playwright empties on start;
    - those four rerun alone: **8/8**.
- **Screens** (Chromium 390×844, dark, Majed then Omar at Barber House; admin):
  - the four choices with the prompt;
  - the walk-in (steps ١–٤);
  - «غير متوفر اليوم» alone;
  - the phone booking tomorrow: hours, then 12 minutes;
  - the calendar's legend with a red «ما حضر» and «التقويم» as the last tab;
  - the admin's «المالك» with two owners;
  - sign-up with «سبق أن وافقت…».
