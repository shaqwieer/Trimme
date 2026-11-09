# TRIMME — Status

- **Updated:** 2026-11-09 (Session 24: per-shop WhatsApp message switches for the admin, and customers signed in for 365 days (D-142); local only, not deployed)
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
  - **365-day customer sign-in:** customers (signed in by a paid code) now stay signed in for a year; staff stay at 7 days.
  - Committed locally, **not pushed or deployed**. Deploying needs a backup, then `git pull`, build api and web, `migrate` and `up -d`.
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
