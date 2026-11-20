# After Phase 17 — D-148: the customer's pages (the client's document, sections 3 and 4)

**Status:** [x] (Session 28, 2026-11-18 → 19) · requested by the user after D-147 · decision: D-148

## The request (user, Session 28: five screenshots, sections 3 and 4)
- **3. The customer's number.** It never reaches the shop for site bookings («رقم العميل غير متاح» stays). A booking the barber recorded (walk-in or phone) shows its number to the barber until «انتهت الخدمة», then it disappears from everyone in the shop (barber, owner, reports); the platform keeps it. — Already D-147's rule (barbers only, while the booking is open; never the owner or the reports; the admin keeps the masked number with a reveal). Verified, nothing to build.
- **4-1** The shop page and the booking's service step: category tabs (hair, beard, care…) with packages as the last tab; services as small tiles, two per row (name, price, duration); the tab shows how many of its services are chosen in a small circle, never «٠».
- **4-2** A package together with other services in one booking; the package shows what it saves («توفّر ١٥» against its services' prices); choosing a service already inside the chosen package shows a clear message instead of silently dropping the package; tabs instead of the «كل الخدمات» dropdown.
- **4-3** Booked times stay in their place, grey and struck through, not pressable (hours and minutes).
- **4-4** Arabic UI: Arabic-Indic digits everywhere (prices, durations, dates, ratings); English UI: Latin digits and «SAR»; the new Saudi Riyal sign instead of «ر.س»; no «·» beside Arabic digits anywhere («—», a comma or a new line instead).
- **4-5** «١٢:٠٠ ص» as a closing time reads «منتصف الليل»; hours past midnight read like «٤ م — ٢ فجر الجمعة».
- **4-6** The date strip: a closed day says «مغلق» under its number, a full day «مكتمل».
- **4-7** Choices survive going back a step or leaving the page, until the booking is made or abandoned.
- **4-8** «No single specialist» names the service that causes it.
- **4-9** «جديد» instead of stars until 3 ratings; «أقرب موعد» stays.
- **4-10** Share the shop on WhatsApp.
- **4-11** A wrong or unavailable shop link shows «المحل غير متاح حالياً» with a way to nearby shops, not a generic 404.
- **4-12** Demo shops and reviews must not look real: a «تجريبي» mark.
- **4-13** A short booking reference.
- **4-14** Past appointments stop saying «مؤكد».
- **4-15** «رائج بالقرب منك» only with a location; otherwise «رائج في الرياض».
- **4-16** Full terms and privacy texts (what is collected and why, who sees it, how long it is kept, deleting the account, contact), to be reviewed by a lawyer.
- **4-17** The customer cancels or changes any time before the start (Faisal's decision); the shop page's policy line follows.

## The user's answers (AskUserQuestion, Session 28)
1. **Riyal sign:** bundle a small web font holding only the official SAMA symbol (U+20C1), so it shows wherever a price is text. English keeps «SAR».
2. **Reference:** «GH-» and 5 characters from the unambiguous alphabet (about 28 million codes); old bookings keep theirs.
3. **Demo data:** an admin switch per shop that shows «تجريبي» on its card and page; nothing deleted.
4. **Past, unclosed appointments:** the customer sees «بانتظار تأكيد المحل» until a barber closes it; then «مكتمل», «ما حضر» or «ملغي» as recorded («مكتمل» opens the review).

## Defaults (decided, not asked)
- **4-17** is a platform setting (`CancellationCutoffMinutes`): its default becomes 0 and a data migration sets the existing row to 0 (the client's decision); the admin can still change it. With 0 the texts say «حتى يبدأ الموعد».
- **4-9** applies to shops as well as barbers (the same wrong picture).
- **4-7** keeps the draft in a short-lived first-party cookie (Web Storage stays banned, spec §9); it holds ids and the chosen time only, and is cleared when the booking is made.
  - **Narrowed by D-157 (2026-11-20, the client's next video):** only a link with no query at all picks the draft up, and then on the service step. Every «احجز» button (the shop page's «احجز الآن», a tile, the QR landing, favourites, a barber's link) starts a new booking and replaces it.
- **4-2** applies to walk-in and phone bookings too (the same offer rule).
- **4-4** Turkish keeps Latin digits and its own currency label.

## Split
- **A forked agent (API, database, integration tests, OpenAPI):** reference format, the cutoff default and data migration, a package with services in one offer (catalog, availability, booking, reschedule, walk-in), the date strip's closed flag, the slots' open windows (for greyed hours), the demo switch.
- **Me (web, copy, docs):** everything on the pages, the Riyal font, digits and separators, the legal texts.

## Checklist
- [x] API: reference «GH-xxxxx»; cutoff 0; package + services; `closed` days; open windows; demo switch — with tests; OpenAPI
- [x] Web: tabs and tiles (shop page: CSS-only radios drawn on the server; service step: ARIA tabs); package + services with savings and the inside-the-package message
- [x] Web: greyed hours and minutes (booking, customer reschedule, shop reschedule); «مغلق» / «مكتمل» days
- [x] Web: digits (formatters and ICU messages: the `arab` number format), the Riyal font, separators; midnight wording
- [x] Web: «جديد» under 3 ratings; «بانتظار تأكيد المحل»; share; the unavailable-shop page; «رائج» title; «تجريبي» badge and the admin switch
- [x] Web: the booking draft cookie; the named service in the no-specialist message
- [x] Copy: cancellation texts; full terms and privacy (ar, en, tr) — to be reviewed by a Saudi lawyer before launch
- [x] Verification: .NET, web, E2E; docs (D-148, TRACEABILITY, deviations, STATUS, handoff)

## Evidence
- **Riyal font provenance:** `apps/web/src/app/fonts/saudi-riyal.woff2` (508 bytes, one glyph at U+20C1, advance 746, bounds 60..686 × 0..700). It was built with fontTools from the Wikimedia Commons SVG of SAMA's published symbol (public domain; file SHA-1 starts `02b0fe79`). SAMA's own site reset the download connection.
- **.NET** (final tree):
  - Release build 0 warnings, 0 errors;
  - unit 480/480; architecture 69/69; integration **256/256** (new `CustomerPagesTests` 5/5);
  - `dotnet ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration";
  - `pnpm openapi:check`: types up to date.
- **Web:**
  - format, lint and typecheck clean;
  - unit **535/535** (60 files): the catalog tabs, package savings and in-package messages, the named service, the draft cookie, Arabic-Indic digits and the Riyal sign in every component test, `format.test.ts`;
  - build passes; `bundle:check`: 88 routes within budget. The public shop page needed the CSS-only tabs: the client tabs took it to 220.2 KB against 220, and without the tabs it measures 219.7 KB.
- **E2E** on the stack rebuilt from this tree (`up -d --build`: migrate applied `CustomerPages`, 26 migrations):
  - First run: 111 passed, 2 failed, 1 skipped. The failures were `shop_page_sections` (the old list) and the 44 px touch-target audit, which found two real bugs:
    - the tabs were 40 px tall;
    - next/font's generated "riyal Fallback" face (local Arial, no unicode-range, first in every stack) drew all the text in Arial and narrowed links.
    - Fixed: 44 px pills, `adjustFontFallback: false`, and radios inside the pills.
  - Second run after the web rebuild: **112 passed, 1 failed, 1 skipped by design.** The failure was `shop_page_sections` on its earlier spec. On the final spec (click the pill, «حتى يبدأ الموعد», «١:٠٠ فجر الجمعة») it **passed**; the spec is the only change since that run.
  - The route audit (axe, language, indexing, overflow; light and dark, every role, ar and en), the keyboard pass and the touch targets all pass.
- **Manual:**
  - `/ar/privacy` and `/en/terms` render 12 sections each;
  - a phone-width screenshot of the shop page's catalog shows the tabs wrapping onto two lines, packages last, and «توفّر ١٠ ⃁»;
  - the computed font stack has no Arial face.
- **Review follow-up** (advisor, after the first commit `b122179`):
  - the barber's walk-in and phone form now books a package with other services. D-148's default had claimed it while the form kept them exclusive; it now has a `board.test.tsx` test.
  - the privacy text was checked against the code and corrected: the location cookie, the coded IP on QR scans and sessions, and the location sent with visits;
  - «جديد» on the shop header at zero ratings;
  - no change needed: JSON-LD keeps raw numbers; the OG image is static; the calendar file is built by the API.
  - Then: web unit **536/536**; format, lint, typecheck clean; build and bundle check (88 routes within budget); after a web rebuild, E2E flows `public-discovery`, `shop-dashboard`, `customer-booking`, `account-legal` **17/17**; keyboard pass and touch targets 5/5; anonymous route audit 2/2.
