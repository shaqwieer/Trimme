# After Phase 17 — D-145: barber accounts replace the desk; the shop changes bookings for the customer; walk-in confirmations

**Status:** [~] (started Session 26, 2026-11-14) · requested by the user after D-144 · decision: D-145

## The request (user, Session 26)
- No desk account: every barber signs in and works as their own desk inside the shop.
- A barber sees **their own** overview, appointments, notifications, breaks and day/week calendar, and books walk-ins.
- A barber (and the owner) can **change the time** of a customer's appointment and **cancel** it; the customer is told.
- New: «نقل حجز إلى حلاق آخر معه في المحل». The customer is told and decides to keep, cancel or change it.
- A walk-in («حضوري») customer whose mobile was given gets the «تأكيد الحجز» WhatsApp (template exists).

## The user's answers (AskUserQuestion, Session 26)
1. Barbers sign in with **mobile + password**: the WhatsApp number already on their profile, and a temporary password replaced at the first sign-in.
2. **Admin and shop owner** can turn a barber's login on and off and set the temporary password.
3. **Remove the desk role; disable existing desk accounts** at the deploy.
4. A **new WhatsApp template** for the move («تم نقل حجزك…»), registered with Meta; until it is approved the customer gets the in-app notice only.

## Defaults stated to the user (not asked)
- A barber sees and changes only their own bookings, breaks, calendar and figures. Walk-ins are for themselves.
- A move keeps the time; the colleague must do the service(s) and be free then. Pending or Confirmed only.
- After a move the customer may cancel or change the booking even inside the cancellation window, until it starts.
- The owner keeps everything, including moving and rescheduling any barber's bookings.

## Design (advisor-reviewed)
- **Identity.** Role `ShopBarber` (managed, ShopUser). `identity.users.professional_id` (unique filtered, immutable, same shop as the professional). Claim `professional_id` in `StartAsync`, `RotateAsync`, `SignInAsync`, `HttpCurrentUser`, `MeResponse`. Sign-in identifier: email, or a mobile parsed with `PhoneNumber.TryParseMobile` → the Professionals module's own number hash (`ProfessionalWhatsApp` purpose) → a new port returning only the professional id → the account linked to it. No number is copied into identity (the customers' unique `phone_lookup_hash` stays theirs). Login does not depend on `NotificationsEnabled`. Unknown numbers cost a dummy hash and answer like a wrong password. `change-temporary` takes the identifier. `ValidateSessionAsync` signs a barber out when their professional is inactive or the login is disabled; disabling revokes sessions.
- **Scoping.** Explicit handler checks keyed on `ICurrentUser.ProfessionalId` (never a query filter: availability reads must still see colleagues). A barber matrix test lists every endpoint a barber can reach as *scoped* (a colleague's booking/professional → 404 or excluded) or *shop-wide by design*; hub groups too.
- **Operations.** Shared reschedule service (admin + shop). New shop endpoints for reschedule (options + command) and for changing the barber (candidates + command); new history kind keeping the previous professional; event `booking.professional_changed` with a nullable previous professional id. One domain predicate for the after-move cutoff exemption, used at every cutoff site.
- **Messages.** New customer event `ProfessionalChanged` (template `trimme_customer_booking_professional_changed`, ar/en), per-shop switch slot, in-app notice; old barber gets «cancelled», new barber «confirmed». Walk-in recipient through a new Jobs-only port (`IWalkInContactReader`), platform-default locale, manage button → the shop page; the number never enters the outbox or SignalR. Barber notifications are per user (`UserNotification`); `/shop/notifications` moves behind `Shop.Notifications.Manage` (owner).
- **Desk removal last.** Migration: disable ShopStaff holders, revoke their sessions, revoke pending ShopStaff invitations, delete the role. Tests switch desk usages to the owner; barber tests are added. Demo seed: barber logins for the demo barbers.

## Checklist
- [ ] 1. Plan file and D-145 skeleton committed
- [ ] 2. Identity: role, professional link, claim, number sign-in, enable/disable (admin + owner), temporary password by identifier, session checks — with tests
- [ ] 3. Scoping: bookings (list, detail, transitions, notes), calendar, overview, walk-in + options, barber-day, loyalty, hubs, notifications — barber matrix test
- [ ] 4. Operations: shared reschedule service; shop reschedule; change barber; history + event; cutoff exemption — with tests
- [ ] 5. Messages: `ProfessionalChanged` event/template/switch/notice; old/new barber; walk-in recipient port; per-user barber notifications — with tests
- [ ] 6. Web: sign-in identifier; barber dashboard (overview, appointments, calendar, walk-in, breaks, notifications); drawer actions (change time, move, cancel); owner «الحلاقون» page; admin professional login card; customer after-move actions — unit tests
- [ ] 7. Desk removal: migration, roles, forms, seed, tests, permissions-matrix doc, E2E (barber role in the route audit)
- [ ] 8. Verification: .NET (Release, unit, architecture, integration), web gates, bundle budget, E2E codes on and off, route audit; docs (D-145, TRACEABILITY, deviations, STATUS, handoff)

## Deploy notes (for the user, before deploying)
- Removing the desk role disables desk accounts at that deploy. Suggest two deploys: first barber logins (desk still works), enable each barber's login and check their numbers; then the desk removal.
- Registering the new template with Meta uses the client's token on the VPS: ask first.
- Barbers have no email, so no «نسيت كلمة المرور»: the owner or admin re-enables the login with a new temporary password.

## Evidence
(filled in as slices are verified)
