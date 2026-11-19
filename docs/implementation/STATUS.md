# TRIMME — Status

- **Updated:** 2026-11-19 (Session 28, third request, slices 4–5: loyalty as a paid add-on with points per mobile number, «استخدم مكافأتي», «خلصت زيارتك», and the plans' monthly WhatsApp allowance (D-150). Before it, slices 1–3: the owner's dashboard — the shop's own sections, a barber's own length per service, languages and adding by mobile, the shop's own booking settings with a cleaning time, fixed shop type, service status wording, a 14-day horizon and the unavailable page for an expired shop (D-149); Before it, Session 28, second request: the customer's pages — category tabs and tiles, a package with other services, greyed times, Arabic-Indic digits and the Riyal sign, «جديد», «تجريبي», short references, cancelling until the start, full legal texts (D-148). Before it, Session 28: the owner watches, the barbers operate — «وصل» then «انتهت الخدمة», red bookings, walk-in and phone bookings with several services, a booking's services, postponement offers, the owner's reports and packages (D-147). Before it, Session 27: the owner adds and edits barbers, each with a login made at once; no walk-ins for the owner (D-146))
- **Current phase:** 17, the last phase (D-116), is complete and verified at 98/100. It delivers:
  - **Security:** a nonce CSP on every page, plus COOP, CORP and HSTS; uploads re-encoded from their pixels; the Data Protection key ring wrapped with a certificate; a test for every rate limit; ZAP, dependency audits, gitleaks and a phone-leak sweep all clean (`docs/security.md`).
  - **Observability:** OpenTelemetry traces and metrics (OTLP only when configured), health checks for Hangfire and the outbox, and log conventions (`docs/observability.md`).
  - **Localization, SEO and accessibility:** a route audit of all 81 pages in both locales as each role (axe 0 serious), a keyboard pass, a sitemap index, Open Graph images and validated JSON-LD (`docs/accessibility.md`).
  - **Performance:** N+1 and query budgets in tests, a batched availability probe, three indexes, bundle budgets, a k6 smoke and recorded Lighthouse runs (`docs/performance.md`).
  - **Visual review** of all 25 design screens.

  2 points are held back for the NVDA screen-reader pass, a manual checklist the user can run. The phase is committed locally and not pushed.
