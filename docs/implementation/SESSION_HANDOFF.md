# TRIMME Session Handoff

- **Updated:** 2026-11-14 (Session 26, second request: barber accounts replace the desk (D-145); first request: eleven changes from the client's review (D-144); before it, Session 25: sign-up and sign-in without a code (D-143); Session 24: per-shop WhatsApp switches and a 365-day sign-in (D-142); after Phase 17)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git).
  - Sessions 21 and 22 are pushed (`206d0a1`) and deployed to groomhere.sa on 2026-11-05.
  - Session 24 (D-142) is pushed (`b6e02c5`) and deployed to groomhere.sa on 2026-11-09 (with its migration).
  - Session 25 (D-143) is pushed (`0bd8fcb`) and deployed to groomhere.sa on 2026-11-13 (no migration). Codes are off there.
  - **Session 26 (D-144) is committed locally only: not pushed, not deployed.** It has migration 21; take a database backup on the VPS before deploying.
  - **Session 26, second request (D-145) is committed locally only: not pushed, not deployed.** Three more migrations (`BarberLogins`, `BookingProfessionalChanges`, `RemoveShopDesk`); back up first. Deploy step one may stop at `8eec53c` (see below).
  - Session 23 (D-141) is pushed (`6abf98a`) and deployed to groomhere.sa on 2026-11-07, web image only. The VPS `.env` holds `TRIMME_FACEBOOK_DOMAIN_VERIFICATION`, and `compose.yml` maps it to web (backup `compose.yml.bak-d141`). The local web container was rebuilt with the token too.
- **Working tree:** clean after the commit.
- **Local stack: running, all in compose.** Ports: web 3300, API 8080, DB 5434, Mailpit 8325.
  - Session 26 rebuilt the api and web images from its tree and started a **fresh** stack (`down -v`, then `up -d`: migrate (21 migrations) and seed ran), so the seeded subscriptions are current again. The API was left with codes **on** (the local default) after the codes-off runs.
  - Session 25 rebuilt the api and web images from its tree (no migration). `infra/docker-compose.yml` now sets `Identity__Otp__Enabled: ${TRIMME_OTP_ENABLED:-true}`: codes stay on locally for the E2E flows, and `TRIMME_OTP_ENABLED=false` gives the deployed default (no code).
  - Session 24 rebuilt the api and web images from its tree, ran `migrate` (`ShopMessageSwitches` applied: 20 migrations) and `up -d`. The seed was not re-run.
  - Session 21 rebuilt the API and web images from its final tree, ran `migrate` (`TemporaryPasswords` applied: 19 migrations) and `up -d`. The seed was not re-run.
  - The volume is still several days old: Barber House's seeded subscription has lapsed there, so the public E2E flows and the anonymous route audit fail on it (`/shops/barber-house…: public page is noindex`). Run those on a fresh stack (see Session 17 below) or after `down -v`.
  - Start with `TRIMME_WEB_PORT=3300 TRIMME_MAILPIT_PORT=8325 TRIMME_SITE_URL=http://localhost:3300`.
- **Demo deployment:** https://groomhere.sa (VPS, demo mode; www redirects to the apex; trimme.net is no longer served). Redeploy from `main`: `git pull` in `/opt/trimme`, then build, migrate and `up -d` in `/opt/trimme-deploy`. A changed `Caddyfile` needs `docker compose up -d --force-recreate caddy`.
- **Phases:** 17 is the last phase (D-116) and is complete at 98/100; 2 points wait on the NVDA pass. Dark mode (D-124) and this session's booking changes (D-125) were requested after the phases and are recorded as decisions, not phases.

## Session 26, second request: barber accounts replace the desk; the shop changes and moves bookings; walk-in confirmations (D-145)
- **The request** (from the user): no desk account, every barber is their own desk: they sign in and see their own appointments, notifications, breaks, day and week calendar and overview, and book walk-ins. A barber (and the owner) changes or cancels a customer's appointment and the customer is told. New: «نقل حجز إلى حلاق آخر معه في المحل»; the customer is told and decides. A walk-in («حضوري») gets «تأكيد الحجز».
- **The user's answers:** mobile + password (temporary first); the admin and the owner turn logins on and off; remove the desk and disable its accounts; a new WhatsApp template for the move.
- **What was built** (details in D-145 and `phases/after-17-d145-barber-accounts.md`):
  - **Identity:** role `ShopBarber` linked to one professional; sign-in by email or mobile; `/shop|admin/professionals/{id}/login`; invites and manual accounts are owners only; migration `BarberLogins`.
  - **Scoping:** a barber's bookings, board, walk-ins and breaks are their own (`BarberScope`, reviewed by `BarberScopeTests`); the shop inbox is the owner's.
  - **Operations:** shop reschedule and «نقل الحجز إلى حلاق آخر» (idempotent, versioned); the after-move customer window; migration `BookingProfessionalChanges`.
  - **Messages:** template `trimme_customer_booking_professional_changed` (ar/en) + switch + notice; both barbers told; walk-in confirmations; barber in-app notices; the owner hears barbers' actions.
  - **Web:** sign-in identifier; barber inbox and scoped screens; drawer dialogs; «الحلاقون»; admin login card; the customer's after-move policy line.
  - **Desk removal:** migration `RemoveShopDesk`; the admin's form makes owners only; demo seed owners + barbers.
- **To tell the client:**
  - two deploys are possible: first up to `8eec53c` (barber logins with the desk still working; the bundle fix `ef0754a` is web-only and can come with step two), turn on each barber's login and check their numbers; then the desk removal. Back up the VPS database first (three migrations);
  - each barber needs a WhatsApp number on their profile (the admin sets it) before their login can be turned on; a forgotten password = the owner turns the login on again with a new temporary password;
  - registering `trimme_customer_booking_professional_changed` with Meta uses the client's token: ask before doing it. Until it is approved, each change of barber fails that WhatsApp (no retry) and raises the admins' «تعذّر إرسال» notice; the customer still gets the in-app notice; the per-shop switch can turn it off meanwhile;
  - walk-in messages are paid WhatsApp messages, go to whatever number was typed, follow the shop's customer switches, and carry no reminder.
- **Evidence:**
  - .NET: Release build of `Trimme.slnx` 0 warnings; unit 475/475; architecture 69/69; integration 236/236 (new: `BarberLoginTests` 2, `BarberScopeTests` 2, `ShopBookingChangeTests` 3, `BarberMessagesTests` 2, `RemoveShopDeskTests` 1; the desk's uses moved to the owner or a real barber login); `has-pending-model-changes`: none; OpenAPI regenerated.
  - Web: `format:check`, `lint`, `typecheck`, `openapi:check` clean; `pnpm test` 522/522; `build` + `bundle:check`: 83 routes within budget (largest `/admin/shops/[shopId]` 328.7 KB) after importing the drawer dialogs statically (with `next/dynamic` most pages gained a duplicated 12 KB chunk, bisected to `1b8df9b`).
  - Local compose volume: rebuilt from this tree (`up -d --build`), migrations applied (24), desk counts before/after in the plan file; left running with codes **on**.
  - E2E: on the local stack rebuilt from this tree, with 2 workers (a first full run with the default workers was stopped by Claude Code for low system memory): smoke + flows 86 passed, 1 skipped (the codes-off spec, by design), 1 failed: the barber-breaks flow expected a barber picker, but a card with one barber names him instead (the app was right; the test now checks Sultan's name and no picker; the card's instruction now says «اختر اليوم» when there is one barber); rerun `shop-schedule` 2/2. Keyboard pass + route audit light and dark, with the new `barber` role and `/shop/barbers`: 25/25 (axe 0 serious). Codes off (API recreated with `TRIMME_OTP_ENABLED=false`, as on groomhere.sa): `phone-sign-in`, `shop-dashboard` (Omar by mobile), `services`, `shop-schedule` 8/8; codes back on afterwards.
- **Incident in this session (my error, repaired):** to build a baseline of the web app I created a git worktree whose `node_modules` were junctions to this checkout's; `git worktree remove --force` then deleted package files through the junctions (`apps/web/node_modules` and part of `node_modules/.pnpm`). Restored with `pnpm install --frozen-lockfile --force --offline` (777 resolved from the local store, nothing downloaded); typecheck, lint and the web tests pass again. No source or git data was affected.

## Session 26: eleven changes from the client's review (D-144)
- **The request** (from the user, with screenshots of groomhere.sa): «احجز» → the services step; no time count under the hours; rescheduling like booking; delete account; no «إشعارات واتساب» row; email and number in personal info with a sign-in warning; rich-text terms and privacy edited by the admin; a Google Maps link fills the shop location; QR files named Groom Here; signed-in users kept off the landing and sign-in pages; directions in Google Maps.
- **What was built** (details in D-144):
  - **Web only:** the per-service booking links (`step=service`), `HourMinutePicker` without counts, `RescheduleForm` rebuilt on the wizard's screens, the account row removed, `directionsUrl` → Google Maps.
  - **API + web:** `GET`/`PUT /me/profile` (`contact_email`; a number change only while codes are off), `DELETE /me` (erasure; 409 with bookings ahead), the `trimme-signed-in` hint cookie + proxy redirect + `SignedInGate`, `GET /{admin|shop}/geo/map-link` (`GoogleMapsLink`, `MapLinkResolver`), `administration.legal_documents` with `/public/legal/{kind}` and `/admin/legal/…`, `/admin/legal` (TipTap 3.32.2), `RichText`, `groomhere-qr-…` files.
  - **Migration 21** `CustomerAccountAndLegalPages` (additive).
- **To tell the client:**
  - a customer with an appointment ahead must cancel it (or wait until it is over) before deleting the account;
  - the number can be changed from personal info only while sign-in codes are off (today's setting); when codes come back it will need a code step;
  - the address fields of a pasted Google Maps link come from the server's geocoder: with the built-in test geocoder (`Geocoding__Provider` unset or `Fake`) only the coordinates fill for points outside the test gazetteer. Check the VPS setting before relying on it; short links need the VPS to reach `maps.app.goo.gl` (this machine cannot, so a real short link was not tried);
  - the terms page still says customers register with a WhatsApp code: the admin can now change it at «الشروط والخصوصية».
- **Evidence:**
  - .NET: Release build of `Trimme.slnx`, 0 warnings. Unit 470/470 (with `GoogleMapsLinkTests` 30); architecture 69/69; integration 225/225 (with `CustomerSelfServiceTests` 3, `LegalDocumentTests` 1, the map-link test, the cookie assertions in `SessionTests`, both matrices and the regenerated OpenAPI contract).
  - Web: `format:check`, `lint`, `typecheck`, `openapi:check` clean; `pnpm test` 509/509. E2E project typecheck and format clean.
  - E2E on the fresh local stack (3300, codes on): smoke + flows 86 passed, 1 skipped (the no-code spec, as designed), 1 failed: `qr.spec` «switched off» under parallel load, the known flake from Session 17; it passes alone (5/5 with `shop-schedule`). New: `signed-in-gate.spec.ts` 2/2, `account-legal.spec.ts` 2/2; changed: `customer-booking` E1, `public-discovery` (Google directions), `shops-professionals` (the map is lower).
  - Route audit (a11y + a11y-dark, with `/admin/legal` added): 21/21 (every role, both locales, light and dark; axe 0 serious). The first run flagged raw audit-action keys on «سجل النشاط» for the four new actions; their labels were added in ar, en and tr and the admin audits re-run 4/4.
  - Codes **off** (API recreated with `TRIMME_OTP_ENABLED=false`, as on groomhere.sa): `account-legal` (number change → sign-in with the new number, same account), `signed-in-gate` (with a client-side logo click after sign-out) and `phone-sign-in`, 5/5. Codes back on: those plus `customer-booking` and `auth`, 15 passed, 1 skipped by design.
  - Bundle budget: `pnpm build` + `bundle:check` within budget on all 82 routes once the editor loads on demand (statically it made `/admin/legal` 393 KB > 330 KB); the largest route is `/admin/shops/[shopId]` at 328.3 KB.
  - Screenshots (local, Arabic RTL, 1280/1440 and 390 px) of the services step, hours, reschedule and its review, account, personal info, the admin legal editor and the location picker; in the session's scratchpad, not in the repository.

## Session 25: sign-up and sign-in without a code; codes off by default (D-143)
- **The request** (from the user): the system must work without a code when a customer registers. The user will say when to bring codes back (SMS or Meta's authentication template); until then, by default, codes must not be used. Then commit, push and deploy to 169.58.70.59 (groomhere.sa).
- **What was built:**
  - **Setting:** `Identity:Otp:Enabled` (`CustomerSignInOptions`), false by default in every environment, Development included (the VPS runs as Development).
  - **API:**
    - `POST /api/v1/auth/phone/sign-in` signs in or creates the customer by number, with the same protections as a code sign-in (protected number, disabled accounts refused, CSRF, `otp` rate limit);
    - `GET /api/v1/auth/sign-in-options` → `{ otpRequired }`;
    - while codes are off, `otp/request` and `otp/verify` answer 403 `otp.disabled`; while they are on, `phone/sign-in` answers 403 `auth.otp_required`.
  - **Accounts:** an account made without a code has `phone_number_confirmed = false`; its first code sign-in confirms it.
  - **Web:** the sign-up and sign-in pages read the options per request. Without codes, the number goes straight to profile completion (new customers) or the destination; the copy no longer mentions WhatsApp codes (DV-A30). New error texts in ar, en and tr.
  - **Tests:** `TrimmeApiFactory` turns codes on, so the existing code-flow tests are unchanged; `PhoneSignInTests` (4) turns them off; `PhoneStep.test.tsx` (4); E2E `flows/phone-sign-in.spec.ts` skips itself on a stack with codes on.
  - **Also fixed:** `EntitiesWithAShopId_AreTenantScoped` had failed since D-142 on `ShopMessageOptOut`, now in the reviewed allowlist with D-142's reason.
- **To tell the client (also in D-143):**
  - anyone who knows a customer's number can sign in as them (their bookings, reviews and loyalty coupons, for a year);
  - booking messages go to whatever number was typed, so a typo sends a paid WhatsApp message to a stranger;
  - the terms page still says customers register with a code over WhatsApp; that text is the client's to change.
- **To turn codes on:** WhatsApp, once the business is verified and `trimme_sign_in_code` is approved: `WhatsApp__Meta__AuthenticationTemplateName`, `Identity__Otp__Sender=WhatsApp`, `Identity__Otp__Enabled=true` in the VPS compose/.env, then `docker compose up -d api`. SMS needs a new `IOtpSender`.
- **Evidence:**
  - .NET: Release build of `Trimme.slnx`, 0 warnings. Unit 440/440; architecture 69/69; integration 220/220 in Release on the final code (with `PhoneSignInTests` 4/4, the authorization and rate-limit matrices, and the regenerated OpenAPI contract).
  - Web: `format:check`, `lint`, `typecheck`, `openapi:check` clean; `pnpm test` 495/495. E2E project typecheck and format clean.
  - Local stack (3300), api and web rebuilt from this tree:
    - with `TRIMME_OTP_ENABLED=false`: `sign-in-options` → `{"otpRequired":false}`; `flows/phone-sign-in.spec.ts` 1/1 (sign-up with terms required, profile, sign-out, sign-in again to the same account, `otp/request` 403 `otp.disabled`, axe clean); the sign-up and sign-in pages checked in Arabic RTL at 390 and 1440 px;
    - API recreated with codes on (the local default), web untouched: `{"otpRequired":true}`; `flows/auth.spec.ts` 6/6 through the code flow, and the no-code spec skipped itself. So the web follows the API setting with no rebuild.
- **Deployed to groomhere.sa (2026-11-13, `0bd8fcb`)** with `/root/deploy-d143.sh`: backup `/root/backups/trimme-before-d143-20261113-134310.sql.gz`; `git pull` from `b6e02c5`; api and web built; `migrate` (database up to date, 20 migrations, none new); `up -d`. The VPS compose and `.env` set no `Identity__Otp__Enabled`, so codes are off. Smoke from the VPS: api and web healthy; `sign-in-options` → `{"otpRequired":false}`; `phone/sign-in` with an invalid number 400 `validation.phone_invalid` (no account created); `otp/request` 403 `otp.disabled`; `/ar`, the sign-up, sign-in and staff sign-in pages 200; both customer pages serve the no-code text; WhatsApp provider still `Meta`; 0 API errors after the restart. A real sign-up was not made on the client's database.

## Session 24: per-shop WhatsApp message switches; customers signed in for a year (D-142)
- **The request** (from the user):
  - on every shop, the Groom Here admin gets a tab to turn on or off each of the nine Arabic utility messages (5 customer, 4 barber), all on by default;
  - a message turned off reaches nobody at that shop, neither the customer nor any of its barbers, and only the admin sets it;
  - a screenshot of the result;
  - sign-in should last 365 days, to send fewer paid sign-in codes.
- **What was built:**
  - **Storage:** `notifications.shop_message_opt_outs` holds one row per switch turned off (migration `ShopMessageSwitches`, additive).
  - **Enforcement:** the one check is in `DispatchFactory.CreateForBookingAsync`, so it covers lifecycle messages and reminders. It is read when the message is due.
  - **API:** `GET` and `PUT /api/v1/admin/shops/{shopId}/whatsapp-messages`, using `Admin.WhatsApp.View` and `Admin.WhatsApp.Templates.Edit` (no new permission), audited.
  - **Web:** a «رسائل واتساب» tab on the admin shop page (`ShopWhatsAppMessagesForm`), with texts in ar, en and tr.
  - **Sign-in:** `CustomerSessionLifetime` is 365 days (was 30), and the new `ShopUserSessionLifetime` is 365 days for owners and desk staff (was 7), at the user's answer. Platform admins stay at 7 days. Sessions started before a deploy keep their old end.
- **Screenshots** (local stack, shop «صالون التجربة»): all on, two off after a save, and phone width. They were taken in the session's scratchpad and are not in the repository.
- **To tell the client:**
  - a barber's «cancelled» goes out only if they were told about the booking, so with the barber's «confirmed» off it rarely goes out;
  - OperationsManager can also change the switches (it holds `Admin.WhatsApp.Templates.Edit`), and Support can only view them.
- **Deployed to groomhere.sa (2026-11-09, `b6e02c5`).** Backup `/root/backups/trimme-before-d142-20261109-105232.sql.gz`; `git pull` from `6abf98a`, build api and web, `migrate` (20 migrations; `ShopMessageSwitches` applied, `notifications.shop_message_opt_outs` empty, so every shop has all nine on), `up -d`. Smoke from the VPS: api and web healthy; `/ar` and the staff sign-in 200; the new admin endpoint 401 without a session; the webhook 403 without a token; the WhatsApp provider still `Meta`; 0 API errors after the restart; the tab is in the web build. Signing in as an admin there needs the client's password.
  - Run with `/root/deploy-d142.sh` on the VPS. Lesson: run such a script as a file, not through `ssh … 'bash -s' < script`. There, `docker compose exec` reads the rest of the script from stdin and bash stops silently after the first exec. In scripts, give every `exec` and `run` `</dev/null`.
- **Evidence:** see D-142 and below.
  - .NET: Release build of `Trimme.slnx`, 0 warnings. Unit tests 440/440.
  - Integration, full suite (Release): 215/215, with the new `ShopMessageSwitches_AMessageTurnedOff_ReachesNobodyAtThatShop_OnlyAdminsSetThem`, the migrations tests and the regenerated OpenAPI contract. `CustomerAndShopSessions_LastAYear_PlatformAdminSessionsAWeek` was added after that run and passed with `SessionTests` and `StaffAuthTests` (19/19, Debug).
  - Web: `format:check`, `lint`, `typecheck`, `openapi:check` clean; `pnpm test` 491/491; the web Docker image builds (`next build`).
  - Local stack (3300): the tab in Arabic RTL at 1440 and 390 px; two switches turned off through the UI, saved, still off after a reload («المفعّل 7 من 9 رسائل»), then restored to all on. A customer code sign-in's refresh cookie lasts 365.00 days.

## Session 22: WhatsApp on the client's Meta account (D-140)
- **The request** (from the user): connect the WhatsApp Business account for sign-in codes ("authentication") and booking messages ("utility"). Tests go to the user's numbers +20 155… and +966 53…, which are in the VPS `.env` allowlist and not in the repository.
- **Meta side (read through the Graph API):**
  - **Token:** a `SYSTEM_USER` token for the app "Groom Here" (`1438990374393920`) that never expires, with both WhatsApp scopes.
  - **Account access:** the WhatsApp account had not been assigned to the system user. The user assigned it, with full control, and added a card for billing.
  - **Account:** WABA `1798791391452264` «Groom here Establishment». Review approved; **business verification not done**.
  - **Number:** +966 54 637 9134, phone number id `1469285809598518`. Cloud API, `CONNECTED`, quality GREEN, 250 conversations a day.
  - **Webhook:** the app's webhook was already active. The app was **not** subscribed to the WhatsApp account; it is now (`POST /{waba}/subscribed_apps`, after the server had the app secret).
- **Templates (`docs/whatsapp/meta-templates.json`, submitted with `tools/whatsapp/register-meta-templates.mjs`):**
  - **First submission:** 2 accepted, 18 refused, which taught Meta's rules (recorded in D-140 and `docs/whatsapp-integration.md`):
    - 2388299: no variable at either end, and a trailing "." does not count;
    - 2388293: at least about 2.5 words per variable;
    - 2388185: the authentication template needs business verification.
  - **The fix:** the default wording was rewritten with the same placeholders in the same order, so the database's old versions still send the right parameters. The 16 refused utility templates were resubmitted.
  - **State at the end of the session:** all 18 utility templates are **APPROVED** (UTILITY). The authentication template `trimme_sign_in_code` cannot be created until the client verifies the business in Meta Business Settings → Security Center.
- **Code:**
  - `MetaTemplateCatalog` with its snapshot test, which also checks Meta's rules;
  - `MetaCloudApiProvider.ParameterText`, which sends parameters on one line and never empty;
  - the default Graph API version is `v27.0`;
  - `WhatsApp:AllowedRecipients` with `AllowlistedWhatsAppProvider`. A refused dispatch is Failed with `whatsapp.recipient_not_allowed`, raises no admin alert and does not mark a barber's number as failing.
- **The user's choices:**
  - go live with an allowlist first, because the demo's made-up numbers could be real people's;
  - deploy D-139 together with D-140.
- **groomhere.sa:**
  - **Before the deploy:** backup `/root/backups/trimme-before-d139-d140-20261105-202822.sql.gz`, and copies `compose.yml.bak-d140-20261105-202822` and `.env.bak-d140-20261105-202822`.
  - **compose.yml:** now passes `WhatsApp__Provider` (`${TRIMME_WHATSAPP_PROVIDER:-Fake}`), `WhatsApp__AllowedRecipients`, `WhatsApp__Meta__PhoneNumberId`, `…AccessToken`, `GraphApiVersion: v27.0`, `ButtonBaseUrl: https://groomhere.sa/` and `AuthenticationTemplateName: trimme_sign_in_code`.
  - **`.env`:** holds the app secret, the number id, the token, the allowlist (the user's two numbers) and `TRIMME_WHATSAPP_PROVIDER`.
  - **Deploy:** `git pull` to `206d0a1`, then build api and web, `migrate` (19 migrations; `TemporaryPasswords` applied) and `up -d`.
  - **Smoke from the VPS:** GET verify echoes the challenge (200); unsigned POST 401; signed POST 200; `/ar` and the staff sign-in 200; no errors in the API log.
- **Real sends are on (2026-11-06):**
  - **Switch:** `TRIMME_WHATSAPP_PROVIDER=Meta` in `/opt/trimme-deploy/.env` (backup `.env.bak-d140-meta-*`), then `docker compose up -d api`.
  - **Check first:** groomhere.sa's 18 active versions are the v1 originals, each with the approved template's variables in the same order.
  - **Test sends from Admin:** «booking confirmed» (ar) to the +20 number reached **Read**; «booking reminder» (en) to the +966 number reached **Delivered**. Both statuses came back through the signed webhook (`POST /api/v1/webhooks/whatsapp` 200).
  - **Turning it off:** set `TRIMME_WHATSAPP_PROVIDER=Fake` and `up -d api`.
- **Fully live (2026-11-06, the user's request; their choice to silence the demo accounts first):**
  - **Demo accounts:** the 5 demo barbers' WhatsApp is off (admin API, audited). The 3 demo customers are disabled (`disabled_at` set by SQL; there is no admin endpoint for customers). After that, 0 active customers have a phone and 0 barbers have messages on.
  - **Allowlist:** `TRIMME_WHATSAPP_ALLOWED_RECIPIENTS` is now empty (backup `.env.bak-d140-allowlist-*`), then `up -d api`; the API is healthy with the provider `Meta`.
  - **Admin template texts:** 16 slots activated as v2 with Meta's approved wording (the cancelled ones were already the same). All 18 active versions equal the approved text.
- **Still open:**
  - after business verification, run the registration script again to add `trimme_sign_in_code`, then set `Identity__Otp__Sender=WhatsApp`. The demo's dev OTP inbox then stops receiving codes.
- **Template texts:** aligned on groomhere.sa (above). Another environment keeps the old wording until each new default is saved as a draft and activated, because the synchronizer never overwrites a slot.
- **Evidence:**
  - .NET: Release build clean. Unit tests 440/440. Integration `Trimme.IntegrationTests.Notifications` 15/15, with the new `Allowlist_SendsOnlyToListedNumbers_AndASkippedMessageIsNoDeliveryFailure`.
  - Local API with the Meta provider and the allowlist: a test send to a demo barber's number was refused before Meta (`whatsapp.recipient_not_allowed`, masked `+966 5•• ••• •01`).

## Session 21: shop accounts created by hand with a temporary password (D-139)
- **The client's answer** (relayed by the user): asked whether owners and staff should be added by hand in the platform or invited by email (changing the password at the first sign-in), the client wants **both**.
  - The email way already exists (invitation and forgot-password emails over SMTP, D-056). It only needs a provider: the user told the client it would be Resend.
  - **What to ask the client for Resend:** an account in the client's name; a sending domain, preferably a subdomain such as `mail.groomhere.sa`, in the Ireland (eu-west-1) region, with the DNS records Resend shows added (or DNS access given); an API key with "Sending access" only, limited to that domain; and the sender address and name.
  - Then, on the VPS, set `Email__Smtp__Host=smtp.resend.com`, `Email__Smtp__Port=465`, `Email__Smtp__Security=SslOnConnect`, `Email__Smtp__Username=resend`, `Email__Smtp__Password=<API key>` and `Email__Smtp__FromAddress=<sender>` in `compose.yml`/`.env`. Check that `Web__PublicBaseUrl` is `https://groomhere.sa`, since emailed links use it. No code change is needed. A set SMTP host switches from the dropping sender to SMTP even in the demo's Development mode.
- **Built: the manual way.**
  - **Admin:** on the shop's Accounts tab, «إضافة حساب للمحل» has the tabs «دعوة بالبريد» (as before, the default) and «إنشاء يدوي». The manual tab takes the name, email, owner or desk, and a temporary password («توليد كلمة مرور» makes `abcd-efgh-jkmn`). The password is shown once after «إنشاء الحساب».
  - **API:** `POST /api/v1/admin/shops/{shopId}/users` (`Admin.Shops.ManageAccount`, the invitation's rules via `NewStaffAccountRules`; audited `shop_user.created`, without the email or the password).
  - **Sign-in:** the temporary password starts **no session**. `staff/sign-in` answers 403 `auth.password_change_required` (only after the right password), and the sign-in card becomes «اختر كلمة مرور جديدة». `POST /api/v1/auth/password/change-temporary` (anonymous, `auth` rate limit, lockout counts) refuses the same password, saves the new one and signs in. An emailed reset also clears the flag.
  - **Migration:** `TemporaryPasswords` adds `identity.users.must_change_password` (default false).
- **Scope kept:** shop owners and desk accounts, by admins only. Owners still cannot add their own staff, and platform admins are still invited by email (D-106).
- **Follow-ups to offer the client:**
  - an admin action to set a new temporary password for an existing shop account (a forgotten password otherwise needs the email reset, so Resend);
  - owners adding their own staff.
- **Evidence (final tree):**
  - .NET: unit 430/430, architecture 69/69. Integration **213/213** in two runs:
    - 102: Identity, Tenancy, Api and Persistence;
    - 111: every other namespace.
  - New integration tests: `ShopUserTemporaryPasswordTests` (3). `AuthorizationMatrixTests` lists the new anonymous endpoint and its rate limit. `OpenApiContractTests` was regenerated (`TRIMME_UPDATE_OPENAPI=1`), and `schema.d.ts` too.
  - Web: `lint`, `typecheck`, `format:check`, `openapi:check` and `build` pass. `pnpm test`: 53 files, 490 tests. New: `StaffForms.test.tsx` and `ShopForms.test.tsx`.
  - E2E on the local stack (rebuilt images): `tenancy` and `auth` flows 9/9. The new tenancy test has the admin create a desk account in the UI. The temporary password leads to the new-password step with `/me` 401, then `/shop`, and the temporary password is then refused. Axe passes on both pages.
  - The existing invite test now matches the email field exactly: the tab panel «Email invitation» also matched `getByLabel('Email')`.
  - `smoke` and the `a11y`/`a11y-dark` route audits: 49/53. The 4 failures are the anonymous pages, the same known ones: Barber House is noindex on this old volume.
  - The route audit loads the Accounts tab on its email panel only, so a scripted Chromium check covered the rest at 360 and 390 px, in Arabic, light and dark. It covered the «إنشاء يدوي» panel after a create and the «اختر كلمة مرور جديدة» step.
    - It found the email in the success message running out of the box: a long address has no break points, and the page overflowed by 32 px at 360 px.
    - Fixed in a second commit: the email is now on its own LTR line that can wrap, under the sentence (`createdBody` no longer contains it).
    - After the fix: 0 px overflow everywhere. `tenancy` passes again, 3/3, on the rebuilt web image.
- **Deploying later (when asked):** this adds a migration, so take the pg_dump backup on the VPS first. Then `git pull`, `docker compose build api web`, `run --rm migrate` and `up -d`.

## Session 20: the legal pages without the draft notice; the WhatsApp webhook's verify token (D-138)
- **The client's points** (relayed by the user):
  1. Remove «مسودة — بانتظار المراجعة القانونية قبل الإطلاق.» from the legal pages. **Done** (ar, en, tr; `legal.draft` removed).
  2. The callback URL and verify token to enter in the Meta app. **Done:** `https://groomhere.sa/api/v1/webhooks/whatsapp`, with the token generated on the VPS (`TRIMME_WHATSAPP_VERIFY_TOKEN` in `/opt/trimme-deploy/.env`; never committed). Subscribe to `messages`.
  3. Where the policy pages are: `https://groomhere.sa/ar/privacy`, `/en/privacy`, `/ar/terms`, `/en/terms`.
  4. What the client must send to connect the Cloud API: the app secret, the phone number id, a permanent system-user token with `whatsapp_business_messaging`, the approved templates' names (the names and wording to submit were exported from the live active versions), and an approved authentication template's name.
- **Code:** the GET subscription check needs only `WhatsApp:Meta:VerifyToken` (it needed the app secret too, which only the client holds). The POST is unchanged: 404 without the app secret, 401 without a valid signature.
- **VPS config:** `compose.yml` passes `WhatsApp__Meta__VerifyToken: ${TRIMME_WHATSAPP_VERIFY_TOKEN}` and `WhatsApp__Meta__AppSecret: ${TRIMME_WHATSAPP_APP_SECRET:-}` to the API (the shared `x-api-environment` anchor). `TRIMME_WHATSAPP_APP_SECRET` is empty until the client sends it; then set it and `docker compose up -d api`. Backups: `compose.yml.bak-d138-20261027-074454`, `.env.bak-d138-20261027-074454`. `WhatsApp__Provider` and `Identity__Otp__Sender` are not set, so the demo keeps the fake provider and the dev OTP inbox.
- **Evidence:**
  - .NET: unit 430/430; integration `NotificationsTests` and `AuthorizationMatrixTests` 18/18 (with the new token-only case).
  - Web: `lint`, `typecheck`, `format:check`, `test`, `openapi:check` and `build` pass.
- **Deployed 2026-10-27 (`7849b46`):**
  - Backup first: `/root/backups/trimme-before-d138-20261027-074855.sql.gz` (95 KB).
  - `git pull` (from `eb044ec`), `docker compose build api web`, `migrate` ("Database is up to date (18 migrations applied in total)"), `up -d`; api and web healthy. The seed was not run.
  - Smoke from the VPS: the right token with `hub.challenge=12345` returns `12345` (200); a wrong token 403; a POST 404 (no app secret yet). `/ar|en|tr/privacy` and `/ar|en/terms` are 200, and none contains the notice sentence. Pages still contain «مسودة»/«Taslak» from other labels in the message bundle, such as the shop status «Draft».
- **Going live with WhatsApp later:** set the app secret, `WhatsApp__Meta__PhoneNumberId`, `WhatsApp__Meta__AccessToken`, `WhatsApp__Meta__AuthenticationTemplateName`, `WhatsApp__Meta__ButtonBaseUrl=https://groomhere.sa/`, then `WhatsApp__Provider=Meta` and `Identity__Otp__Sender=WhatsApp` (`docs/whatsapp-integration.md`). Test from Admin → WhatsApp → Templates first.

## Session 19: the language menu on a phone's dashboard; no sentence under the walk-in's mobile; the owner's services again; breaks every 5 minutes (D-137)
- **The client's points** (two screenshots of the owner's account on a phone, groomhere.sa, and a message relayed by the user):
  1. The language choice at the top left of the dashboard, done first so the client can check it the same day. **Done.**
  2. Remove the sentence under the walk-in's mobile. **Done.**
  3. Give the owner (not the desk) back adding and editing services; the platform team keeps it too. **Done.**
  4. Breaks: after choosing an hour, choose the time every 5 minutes, as in the booking, with a confirmation at each step that the time is now a break. **Done.**
- **The user's answers in the session:** deploy once all four are done (not the language alone), and, for the breaks, 5-minute chips with a confirm button.
- **Language menu:** in the shop and admin headers at every width, as the header's last control (top left in Arabic), compact below 768 px. The header's gaps are 8 px below 768 px. On a phone, «مباشر» and «جارٍ الاتصال…» are the coloured dot alone; «إعادة الاتصال…» keeps its words.
- **Walk-in:** the `phonePrivacy` sentence is removed (ar, en, tr). The walk-in grid is bounded (`grid-cols-1`), which fixes a 13 px overflow at 360 px.
- **The owner's services:**
  - Restored as «الخدمات» in the drawer and the sidebar, not in the bottom tab bar. `/shop/services` (list, switch, reorder, edit, archive, delete), `/shop/services/new` and `/shop/services/{id}`, all behind `Shop.Services.Manage`, which the owner has and the desk does not. Packages stay the admin's.
  - The form has the shop's barbers: a new service starts with every active one picked. The API's shop create and update take an optional `professionalIds`, limited to the shop's own barbers (on edit, null keeps them), and `ShopServiceResponse` has `professionalIds`. OpenAPI and `schema.d.ts` are regenerated.
  - D-130 had deleted `ShopCatalogLists.tsx`; its services part is back as `components/catalog/ShopServices.tsx` (`ShopServiceList`, `ShopServiceEditor`).
- **Breaks every 5 minutes:** `lib/shop/barberDay.ts` now works in 5-minute slots, and an hour shows a summary of its slots (partly free, partly a break with the minutes).
  - `BarberDayCard` opens the tapped hour's twelve slots under the hours. The barber picks free slots or «الساعة كاملة», then «تأكيد الاستراحة», then confirms in the dialog. A green message then says «تم حجز … استراحة لـ …».
  - Tapping a break asks to cancel that whole break, from now on.
  - «استراحة طوال اليوم» and «إلغاء كل الاستراحات» ask first too.
  - No API change: it already took 5-minute windows. The cap of 48 separate breaks a day has its own message.
- **Evidence (final tree):**
  - .NET: unit 430/430, architecture 69/69. Integration 210/210 in two runs on the final API code: 87 (Services, Api, Availability, Bookings, Discovery) and 123 (all other namespaces). New: `OwnerShopServicesTests`.
  - Web: `lint`, `typecheck`, `format:check`, `openapi:check`, `build` and `bundle:check` (81 routes) pass. `pnpm test`: 51 files, 485 tests, on the final tree. New tests cover the header's language menu, the owner's services editor and list, and the 5-minute breaks (logic and card).
  - E2E on the local stack (Session 19's API and web images): `services`, `shop-schedule` and `shop-dashboard` passed 7/7, twice, the second time on the final web image. They cover the owner adding a service with one barber from the sidebar, then editing, reordering and archiving it, and the desk having no link, no page and getting 403. They also cover the desk making 4:00–4:15 PM a break with the confirmation and message, the public times following, and cancelling it again. `smoke` passed in full. The `a11y` and `a11y-dark` route audits passed 49/53, with the three restored services pages for the owner. The 4 failures are the anonymous pages, where Barber House is "noindex" because its seeded subscription has lapsed on this volume. The change does not touch them.
  - A scripted Chromium check at 390 and 360 px (ar, en, tr; owner, desk, admin; light and dark) found no overflow anywhere. «النظرة التشغيلية» fits at 390 px. The breaks flow and the services pages have no overflow at 390 px.
  - Screenshots are in the session scratchpad (`shots-d137/`). They show the header and the open language menu in light and dark, and the breaks flow (picked chips, confirmation, saved message, the hour reading «استراحة ١٥ دقيقة») in light and dark. They also show the owner's services list and new-service form.
- **Deployed 2026-10-22 (`eb044ec`: D-137 and the fix that keeps the break panel's confirm button above the phone tab bar):**
  - Backup first: `/root/backups/trimme-before-d137-20261022-120155.sql.gz` on the VPS (a complete dump, 95 KB).
  - `git pull` (from `60bb98e`), `docker compose build api web`, `docker compose run --rm migrate` ("Database is up to date (18 migrations applied in total)"; reference data synchronized), `up -d`; api and web healthy. The seed was not run.
  - Checked from the VPS: `/ar`, `/api/v1/meta`, the Al Asala page and the staff sign-in answer 200; `/ar/shop/services` answers 307 to the sign-in (it was 404 before). The live API's OpenAPI document (read from inside the web container; Caddy does not expose it) has `professionalIds` on `CreateShopServiceRequest` and `ShopServiceResponse`.
  - Not checked signed in: the demo owner's password on groomhere.sa is not the local demo one (401), and no credential was looked up. The client's own check covers the signed-in pages.
- **CI:** run 39753014541 on `7c66103` (the deployed code plus the deploy notes) passed all four jobs: web, backend (integration included), gitleaks, and the Docker stack with Playwright smoke, flows and route audit. The run on `eb044ec` was cancelled by that newer push.
- **To tell the client:** packages are still added by the platform team. The owner's own service changes are not in the audit log; the platform team's are.

## Session 18: the desk's pages (D-134); «انتهت الخدمة» and new or returning customers (D-135); the client's answers and the deploy (D-136)
- **The client's points** (screenshots of the desk account on groomhere.sa):
  1. **The desk saw the shop's and barbers' hours, time off and closures** on «الدوام والبريكات» (read-only since Phase 09, but it looked editable). The client asked why: there was no strong reason (the desk could see who works when). The desk now sees only «البريكات», and `GET /shop/schedule` answers 403 to it.
  2. **Breaks with no fixed times or reasons:** each barber's day as 24 hour cells; one tap makes an hour a break and another frees it; «استراحة طوال اليوم» and «إلغاء كل الاستراحات». Booked hours, the owner's breaks and time off cannot be changed there. New `Shop.Breaks.Manage` and `GET/PUT /shop/schedule/barber-day`. Break names are optional, and the owner's form has no name field. The demo's prayer and lunch breaks are no longer seeded, and the seeder deletes them, so groomhere.sa loses them on the next deploy. The owner's page keeps everything and gets the same day view.
  3. **Walk-in:** the summary no longer hangs mid-screen on a phone (it follows the form; it stays beside it from `lg`). The desk asks for the customer's mobile, which is optional (the client changed it from required); the name is required. A given mobile is stored encrypted (`bookings.walk_in_contacts`, purpose `trimme.walk-in-phone`), never returned to the shop, and shown to admins masked on the booking page, with an audited reveal (`Admin.Customers.ViewContact`).
  4. **Appointments for the desk:** «حجوزات اليوم» and «المواعيد القادمة» by day, a week at a time, Pending, Confirmed and Arrived only. Tapping opens the drawer. The owner keeps the full board.
- **A desk login is not a barber.** ShopStaff is not linked to a professional, so the break page asks which barber. Per-barber logins would be a separate feature.
- **Evidence (D-134 and D-135 together, final tree):**
  - .NET: unit 430/430, architecture 69/69.
  - Integration: **209/209**, run in six batches of classes (the single full run had been stopped by Claude Code for low memory at 19/0). New: `Staff_SetABarbersBreaksForOneDay_ByTheHour_WithoutAReason`, `WalkIns_TakeTheCustomersMobile_ForThePlatformTeamOnly`, `FinishingEarly_FreesTheRestAtOnce_AndTheOwnerSeesNewAndReturningCustomers`. `ShopFacingContractTests`, `MigrationTests` and `OpenApiContractTests` (regenerated) pass.
  - Web: `lint`, `typecheck`, `format:check`, `openapi:check`, `build`, `bundle:check` pass; `pnpm test` 51 files, 473 tests (new: `barberDay.test.ts`, `BarberDayCard.test.tsx`, `SimpleAppointments.test.tsx` with «انتهت الخدمة», the optional walk-in mobile, the admin walk-in reveal, the drawer's «زبون عائد»).
  - E2E on a **fresh** stack (`-p trimme-verify`, ports 3310/8090/5444/8335, removed afterwards), `CI=1 --workers=4 --retries=0`, images built from the final tree: **102/102** (smoke, flows, a11y, a11y-dark). An earlier run on the D-134 tree was also 102/102.
  - Screenshots in the session scratchpad (`shots-d134/`): as the desk at 390 px, the break grid, the walk-in, the appointments list; the owner's schedule at 1440 px; the owner's KPI tiles («زبائن جدد · آخر 30 يوماً» 2, «زيارات العائدين: 2») and the board with «زبون جديد» / «زبون عائد» on the demo's completed visits.
- **Walk-in autofill:** the mobile field has `autocomplete="off"`, so the desk's browser should not offer earlier customers' numbers (best effort).
- **Deployed 2026-10-21 (`60bb98e`):**
  - Backup first: `/root/backups/trimme-before-d133-d136-20261021-091514.sql.gz` on the VPS (complete dump).
  - `git pull`, `docker compose build api web`, `docker compose run --rm migrate` (applied `LoyaltyFirstVisitStamps` and `WalkInContacts`; reference data and the managed roles synchronized), `up -d`; api and web healthy. Permissions are read per request, so desk users follow at once.
  - The demo seed was **not** re-run: it would re-create any demo row the client deleted, by fixed id. Instead, in one transaction: the four seeded prayer and lunch breaks were deleted by id (they were the only breaks), and `administration.platform_settings.min_lead_time_minutes` was set to 15 (`updated_at` stamped; not in the audit log).
  - Checked from the VPS: `/ar`, `/api/v1/meta`, the Al Asala page, the staff sign-in and the public shop API answer 200. At 12:20 Riyadh the first open time at Al Asala was 12:40 (15-minute lead time on the 5-minute grid). Curl from this Windows machine to groomhere.sa fails (exit 43), so check from the VPS.
- **Then, D-135 (same session, a second message from the client):**
  - «انتهت الخدمة»: a confirmed visit can be finished once it has started (state machine Confirmed → Completed), and a visit finished early ends at that minute. Completed bookings already freed their time (only Pending, Confirmed and Arrived hold it), but completing took two steps in the drawer. The desk's list has the button on each visit under way; the drawer's action reads «انتهت الخدمة».
  - The online **minimum lead time** (`MinLeadTimeMinutes`, 60 by default, admin settings) still stops people at home from booking time freed in the next hour. The walk-in page shows the barber free at once. Lowering it (for example to 10–15 minutes) is the client's call.
  - New or returning customers: `Shop.Insights.View` (owner only). `ShopBookingResponse.Visit` on the owner's board and drawer; the owner's overview tile with the last 30 days' new customers and returning visits. The desk gets null.
  - The walk-in mobile became optional (D-134 updated). No migration.
- **Then, D-136 (the client's answers):** online minimum lead time 15 minutes on groomhere.sa (a setting, set by SQL after the deploy's backup; the code default stays 60); «انتهت الخدمة» asks for a confirmation on the list and in the drawer; the owner has the same simple appointments list (with the new or returning badges); the full board is removed. Web 474 tests; E2E on a fresh stack with the final web image 102/102.
- **To ask the client:** will each barber get their own login (a separate feature)?
- **CI follow-up (after the push):** CI failed on `60bb98e` and `fbc176f`, on two causes, both fixed (`40c890f`, `1bf427a`, not pushed yet):
  - Web: `pnpm audit` found vite 8.3.1 (GHSA-587h-7wmj-h5pm, high), the implicit peer of vitest. `apps/web` now declares vite 8.3.4 itself (`docs/security.md`). The "1 high" pnpm still prints is the muted `braces` CVE; the audit exits 0.
  - Backend: 4 integration tests failed with `53300: sorry, too many clients already`. This is the same failure earlier sessions reran as a parallel-load flake, and it is not one: about 80 tests each create a database, and their idle connections stayed for 300 s. The test connection strings now close idle connections after 10 s (D-069 updated). A full local run went from 10 failures to 209/209, peaking at 68 of 400 connections. **If `53300` comes back, treat it as a bug.**
  - Locally, every other CI step passes (unit 430, architecture 69, NuGet audit, no pending model changes, web lint, typecheck, format, 474 tests, OpenAPI drift, build, bundle budgets). The stack job has not run on CI since `8ac7d9a`; the local fresh-stack E2E above was 102/102.

## Session 17: service before time from a barber's page or code; first-visit points (D-133)
- **The client's three points:**
  1. **Two salons, two cards?** Yes, already: a card belongs to the salon and the customer, and `/me/loyalty` lists one per salon with no set limit. Now tested with two salons.
  2. **After scanning a barber's code, the service was skipped.** The «أقرب الأوقات» chips (barber page and QR page) opened the wizard on the review with a service the system had picked. Also, the barber's own «احجز» link lost the barber until a service was chosen.
     - The chips are removed (DV-T24). The QR page's services each have «احجز».
     - The wizard keeps `pro` without a service and lists only that barber's services, under «الحجز مع …» with «تغيير المختص».
     - With a barber chosen, the services lead straight to the day and time. Back still reaches the specialist step.
     - Order now: scan → service → time → confirm.
  3. **First-visit points:** «نقاط أول زيارة» on the admin's «الولاء» tab (1 to visits − 1). A customer's first completed visit at the salon fills that many circles; every later visit fills one. Read as a programme rule from the client's example, not a manual grant to one customer: confirm with the client.
- **Backend:** `LoyaltyProgram.FirstVisitStamps`, a stamp row per circle (`stamps.number`, unique (`booking_id`, `number`)), migration `20261020205012_LoyaltyFirstVisitStamps` (additive; defaults 1; check constraints). The request field is optional (null = 1).
- **Evidence:**
  - .NET: unit 429/429, architecture 69/69. Integration: a full run under a parallel Docker build was 195/206; the 11 failing tests (`BookingTests`, `NotificationsTests`, `QueryCountTests`, `SessionTests`, `NotificationsHubTests`) then passed 41/41 alone. `LoyaltyTests` 3/3 (new: first-visit circles, two salons → two cards, validation), `MigrationTests` (applies from empty, no pending model changes) and `OpenApiContractTests` 4/4 without regeneration.
  - Web: `lint`, `typecheck`, `format:check`, `openapi:check`, `build`, `bundle:check` pass; `pnpm test` 48 files, 462 tests.
  - E2E on a **fresh** separate stack (`-p trimme-verify`, ports 3310/8090/5444/8335, the same images; removed afterwards), `--retries=0`: `qr` + `public-discovery` 11/11 (including the new barber path), `customer-booking` 5/5, `a11y` route audit + keyboard 13/13.
  - The same specs on the long-lived local volume failed 8/11, on pages this change does not touch, because Barber House's seeded subscription has lapsed there.
  - Screenshots at 390 px (ar): the barber QR page (no chips, «احجز» per service), the wizard (banner, Sultan's two services only), the day-and-time step straight after; the admin form with «نقاط أول زيارة» = 3; the salon card «اجمع 6 دوائر…» / «أول زيارة لك تملأ 3 دوائر…».
- **To deploy (not done):** pull, build, run `migrate` (applies `LoyaltyFirstVisitStamps`), `up -d`. Take a database backup first, as for D-132.

## Session 16, second request: Turkish, the loyalty card, «موقع سعودي ١٠٠٪» (D-132)
- **Turkish (`tr`)**: `/tr` routes, `messages/tr.json` (translated; `messages.test.ts` checks parity with ar/en), a three-language menu, Turkish emails; WhatsApp and the sign-in code use the English templates for Turkish speakers (no Turkish templates are approved).
- **Loyalty**: new module `src/Modules/Loyalty` (schema `loyalty`, migration `20261019200939_LoyaltyProgramme`, additive). Admin tab «الولاء» on the shop page (`Admin.Loyalty.Manage`); salon page card; `/account/loyalty`; the coupon picker on the wizard's review step; «كوبون» chips on the salon's appointments. Tests: `LoyaltyTests` (integration), `loyalty.test.tsx`, the wizard coupon test.
- **To do on a real deployment**: grant `Admin.Loyalty.Manage` to an existing OperationsManager role by hand if needed; set each salon's programme from its «الولاء» tab (none is set by the seed).
- **Open**: `tests/E2E/audit/routes.spec.ts` still audits ar and en only (Turkish not yet in the a11y route audit).
- **Dependencies**: Next.js and eslint-config-next 16.3.8 (GHSA-cjq9-62q9-8jv4, high). `pnpm audit` locally still lists `braces@3.0.3` (no fixed release exists; it was in the lockfile before); CI's audit step passed.
- **Verified**: web gates (459 unit tests), production build and bundle budgets; .NET unit (429), architecture (69) and loyalty/auth integration tests locally (a full local run hit 9 Postgres connection timeouts that passed on re-run); CI run 39322851392 on `7f99b05` green on all four jobs, including the Docker stack + Playwright smoke, flows and route audit.
- **Deployed**: `7f99b05` on groomhere.sa; migration `LoyaltyProgramme` applied; database backup taken first at `/root/backups/trimme-before-loyalty-20261019-225202.sql.gz` on the VPS. No salon has a loyalty programme yet (set it on the shop's «الولاء» tab).

## Session 16: Groom Here (D-131)
- The product name is **Groom Here** in both locales, the page metadata and the emails. The logo, Open Graph image and favicon come from the client's `public/brand/new-logo.jpeg`.
- Internal names (cookies, `TRIMME_*` variables, namespaces, the API title) are unchanged on purpose.
- The demo moved to groomhere.sa. On the VPS, `compose.yml` (CORS, `Web__PublicBaseUrl`, `TRIMME_SITE_URL`, mail sender) and the `Caddyfile` were changed; backups sit next to them as `*.bak-trimme-net`.
- Pinned `System.Security.Cryptography.Pkcs` 10.0.13 and `Microsoft.AspNetCore.SignalR.Client` 10.0.13 after new advisories failed restore.
- Open: an Arabic spelling of the name, if the client wants one; the user guides in `docs/guides/` still say TRIMME.

## Session 15, second request: short home page (D-126) and admin services (D-127)
- **Home page:** only the hero (search, «استخدم موقعي», the figures, browse link), the photo (16:10 on phones) and the top-rated salons.
  - The trust strip, how it works, outcomes, shop-partner band, FAQ and final call to action were removed, with their catalogue entries.
  - The header anchors became Shops and Nearby.
  - Page height at 390 px went from 5,485 to 1,746 px.
  - Shop sign-up has no entry point on the home page now (`TRIMME_PARTNER_CONTACT_URL` is unused).
- **Admin services:**
  - New permission `Admin.ShopServices.Manage`.
  - New endpoints: `POST /admin/shops/{id}/services` and `PUT /admin/services/{id}`, both with `professionalIds` and both audited.
  - New Services tab on the admin shop page, the `/admin/services/new?shopId=` page, and an edit form on `/admin/services/{id}`.
  - Creating a shop lands on its Services tab.
  - An existing OperationsManager role needs the permission granted by hand.
- **Gap to raise:** admins cannot set a shop's opening hours.
- **Fifth request (D-130):**
  - The wizard shows every 5 minutes, with unavailable ones greyed; demo `SlotStepMinutes` is back to 5.
  - «كل الخدمات» on the services step.
  - The shop's catalogue pages are removed; the admin manages packages through the new endpoints and pages.
  - The shop dashboard has a bottom tab bar on phones, and the schedule label is «الدوام والبريكات».
  - `/discover` is the logo with the location, then the salons; sign-in lands there.
  - QR was checked and works on the demo.
- **Fourth request (D-129):**
  - Customer wording changed from «حلاق / محل» to «صالون / مختص» (dashboards unchanged).
  - The wizard has four steps: the day's times show under the days.
  - The month is shown on the date chips.
  - The salon phone is removed from the public API and all customer pages.
  - `SlotStepMinutes` = 15 on the demo database.
  - Open: there is no TRIMME support contact for customers yet.
- **Third request (D-128):**
  - No "from" price on the shop page bar (now a centred «احجز الآن») or on the shop cards.
  - The search button reads «احجز الآن».
  - The figures row is removed, with its `public/stats` request.
  - The FAQ and closing call to action are restored.
  - Home page height at 390 px: 2,753 px.
- **Also fixed:** a race in the `FilterSheet` unit test. It now waits for the count request that carries the new filter.

## Session 15: simpler customer booking (D-125)
- **What (the client's nine points):**
  1. The search placeholder is «ابحث عن صالون».
  2. The location button says «استخدم موقعي».
  3. Several services can be booked in one booking.
  4. Services are compact tiles.
  5. Dates start at today.
  6. The time is picked as the hour, then the minutes.
  7. The nearest free time is offered first.
  8. Step circles show progress.
  9. Going back keeps every choice.
- **Backend:**
  - `serviceIds` on the public `availability/dates|slots` and on `POST /bookings`. `IBookableOfferCatalog.FindServicesAsync` sums durations and prices and keeps the barbers assigned to every service.
  - A booking keeps `service_id` = the first service and lists every service in the JSON items, so there is no migration.
  - Reschedule and the admin candidates resolve the booked services (`FindBookedAsync`). A service that appears only in booking items is in use.
- **Web:**
  - `wizard.ts`: repeated `service`, `combineOffers`, `withItems`, `restoreSelection`, `offerKey`.
  - New components: `ServiceTile`, `HourMinutePicker`, `BookingProgress`.
  - Rebook links carry every service.
  - The last choices per shop are kept in module memory (Web Storage is banned).
- **Evidence:**
  - Web: `pnpm lint`, `typecheck`, `format:check`, `openapi:check`, `build` and `bundle:check` all pass. `pnpm test` passes 44 files and 434 tests.
    - One earlier run under parallel Docker load timed out the five-step wizard test at 5.2 s. It now has a 15 s timeout.
  - .NET:
    - Unit tests 429/429 and architecture tests 65/65.
    - Integration tests 198/200 on a run concurrent with a Docker rebuild. Both failures were Postgres `53300 too many clients` in the shared test container. Their classes then passed alone: `BookingTests` 11/11 (including the new multi-service test) and `NotificationsTests` 12/12.
  - E2E on compose with the rebuilt API and web, `--retries=0`:
    - `customer-booking` and `qr` passed 6/8. E1, E6 and QR passed.
    - E7 is the known volume issue (Sara's seeded visits are past the review window).
    - "favorites and profile" timed out under parallel load and passed 2/2 alone.
  - Visual check at 390 px: tiles, step circles, nearest-time card, dates from today, hours then minutes, review. Browser back twice kept the services, date and time.
  - axe on the services (checked tiles), date (nearest card), hours and minutes steps, in light and dark: 0 serious, contrast included. The route audit itself only visits the wizard's first step.
  - A real two-service booking through the local UI: haircut + beard via "Book this time". Majed was picked automatically (the only barber who does both), 10:00–10:45, 85 SAR, and the detail reads «قص وتصفيف + تحديد لحية». The booking was cancelled, and the rebook link carries both services.
- **CI follow-up (after the push):** the web job failed `pnpm audit --audit-level high` on a new advisory. CVE-2026-93687 (`braces` ≤ 3.0.3, published 2026-09-18, no fix released) sits in the lint-only `eslint-config-next` chain. It is not caused by this change, and any push would have failed. It is ignored by CVE in root `package.json` and recorded in `docs/security.md` with a removal condition. That failure had also skipped the Playwright job, so the follow-up push is the first full CI run of D-125.
- **Known limits:**
  - Booking statistics count services booked together under the first service.
  - The per-shop memory lasts across in-app navigation, not a reload.
  - Walk-ins stay single-service.

## Session 14: dark mode (D-124)
- **What:** Light / Dark / System (System by default) across every page and portal.
  - The theme button is in every shell header (public and auth pages, customer, shop and admin dashboards), and the customer account page has an Appearance setting.
  - The choice is saved in the `trimme-theme` cookie (one year) and rendered by the server: no flash, no inline script, no hydration mismatch, correct even without JavaScript.
  - System follows the OS live through CSS. Open tabs follow a change through a `BroadcastChannel`. Printing stays light.
- **How:** one dark palette under `@custom-variant dark` in `tokens.css`, with the same token names, so utilities re-theme.
  - `navy-900` is ink and inverts.
  - New `chrome` tokens keep brand navy panels navy: the sidebar and drawer, toasts, hero, subscription card, poster, photo badges and map pins.
  - Shadow colours are variables. MapLibre controls are restyled; tiles, photos and QR codes are untouched.
  - Rules for new UI: `docs/theming.md`.
- **Fixes found on the way:** `bg-warning-100` (not a token) on the paused chip is now `warning-50` (DV-T15). The header selector first used Radix's dropdown, which pushed public pages to 228.7 KB (budget 220). It is now a native radio disclosure (216.6 KB). A focus bug in that disclosure, where a mouse choice was lost, was caught in the browser and is now covered by a unit test.
- **Evidence:**
  - Web: `pnpm lint`, `typecheck`, `format:check`, `test` (44 files, 423 tests), `build` and `bundle:check` all pass.
  - `flows/theme.spec.ts`: 7 tests, 5 consecutive green runs.
  - `a11y-dark` route audit: 8/8, axe 0 serious or critical, colour contrast included, and every page confirmed dark.
  - Full E2E on compose (all four projects): 99/100. The one failure was `qr.spec`: the switched-off code still shows active after 5 s under parallel load; it passes 3/3 alone.
  - Without `a11y-dark`: 89/92. The failures were `qr.spec`, `customer-booking` E7 (Sara's seeded visits are past the 7-day review window on this volume) and the retired-QR page reporting no `lang`/`dir` in the light audit.
  - **The pre-change baseline (`f864343`) on the same volume fails the same three tests** (82/85), so they are pre-existing volume and load issues, not regressions. A run on a bare `next start` failed only where the harness differs (dev routes off, no hub proxy).
- **Visual pass in dark** at 1280 and 390, with data loaded: the shop board, appointment drawer, cancel dialog, day and week calendars, the gallery's dropdown, toasts, tooltip, dialog, sheet and confirm, the search filter sheet, the booking wizard (selected slot, disabled Next), dashboards, tables, forms, charts, the QR pages and poster, account and auth.
- **CI follow-up (after the push):** CI failed the route audit on `/en/shop/*` (5 px overflow at 390, light and dark) and flaked on `/ar/q/bhxx8dfg`.
  - The overflow was the dashboard header plus the theme button; reproduced in `playwright:v1.63.0-noble` (6 px, title squeezed to 0 px). The dashboard theme button now lives in the drawer below 768px (D-124).
  - The flake: every 404 is served as Next's bare `__next_error__` document, and the localized not-found page renders on the client. The audit now waits up to 10 s for the root layout's `lang` before checking.
  - Evidence: Linux Chromium route audit (light and dark), keyboard pass and theme flows **28/28**, `--retries=0`; the hub-blocked worst case is 0 px overflow with the title back to 32 px. Web 424 tests. Compose full suite 98/100; the two failures are the known volume ones (E7, QR), which also fail on the baseline.
  - Worth a separate look: server-render the localized 404 (no-JS visitors and crawlers currently get an empty document).
- **Not inspected by hand:** the NVDA pass (still open from Phase 17), and Safari and Firefox. Chrome only.

## Completed in Session 13 (Phase 17)
Phase 17 (`phases/phase-17-hardening.md`, D-117 to D-123).

**Security** (`docs/security.md`):
- A per-request nonce CSP on every page (`proxy.ts`). Every page renders per request.
  - Files and 404s get a script-less policy; COOP/CORP `same-origin`; HSTS for an HTTPS site.
  - Every E2E test fails on a CSP violation. On its first run that guard found the API CSP blocking the Hangfire dashboard's inner pages, now fixed.
- Uploads are re-encoded from their pixels with SkiaSharp (D-119): no metadata or appended payload survives, the EXIF orientation is applied, and the longer side is at most 2,560 px. Checked on Linux through compose.
- The Data Protection key ring was stored unencrypted next to the numbers it protects. It is now wrapped with a mounted certificate, required outside Development and Testing, and certificate rotation is supported (D-120).
- Rate limits: a 429 test for each of the nine policies, and a matrix test that pins which endpoints carry which policy.
- Every JavaScript-handled form posts. ZAP had found credentials in the URL on a submit before hydration.
- Scans: `pnpm audit` and the NuGet audit clean (both now in CI); gitleaks clean; ZAP 0 FAIL.
- Phone-leak sweep: logs, spans, Hangfire, outbox and audit, all 0.

**Observability** (`docs/observability.md`, D-118):
- OpenTelemetry for ASP.NET Core, HttpClient, Npgsql/EF, runtime, Hangfire jobs (own filter) and outbox spans and metrics.
- OTLP export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. A sampler drops parentless polling spans.
- `/health/ready` reports `jobs` and `outbox`, Degraded at worst.

**Localization, SEO, accessibility** (D-122, `docs/accessibility.md`):
- `tests/E2E/audit` is the Playwright project `a11y`. The route audit covers 81 routes × 2 locales, each as the right role. Its first runs found:
  - raw permission and audit keys;
  - a hydration error on `/admin/qr`;
  - missing `lang` on parts in the other language;
  - MapLibre's English labels;
  - three inverted chevrons.

  All are fixed.
- The keyboard pass found dialogs dropping focus on close, now fixed in the shared overlays, and touch targets under 44 px, which now grow under `pointer: coarse`.
- SEO: sitemap index plus `/sitemaps/ar.xml` and `/sitemaps/en.xml`; a default Open Graph image; Open Graph on the legal pages; JSON-LD validated by the audit.
- Plural review counts.

**Performance** (`docs/performance.md`, D-123):
- `QueryCountTests` found an N+1 in discovery's availability probe. It is batched (`ProbeManyAsync`, six queries for any number of shops).
- Three indexes (migration `PerformanceIndexes`).
- EF query-shape warnings throw in Testing.
- Bundle budgets (`pnpm bundle:check`): form pages went from 412 to 320 KB gzip. The Zod import pulled every locale pack, and the validation module imported the whole catalogue.
- The k6 smoke passes.
- Lighthouse: a11y, best practices and SEO 100; LCP 3.5–3.8 s on simulated slow 4G (documented gap).
- Public cache headers (D-121).

**Visual review** of 25 screens against `design/reference`. Fixed:
- the landing search bar collapsed at 390 px (`sm` is 390 px, D-041);
- the booking back chevrons;
- the step bar on step 1;
- hidden map credits;
- two admin button styles;
- the suspended-shop badge colour;
- pagination overflowing a phone once a list reaches six pages (found by audit run 4).

The rest is recorded in DV-C11 and DV-T14.

**Also:**
- the SignalR hubs ignore a client abort during the handshake;
- the axe helpers wait for the page title (a soft-refresh race);
- CI runs the bundle budgets, the dependency audits and all Playwright projects.

## Verification evidence
| Command | Result |
|---|---|
| Phase 16 re-validation | `QrTests` 3/3, `qr.spec.ts` 3/3 |
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration | PASS: 429 / 65 / 199 |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web `format:check`, `lint`, `typecheck`, `test`, `build`, `openapi:check`, `bundle:check` | PASS: 385 tests; 79 routes within budget |
| Fresh `down -v` + `up --build`, `CI=1 --workers=2 --retries=0` | 85/85 ×2; 0 × 5xx, 0 × 429. After the hub fix: run 3 84/85 (axe `<title>` race, fixed); run 4 83/85 (pagination overflow at 390 px on a six-page list, fixed); run 5 **85/85** |
| Linux Chromium: route audit, keyboard pass, `public-discovery`, `customer-booking` | 26/26 (`customer-booking` rerun after its seeded numbers' hourly OTP limit, which runs 4 and 5 had used) |
| ZAP baseline; `pnpm audit`; NuGet audit; gitleaks `dir`/`git` | 0 FAIL; clean; clean; no leaks (`git`: 67 commits) |
| Phone-leak sweep | 0 everywhere (logs, 50,601 spans, Hangfire, outbox, audit) |
| k6 smoke; Lighthouse | Passed thresholds; recorded in `docs/performance.md` |

## Database and migrations
- Session 18: `20261021064349_WalkInContacts` (additive), applied on groomhere.sa (2026-10-21), on fresh verification volumes and from empty in the integration tests. Not yet on the local compose volume.
- Session 17: `20261020205012_LoyaltyFirstVisitStamps`, applied on groomhere.sa (2026-10-21), the local compose volume, fresh verification volumes and from empty in the integration tests.
- groomhere.sa data changes by SQL (2026-10-21, after the backup): the four seeded breaks deleted; online minimum lead time 15 minutes.
- Created and applied locally: `20261001082134_PerformanceIndexes` (index-only), on the compose volume and from empty in the integration tests.
- There is no production (non-demo) database yet; groomhere.sa is the demo.

## Decisions added
- D-117: nonce CSP.
- D-118: OpenTelemetry, health and logs.
- D-119: upload re-encoding.
- D-120: key ring encrypted at rest.
- D-121: caching.
- D-122: localization audit rules.
- D-123: performance budgets and the batched probe.
- Design deviations: DV-C11 and DV-T14 added; DV-S09 corrected to applied.

## Known issues or blockers
- **The NVDA pass is not run** (manual, `docs/accessibility.md` §3); 2 points are held back.
- **E2E on the long-lived volume:**
  - E7 needs a fresh volume (`docker compose down -v`), as its own message says.
  - `qr.spec`'s switch-off assertion and the retired-QR `lang`/`dir` check fail under full parallel load, on the baseline too. They are worth a look: the 5 s wait, and the 404 path for retired codes.
- **LCP on simulated slow 4G is 3.5–3.8 s** (target 2.5 s; next steps in `docs/performance.md` §4).
- **A contended booking race costs about 1 s** (deadlock path, D-089); a per-barber advisory lock is recommended (D-123).
- **Native date and time inputs** follow the browser's language (DV-T14).
- **The OTP limit when re-running E2E.** `customer-booking` signs in with three fixed seeded numbers, and each number gets 5 OTP codes per hour. More than about five suite runs in an hour against one stack give 429 on `/auth/otp/request`. That is the limit working, not a regression. CI uses a fresh stack per run.
- **Production setup is not in this repository** (D-116):
  - Nginx with `X-Forwarded-For` overwrite, `KnownProxies`/`KnownNetworks` and `client_max_body_size 6m`;
  - the certificate and the lookup key;
  - SMTP, WhatsApp and map hosts;
  - a shared rate-limit store before scaling out;
  - staff MFA.

  See `docs/security.md` §3 and §6.
- **Carried over:** a backplane for the hubs; a dead-letter replay screen; "any professional" does not retry; packages across professionals (D-020); grace days and limits (D-077).

## Exact next action
0. **D-145 is complete and committed locally.** Ask the user before pushing or deploying (two-step option at `8eec53c`, back up first) and before registering `trimme_customer_booking_professional_changed` with Meta (client token). The D-144 deploy question is still open.
0. Done: Sessions 17 and 18 pushed and deployed. Confirm with the client:
   - that «نقاط أول زيارة» (a rule for every customer's first visit) is what they meant;
   - per-barber logins (see Session 18).
1. Done: dark mode pushed to `origin/main`. Watch its CI run: `pnpm e2e` now also runs the `a11y-dark` project (8 more audit tests), so keep an eye on the 45-minute stack job.
2. Run the NVDA checklist (`docs/accessibility.md` §3) and record it in the Phase 17 file. That restores the remaining 2 points.
3. All phases are complete. Any further work, such as production deployment or the items above, needs a new plan agreed with the user.

## Files intentionally left modified
- None.