- **After Phase 17 (Session 14, at the user's request): dark mode (D-124).** Light / Dark / System (System by default) on every page, from the shell headers and the account page. The choice is saved in a cookie the server renders from, so there is no flash and no script; open tabs follow at once; printing stays light. The dark palette is checked by `tokens.test.ts`, `flows/theme.spec.ts`, and the `a11y-dark` route audit (81 routes, both locales, every role, axe 0 serious). See `docs/theming.md`.
- **After Phase 17 (Session 15, at the client's request): simpler customer booking (D-125).**
  - New copy: «ابحث عن صالون» and «استخدم موقعي».
  - Several services in one booking: one barber, back to back, durations and prices added. The API takes `serviceIds`; no migration.
  - Compact service tiles, and dates that start at today.
  - The time is picked as the hour first, then that hour's minutes.
  - The nearest free time is offered first, with one tap to book it.
  - Step circles (الصالون — الخدمة — الوقت — التأكيد) at every width.
  - Going back, with the app or the browser, keeps every choice.
  - Deployed to trimme.net.
- **Session 15, second request (D-126, D-127).**
  - The home page is cut to the search (with the location and the figures), the photo and the top-rated salons: 1,746 px tall on a phone instead of 5,485.
  - The admin shop page has a Services tab to add and edit a shop's services and pick its barbers for each.
  - Gap: admins still cannot set a shop's opening hours.
  - Then (D-130): every 5 minutes shown with unavailable ones greyed; «كل الخدمات»; services and packages managed by the admin only; a four-tab bottom bar for the shop on phones; the customer home is the logo with the location, then the salons.
  - Then (D-129): «صالون / مختص» wording for customers; the day's times under the days on one step; the month on date chips; no salon phone for customers; 15-minute slots on the demo.
  - Then (D-128): no salon starting prices or platform figures for customers; «احجز الآن» on the search and the shop page's centred bar; the FAQ and closing call to action are back on the home page.
- **Session 16 (D-131): the product is Groom Here.** New name in both locales and in the emails, the client's new logo, Open Graph image and favicon, and the demo now runs at https://groomhere.sa (www redirects to it; trimme.net is no longer served).
- **Session 16, second request (D-132):** Turkish as a third language (UI, emails; WhatsApp falls back to English); a loyalty card per salon (new `Loyalty` module and migration `LoyaltyProgramme`: the admin sets the visits and the reward, completed online visits with any specialist fill green circles, a full card becomes a coupon chosen when booking); «موقع سعودي ١٠٠٪» on the landing page, the customer home and the footer.
- **Session 18 (D-134), at the client's request:** the desk («موظف الاستقبال») no longer sees the shop's hours, barbers' hours, time off or closures (the API refuses them too). Its «البريكات» page shows each barber's day as 24 hours, where one tap makes an hour a break, with no reason. The demo's prayer and lunch breaks are gone, and break names are optional. A walk-in now asks for the customer's mobile (optional), stored encrypted for the platform team only (masked on the admin booking, revealed with a reason). The walk-in summary no longer hangs mid-screen on a phone. The desk's «المواعيد» is a plain list of today's and the coming bookings by day, a week at a time, without cancellations. Migration `WalkInContacts`. Then (D-135): «انتهت الخدمة» finishes a visit under way in one tap and frees the rest of its time at once; the owner (only) sees whether each customer is new or returning, and the last 30 days' figures on the overview. Then (D-136): «انتهت الخدمة» asks for a confirmation, the owner has the same simple appointments list, and groomhere.sa's online lead time is 15 minutes.
- **Session 17 (D-133), at the client's request:** from a barber's page or QR code the customer now picks the service, then the time, then confirms; the time chips that booked a service for them are gone, and the wizard keeps the barber and lists only their services. Loyalty: one card per salon was already the rule (now tested with two salons); the admin can make the first visit fill several circles (e.g. 3 of 6), then one per visit (migration `LoyaltyFirstVisitStamps`).
- **Session 19 (D-137), at the client's request:** the shop and admin dashboards show the language menu at every width, in the header's top-left corner in Arabic (compact on phones, where «مباشر» is the green dot alone); the walk-in no longer has a sentence under the customer's mobile; the salon owner (not the desk) adds and edits the shop's services again and picks the barbers who do each (packages stay the platform team's); breaks are set every 5 minutes: an hour opens its 5-minute slots, the picked ones become a break after a confirmation, and a message says what was saved. No migration.
- **Session 20 (D-138), at the client's request:** `/terms` and `/privacy` no longer show «مسودة — بانتظار المراجعة القانونية قبل الإطلاق.» (ar, en, tr). Meta's webhook subscription check (GET `/api/v1/webhooks/whatsapp`) now needs only the verify token, so the client can verify the callback before sending the app secret; every POST still needs the app secret and a valid signature. groomhere.sa has a generated verify token in its `.env`; the demo keeps the fake WhatsApp provider and the dev OTP inbox. No migration.
- **Session 21 (D-139), at the client's request:** besides the emailed invitation, the admin can create a shop owner or desk account by hand on the shop's Accounts tab («إنشاء يدوي»): name, email, role and a temporary password, with no email sent. That password starts no session: the staff sign-in asks for a new password first («اختر كلمة مرور جديدة») and then opens the dashboard. An emailed reset replaces it too. New endpoints `POST /admin/shops/{shopId}/users` and `POST /auth/password/change-temporary`; migration `TemporaryPasswords`. Not deployed yet. The email way needs a Resend account and a verified sending domain from the client (SMTP settings only, no code change).
- **Session 22 (D-140), at the user's request:** WhatsApp on the client's Meta account. The account is «Groom here Establishment» and the number is +966 54 637 9134. The token and app secret are in the VPS `.env` only.
  - **Templates:** the 18 booking templates are generated from the code (`docs/whatsapp/meta-templates.json`) and submitted with `tools/whatsapp/register-meta-templates.mjs`, in wording rewritten for Meta's rules (no variable at either end; enough words per variable).
  - **Sending:** body parameters are sent on one line and never empty. Graph API v27.0.
  - **Allowlist:** `WhatsApp__AllowedRecipients` limits real sends to the user's test numbers while the demo still has made-up numbers. A refused message raises no admin alert and does not mark a barber's number as failing.
  - **Sign-in codes:** they stay on the dev OTP inbox, because Meta allows the authentication template only after business verification.
  - **Deployed** to groomhere.sa (`206d0a1`, with D-139's migration), and the app is subscribed to the WhatsApp account.
  - **Live (2026-11-06):** all 18 templates are approved and the provider is Meta. The test sends to the user's numbers were Read and Delivered.
  - **For everyone:** the demo accounts were silenced first (5 demo barbers' messages off, the 3 demo customers disabled), then the allowlist was cleared.
  - **Admin texts:** the Templates page now holds Meta's approved wording (v2).
- **Session 23 (D-141), at the user's request:** the Meta Business domain verification tag (`<meta name="facebook-domain-verification">`) is in every page's `<head>` from the web setting `TRIMME_FACEBOOK_DOMAIN_VERIFICATION`. Deployed to groomhere.sa on 2026-11-07 (`6abf98a`, web only) and checked live. Meta's confirmation is the client's step; if the redirect from `/` to `/ar` gets in the way, use the DNS TXT method.
- **Session 24 (D-142), at the user's request:**
  - **Per-shop WhatsApp switches:** each admin shop page has a «رسائل واتساب» tab with the nine messages (5 customer, 4 barber), all on by default. A message turned off reaches no customer and no barber of that shop. Admin-only and audited; one additive migration (`ShopMessageSwitches`).
  - **365-day sign-in:** customers (signed in by a paid code), shop owners and desk staff now stay signed in for a year; the platform's admins stay at 7 days.
  - Pushed (`b6e02c5`) and **deployed to groomhere.sa on 2026-11-09**, after a database backup; smoke checks clean, WhatsApp still on Meta.
- **Session 25 (D-143), at the user's request:**
  - **No code by default:** customers sign up and sign in with their mobile number alone (`POST /auth/phone/sign-in`); the code endpoints answer 403 `otp.disabled`. The sign-up and sign-in pages ask the API (`GET /auth/sign-in-options`) and drop the code wording.
  - **Codes later:** `Identity__Otp__Enabled=true` plus a sender (WhatsApp once Meta approves the authentication template; SMS needs a new sender). Then a number alone is refused (`auth.otp_required`).
  - **The risk, accepted by the user:** anyone who knows a customer's number can sign in as them; booking messages go to whatever number was typed.
  - Pushed (`0bd8fcb`) and **deployed to groomhere.sa on 2026-11-13** after a database backup; no migration; smoke checks clean (codes off, WhatsApp still on Meta).
- **Session 26 (D-144), at the user's request** (screenshots of groomhere.sa):
  - **Booking:** a service's «احجز» opens the services step with it chosen (more can be added); the hour buttons no longer show a time count; rescheduling uses the booking screens (nearest time, days, hour then minutes, a review of the old and new times).
  - **Customer account:** personal info has an optional email and the number (masked) with «تغيير الرقم» and the warning that the new number becomes the sign-in number (only while codes are off); «حذف الحساب» erases the customer (refused while bookings are ahead); the «إشعارات واتساب» row is gone.
  - **Signed-in users** cannot open the landing or sign-in pages until they sign out (typed or with Back); a stale hint never locks anyone out.
  - **Admin:** «الشروط والخصوصية» edits both legal pages per language in rich text (TipTap, stored as an allowlisted document, never HTML); restore brings back the built-in text.
  - **Location:** a pasted Google Maps link (full or short) fills the coordinates and the address; short links are followed through Google hosts only.
  - **Directions** open Google Maps from the visitor's location; **QR files** are named `groomhere-qr-…`.
  - Migration 21 (`CustomerAccountAndLegalPages`, additive). Pushed and deployed with D-145 on 2026-11-14.
- **Session 26, second request (D-145), at the user's request:**
  - **Barbers sign in** with the WhatsApp number on their profile and a password (temporary at first); the owner («الحلاقون») or the admin turns logins on and off.
  - **A barber is their own desk:** their overview, appointments, day and week calendar, walk-ins, breaks and notifications, never a colleague's.
  - **The shop changes a booking for the customer:** a new time with the same barber, or «نقل الحجز إلى حلاق آخر» at the same time; the customer is told (new WhatsApp template, in-app) and may cancel or change it until it starts.
  - **Walk-ins with a mobile** get «تأكيد الحجز» (and changes, cancellation); both barbers hear about a move; the owner hears what barbers did.
  - **The desk role is removed** (migration `RemoveShopDesk` disables its accounts). Three migrations in all.
  - .NET, web and end-to-end gates pass (codes on and off). pushed (`fd512e0`) and **deployed to groomhere.sa on 2026-11-14** together with D-144, after a database backup (`/root/backups/trimme-before-d144-d145-20261114-145938.sql.gz`); 24 migrations there now. The 2 demo desk accounts were disabled; owners untouched; home, team sign-in and shop pages 200; codes still off. `trimme_customer_booking_professional_changed` (ar, en) submitted to Meta the same day: **PENDING** review.
- **Session 27 (D-146), at the user's request:**
  - **No walk-ins for the owner:** the page, the menu entry and the tab are gone for the owner and the API refuses them (403); the barbers book their own. The owner's bottom bar shows «الحلاقون» in their place.
  - **The owner adds and edits barbers** («الحلاقون» → «إضافة حلاق», and each barber's page): profile, photo, WhatsApp number (masked; it is also the sign-in, so it can be changed but not removed), the services they do, the login, and whether they take bookings. No delete, no change of shop, no reveal. This overrides spec §7/§8 for the owner (recorded in D-146).
  - **A login is made with every barber the owner adds:** the server generates the temporary password (12 easy-to-read characters), shows it once with «نسخ», and the barber replaces it at the first sign-in. «تفعيل الدخول» and «كلمة مرور مؤقتة جديدة» (owner and admin) also generate it now.
  - No migration. .NET, web and end-to-end gates pass (details in `phases/after-17-d146-owner-barbers.md`). Pushed (`07c1151`) and **deployed to groomhere.sa on 2026-11-18** after a backup.
  - **groomhere.sa data cleanup (same day, the user's choices):** the 5 demo barbers deleted with all their history (every booking on the site: 25; reviews and ratings); the 3 demo customers deleted; two customers kept (+966 53 017 8784 and «Sulaiman aa»); sign-in emails `admin@groomhere.sa`, `alasala@groomhere.sa`, `barberhouse@groomhere.sa`. The shops have no barbers until the owners add them.
- **Session 28 (D-147), at the user's request** (the client's document, sections 2-1 … 2-4):
  - **The owner watches, the barbers operate.** The owner keeps the overview, appointments and calendar (read-only), the new reports, services and packages, barbers and settings; every booking action, the barbers' breaks and the pause are the barbers' (the API refuses the owner, 403). The owner's breaks are weekly only.
  - **A barber's visit:** «وصل» then «انتهت الخدمة» (actual start and end recorded); «ما حضر» from a quarter hour late; «إلغاء متأخر» in the last quarter hour; no cancellation once arrived. Any barber accepts or rejects the shop's pending bookings and closes its red ones (open 15 minutes past their end, shown 3 days, «انقفل متأخر» with who closed it). Every log line names who did it. A barber sees no figures or income.
  - **Walk-in and phone bookings:** «حضوري» or «بالتلفون», several services or a package, for themselves or a free colleague, steps numbered ١–٥; the customer's mobile reaches barbers (never the owner) while the booking is open — a reviewed exception to the phone rule, recorded in CLAUDE.md, MASTER_PLAN, TRACEABILITY and security.md.
  - **A booking's services:** add after «وصل», remove until «انتهت الخدمة».
  - **Postponement with consent:** the barber offers an online booking a new time, held for the customer (database triggers under a per-barber lock) until an hour before the original start; the customer accepts or declines on the booking page (in-app notice; no WhatsApp template yet).
  - **Reports and expected income** (`/shop/reports`): day, week, month; per barber and shop; loyalty by who closed the visit. **The owner's packages** pages.
  - Migration 25 `BarberOperations` (additive). .NET, web and E2E gates in `phases/after-17-d147-barber-operations.md`. Pushed and **deployed to groomhere.sa on 2026-11-18** with D-148.
- **Session 28, second request (D-148), at the user's request** (the client's document, sections 3 and 4):
  - **Catalog:** category tabs over tiles, two to a row, packages last, on the shop page (CSS-only radios drawn on the server) and the booking step (ARIA tabs). A tab counts its chosen items. A package with other services in one booking, showing what it saves; a service already in the package is refused with a message. «كل الخدمات» is gone.
  - **Numbers:** Arabic-Indic digits for every Arabic quantity (formatters and ICU messages); the Riyal sign U+20C1 from a bundled one-glyph font; «—» instead of «·». English stays Latin with «SAR».
  - **Times:** taken hours greyed and struck through in place; «مغلق» and «مكتمل» days; «منتصف الليل» and «… فجر الجمعة».
  - **Other items:**
    - the draft survives leaving the page (a first-party cookie);
    - the no-specialist message names the service;
    - «جديد» below 3 ratings;
    - «بانتظار تأكيد المحل»;
    - WhatsApp share;
    - the unavailable-shop page;
    - «رائج في الرياض»;
    - «تجريبي» marks and the admin switch;
    - references «GH-xxxxx»;
    - cancelling or changing until the start (platform window 0);
    - full terms and privacy in ar, en and tr (a lawyer must review them).
  - Migration 26 `CustomerPages` (adds `is_demo`, widens `reference`, sets the cancellation window to 0). Gates are in `phases/after-17-d148-customer-pages.md`. Pushed (`972fcee`) and **deployed to groomhere.sa on 2026-11-18** with D-147, after a database backup.
- **Session 28, third request (D-149), at the user's request** (the client's section 6 and five owner-dashboard changes). Slices 1–3 (D-149):
  - **Booking settings per shop:** manual or automatic confirmation, start interval, minimum notice, furthest day (14 by default, at most 90) and a cleaning time after each booking, set by the owner («إعدادات المحل» › «إعدادات الحجز») and the admin. The cleaning time is held by the engine and the database constraint.
  - **Barbers:** each barber's own length per service (slots, «أي مختص», walk-ins, reschedules); languages; the owner adds a barber by mobile first.
  - **Sections:** each shop's own, ordered by the owner; the shop page and booking tabs follow them; the search category filter is gone.
  - **Small items:** service status badge and button wording; the shop type fixed to a men's barbershop; «المحل غير متاح حالياً» for a shop its subscription hides.
  - Migration 27 `OwnerSettings` (sections from each shop's categories, every shop a barbershop, 14 days, bookings held until their end). Gates in `phases/after-17-d149-owner-settings-loyalty-plans.md`.
  - **Slices 1–5 pushed (`87307ec`) and deployed to groomhere.sa on 2026-11-19** after a database backup (28 migrations there now); the admin data steps (plans, add-on price) are not done yet.
  - **Slices 4–5 (D-150):**
    - **Loyalty as a paid add-on:** SuperAdmin prices it in versions; the admin switches it on until a date or off; earning and redeeming stop when it ends and points are kept.
    - **Points per mobile number per shop** (keyed hash only): the owner's rules (first visit, later visits, points for a reward, a free service or 5–1,000 SAR off, wipe or decay without visits); points only at «انتهت الخدمة» with the barber's name; «+٢» beyond a full card; «استخدم مكافأتي» at booking (one per booking, never with a package; held, used at completion, returned on a cancellation or no-show); walk-in redemption through the barber's lookup; «خلصت زيارتك» in the app.
    - **The plans' monthly WhatsApp allowance:** empty is unlimited, 0 is none, 500 a month; enforced before the provider with an atomic slot; the owner told once a month; the month's use on both subscription pages.
    - Migration 28 `LoyaltyAddOnAndPlanMessages` (tables and columns only). The 489/639 plans, hiding half-year/yearly, the add-on price and which shops get it are admin data at deploy time.
- **Platform progress:** 1798 / 1800 points (Phases 00–17). Phase 18 was removed at the user's request (D-116).

| Phase | Status | Points |
|---|---|---|
| 00 Discovery & plan | [x] | 100/100 |
| 01 Backend & infra foundation | [x] | 100/100 |
| 02 Web foundation | [x] | 100/100 |
| 03 Design-system components | [x] | 100/100 |
| 04 Identity & sessions | [x] | 100/100 |
| 05 Tenancy, privacy & audit | [x] | 100/100 |
| 06 Shops, locations & professionals | [x] | 100/100 |
| 07 Services & packages | [x] | 100/100 |
| 08 Subscriptions & settings | [x] | 100/100 |
| 09 Schedules & availability | [x] | 100/100 |
| 10 Booking core | [x] | 100/100 |
| 11 Public discovery | [x] | 100/100 |
| 12 Customer booking & account | [x] | 100/100 |
| 13 Shop dashboard | [x] | 100/100 |
| 14 Admin dashboard | [x] | 100/100 |
| 15 WhatsApp & notifications | [x] | 100/100 |
| 16 QR & attribution | [x] | 100/100 |
| 17 Hardening | [x] | 98/100 |

## Decisions resolved with the user (D-037)

| ID | Decision |
|---|---|
| D-004 | In-house command/query dispatcher; MediatR is not used |
| D-005 | Customers sign in passwordless with mobile + 6-digit OTP over WhatsApp (fake in dev). Staff sign in with email + password, with reset over email (Mailpit in dev) |
| D-006 | Per-shop `RequireManualConfirmation`, default off, so online bookings are auto-confirmed |
| D-007 | OpenStreetMap: MapLibre with OSM tiles and a Nominatim-compatible geocoder. Production needs a self-hosted or OSM-based host, because the public OSM services' usage policies forbid heavy traffic |

## Decisions made at the user's request
| ID | Decision |
|---|---|
| D-064 | Photos and other media are stored in PostgreSQL (`media.media_files`), not on disk (Session 4) |
| D-078 | Keep the default: shops without a subscription in force are hidden and take no online bookings; the warning-only setting stays available for a temporary rollout (Session 4) |
| D-081 | Custom durations and back-dated starts need a SuperAdmin override with an explicit total price and a reason, fully audited (Session 4) |

## Open decisions
None are blocking. Phase 09 recorded D-082…D-084, Phase 10 D-085…D-089, Phase 11 D-090…D-095, Phase 12 D-096…D-098, Phase 13 D-099…D-100 and Phase 14 D-101…D-107, Phase 15 D-108…D-113, Phase 16 D-114…D-115 and Phase 17 D-117…D-123 as their own design decisions (D-096 supersedes the inline part of D-033; D-100 refines D-034); D-012 and D-015 are now Accepted; D-017 (reviews) is completed by D-102. The product assumptions D-012…D-035 remain overridable defaults. Phase 08 accepted D-013/D-014 as settings (D-076, D-078); the user confirmed D-078 and decided D-081.

## Blockers
None.
