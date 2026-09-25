# 02 — Customer screens (Claude Design prototype → Next.js + ASP.NET Core)

> **Line-number note:** citations in this file refer to a 4658-line working copy of `TRIMME.dc.html`. The committed canonical copy (`design/source/TRIMME.dc.html`, 4656 lines, identical to the Claude Design project) is **2 lines shorter**: subtract 2 from any cited line number greater than 5.

Source: `design/source/TRIMME.dc.html` (single "dc" component).
Template scope: lines 819–2116 (`s_c_*` screens). Logic scope: 3430–3470 (state), 3740–4160 (customer data/handlers), plus helper definitions the customer screens depend on: `STATUS`/`bStyle`/`bDot` (3631–3647), `ratingBars` (3680–3683), `landingValues` (3719–3724), `shops` (3726–3738), design assumptions `assumptions` (3500–3509), `sitemap` (3523–3550), `journey` (3552–3560), `lifecycle` (3574–3581), `stateCards` (4612–4619), `respRules` (4592–4597).

Spec: `TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md` §2, 7, 8, 9, 10, 11, 12, 16, 17, 18.

Prompt-injection check: nothing in the scoped lines reads as instructions to an AI agent. The Arabic annotation panels (`mapRules`, `shopNotes`, `bookingRules`, `apptNotes`, disabled-slot copy, `assumptions`) are the designer's notes on product rules. This document records them as business rules.

---

## 0. Global conventions for reading this document

### 0.1 How the prototype works (so nobody copies its mechanics)
- Every customer screen is drawn inside a **390×800 phone frame** (`width:390px;height:800px;border:9px solid #10283D;border-radius:40px`) with a fake status bar (`٩:٤١ ▮▮▮`). The frame and status bar are presentation chrome. **Do not build them.**
- Several phone frames sit side by side on one "screen" of the prototype. Example: `s_c_auth` shows sign-up, OTP and location as three frames. Each frame corresponds to one real route or state below.
- The white annotation panels to the right of the frames (for example "قواعد الخريطة والفلاتر") are **designer documentation, not UI**. Their rules are captured in the relevant section.
- The only screen drawn at desktop width is `s_c_landing` (1280px browser frame, `trimme.sa` URL bar at 823). Every other customer screen is **mobile only (390px)**. The design says "كل شاشات العميل صُممت على ٣٩٠ أولاً، والنسخة المكتبية توسعة لا العكس" (4596), and `VP['c-landing']` = "مرجع ١٤٤٠ / ٧٦٨ / ٣٩٠ بكسل" (3428). **The desktop layout for every screen except the landing page is not designed.** Breakpoint rules (4593): ≥1200 full layout, 768–1199 drawer, <768 bottom nav + cards.
- The prototype sample data mixes number systems for the same field. Examples: review count `٣٤٦` (1154, 3752) vs `346` (3731); ratings are always Latin `4.8`; times are always Arabic-Indic `٥:٣٠ م`; prices are always Latin `85`. **The API must return raw numbers, ISO dates and UTC instants. The web app formats them per locale** (`Intl.NumberFormat`/`DateTimeFormat`, `ar-SA` with `nu-arab` or `latn` as a product decision, and `Asia/Riyadh`).
- Currency is always `ر.س` (SAR), with the price in Inter bold and the unit in Tajawal.

### 0.2 Wired vs static controls
In the prototype, most controls are plain `<span>`s with no handler. Only the controls marked **[wired]** below have handlers. Everything else is **[static]** and shows a single frozen state. Both kinds must become real controls in the rebuild; the marker only tells you how much of the behaviour the designer actually specified.

Wired handlers in customer scope:
- filter drawer open/close: `openFilter`/`closeFilter` (3771–3772)
- shop tabs: `shopTabs[].go` (3795)
- `goBooking` (3808)
- booking wizard: `bkServices[].pick`, `bkPros[].pick`, `bkDates[].pick`, `bkPeriods[].slots[].pick`, `bkNext`, `bkBack`, `bkReset`, `bkGoAppts` (3866–3992)
- appointment tabs (4009)
- cancel sheet: `openCancel`, `closeCancel`, `doCancel` (4051–4053)
- `toastResched` (4083)
- stars `setStar1..5` (3470–3473) and `submitRating` (4111)
- a global toast (template 3302–3305, `toast()` 3453, 2.6 s auto-dismiss)

### 0.3 Computed but unused values (dead code — ignore)
These are computed in `renderVals()` but no template binding uses them. The template uses `sc-if` variants instead:
- `bkProgress` (3978), `bkCtaStyle` (3980), `bkDoneStyle` (3993)
- `scrimStyle` and `drawerStyle` (3774–3783)
- `cancelScrim`, `cancelSheet` and `cancelBtnWrap` (4055–4061)
- `starBtns` (4086), `rateCta` and `rateCtaStyle` (4104–4110)
- `nav.explore` (3745) is never rendered

The transitions they describe are still good guidance for animation:
- sheet: `transform .32s cubic-bezier(.32,.72,0,1)`
- scrim: `opacity .25s`
- progress: `width .3s`

### 0.4 Customer bottom navigation (shared)
Defined by `navItems` (3740–3745). It has 5 items, in RTL order:

| # | Label | Icon | Proposed route |
|---|---|---|---|
| 0 | الرئيسية (Home) | home | `/[locale]/discover` |
| 1 | استكشاف (Explore) | search | `/[locale]/search` |
| 2 | مواعيدي (My appointments) | calendar | `/[locale]/account/bookings` |
| 3 | المفضلة (Favorites) | heart | `/[locale]/account/favorites` |
| 4 | حسابي (My account) | user | `/[locale]/account` |

- Active item color is `#10283D`; inactive is `#98A7B5`. Minimum height is 46px, with the icon at 21px and the label at 10.5px/700.
- It appears on: Home (1067, active 0), My appointments (1735, active 2), Favorites (1960, active 3), Notifications (1988 — uses `nav.home`, so Home is highlighted; Notifications is not a tab and is opened from the bell), and Profile (2031, active 4).
- It is hidden on search results, map, shop, professional, booking, details, cancel, reschedule, rate and QR (those screens have a back button or a sticky CTA instead).

### 0.5 Status vocabulary used by customer screens (`STATUS`, 3633–3639)

| Arabic label | Proposed enum | Badge bg / fg / dot |
|---|---|---|
| بانتظار التأكيد | `Pending` | #FDF3E2 / #8A5A12 / #D89A2E |
| مؤكد | `Confirmed` | #E8F1FB / #2C5C8C / #4A7FB5 |
| حضر العميل | `Arrived` | #EDF3FA / #3D5266 / #6D9BCB |
| مكتمل | `Completed` | #E6F4EC / #1F6B48 / #2E9E6B |
| ملغي | `Cancelled` (customer or shop — the design shows one label) | #FBEAEA / #9B2C2C / #C44545 |
| لم يحضر | `NoShow` | #F1F4F7 / #5E6E7E / #98A7B5 |

The lifecycle notes (3574–3581) define who changes each status:
- Pending: the customer books; the time is held temporarily.
- Confirmed: set by the shop, which sends WhatsApp and a reminder "قبل ٣ ساعات".
- Arrived: set by the shop; no message is sent.
- Completed: set by the shop "أو تلقائياً"; this opens a 7-day review window.
- Cancelled: set by the customer or the shop; the time is freed immediately and the other party is notified with the reason.
- NoShow: set by the shop after 15 minutes.

The spec's state machine (§11) matches, with separate customer-cancel and shop-cancel. The API should expose `status` plus `cancelledBy` (`Customer|Shop|Admin`).

### 0.6 Design-level business assumptions (`assumptions`, 3500–3508) — relevant to customer scope
1. "الحساب برقم الجوال + رمز OTP — لا كلمات مرور في النسخة الأولى؛ نفس الرقم المستخدم في رسائل واتساب." (**Conflicts with spec §9 — see §E.**)
2. "مدة الخدمة تحدد طول الفتحة — الشبكة تُحسب بخطوة ٥ دقائق ثم تُفلتر بما يكفي لاحتواء مدة الخدمة كاملة قبل أي حجز تالٍ." (Slot step = 5 min, consistent with spec §11.)
3. "الإلغاء متاح حتى ساعتين قبل الموعد — بعدها يتحول الزر إلى «طلب إلغاء» ويُخطر المحل بدل الإلغاء الفوري."
4. "التقييم يفتح بعد «مكتمل» فقط — نافذة ٧ أيام، تقييم واحد لكل موعد، ويُنسب للحلاق وللمحل معاً."
5. "الاشتراك يوقف الظهور لا البيانات — عند الانتهاء يختفي المحل من الاكتشاف وتبقى مواعيده القائمة سارية."
6. "رقم العميل مخفي عن المحل نهائياً."
7. "خدمة واحدة لكل موعد في v1 — الباقات تُنمذج كخدمة واحدة بمدة مجمّعة (مثل: شعر ولحية — ٥٠ دقيقة)." (**Conflicts with spec §10 — see §E.**)
8. "الحجز الحضوري يخصم من نفس الشبكة."

### 0.7 Proposed route map (summary)

| Screen / frame | Route | RSC / Client | Index | Auth |
|---|---|---|---|---|
| Landing | `/[locale]` | Server | **index** | public |
| Sign up (phone) | `/[locale]/auth/sign-up` | Client | noindex | guest only |
| Sign in (phone) — not drawn | `/[locale]/auth/sign-in` | Client | noindex | guest only |
| OTP verify | `/[locale]/auth/verify` | Client | noindex | guest with challenge |
| Location permission / manual | `/[locale]/onboarding/location` (also a sheet from the Home location header) | Client | noindex | public |
| Home (app) | `/[locale]/discover` | Server shell + Client islands (location-dependent) | noindex (personalized; the landing page is the SEO page) | public |
| Search results (list) | `/[locale]/search?q=&category=&sort=&openNow=&verified=&today=&minPrice=&maxPrice=&lat=&lng=&view=list` | Client (URL-driven) | noindex | public |
| Map view | `/[locale]/search?view=map&…` (same page, view toggle) | Client | noindex | public |
| Filter drawer | state of `/search` (URL params); not a route | Client | — | public |
| Shop page | `/[locale]/shops/[shopSlug]` (tab in `?tab=services\|professionals\|reviews\|about`; all tab content rendered on the server) | **Server** (tabs = small client island) | **index** (LocalBusiness + AggregateRating JSON-LD when backed by data) | public |
| Professional profile | `/[locale]/shops/[shopSlug]/professionals/[proSlug]` | **Server** | **index** | public |
| Booking wizard | `/[locale]/shops/[shopSlug]/book?step=service\|professional\|date\|time\|review&service=&pro=&date=&time=` | Client | noindex | public until confirm; customer required to confirm |
| Booking success | `/[locale]/account/bookings/[bookingId]?created=1` or `/[locale]/shops/[shopSlug]/book/done?booking=` | Client | noindex | customer |
| My appointments | `/[locale]/account/bookings?tab=upcoming\|past` | Server (auth, dynamic) + Client tabs | noindex | customer |
| Appointment details | `/[locale]/account/bookings/[bookingId]` | Server + Client actions | noindex | customer (owner) |
| Cancel confirmation | dialog/sheet on details (optionally an intercepting route `@modal/(.)cancel`) | Client | noindex | customer (owner) |
| Reschedule | `/[locale]/account/bookings/[bookingId]/reschedule` | Client | noindex | customer (owner) |
| Rate visit | `/[locale]/account/bookings/[bookingId]/review` | Client | noindex | customer (owner, Completed, within window) |
| Favorites | `/[locale]/account/favorites` | Server + Client toggles | noindex | customer |
| Notifications | `/[locale]/account/notifications` | Client (TanStack Query, SignalR optional) | noindex | customer |
| Profile & settings | `/[locale]/account` (+ `/account/personal`, `/account/privacy`, `/account/security` — last not designed) | Server + Client | noindex | customer |
| QR landing | `/[locale]/q/[qrCode]` → resolves the shop or professional, records the visit on the server, renders the "entered via QR" variant of the shop page or redirects to it with an attribution cookie | Server | **noindex**, canonical → shop/pro page | public |

Wizard state lives in search params so back, refresh and deep links work. The prototype keeps it in component state, and its back button walks steps (3990).

---

## A. Screens

### A1. `s_c_landing` — صفحة التسويق (Marketing landing page) — lines 819–913

**Layout (desktop only, 1280px frame; tablet/mobile described as "مرجع ١٤٤٠ / ٧٦٨ / ٣٩٠" but not drawn):**
1. **Top nav** (826–835): logo `trimme-logo.png` (h38) · links الرئيسية (active, bold) / المحلات (Shops) / للأعمال (For business) / عن تريمي (About) · **language switch `EN`** (832) · primary button **تسجيل الدخول** (Sign in, 833).
2. **Hero** (837–864), 2-column grid (1.05fr / 1fr):
   - Badge with pin icon: "متاح الآن في الرياض" (Available now in Riyadh).
   - H1 (44px): "احجز حلاقتك في دقيقة، بلا اتصال ولا انتظار" (Book your haircut in a minute, no calls, no waiting).
   - Paragraph (841): "تريمي يجمع أفضل صالونات الحلاقة في مدينتك، ويعرض لك الأوقات المتاحة فعلاً لدى الحلاق الذي تختاره — وتصلك التأكيدات على واتساب."
   - **Search bar** (842–847): query placeholder "خدمة أو اسم محل" (Service or shop name) · location segment "حي الملقا" with pin · button **ابحث** (Search).
   - **Stats row** (848–852): 128 "صالون شريك" (partner salons), 540 "حلاق محترف" (professional barbers), 4.8 "متوسط التقييم" (average rating).
   - Right column: 4:5 hero image placeholder ("hero: barber at work · warm light · 4:5"). A floating "booking confirmed" card overlaps it: green dot "تم تأكيد موعدك", "الخميس ١٨ سبتمبر · ٥:٣٠ م", "صالون الأصالة · فيصل القحطاني". This card is decorative, not live data.
3. **Value props** (866–876), auto-fit grid of 4 cards (`landingValues` 3719–3724): icon + title + description.
   - clock: "أوقات حقيقية فقط" (Real times only) — "كل وقت معروض مضمون الحجز — محسوب من دوام الحلاق ومدة خدمتك."
   - user: "اختر حلاقك" (Choose your barber) — "احجز مع الحلاق الذي تعرف يده، أو دع النظام يقترح الأقرب وقتاً."
   - msg: "تأكيد على واتساب" (WhatsApp confirmation) — "تأكيد فوري وتذكير قبل الموعد، بلا تطبيقات إضافية."
   - qr: "دخول عبر QR" (Entry via QR) — "امسح رمز المحل واحجز مباشرة دون بحث."
4. **Top rated** (878–903): H2 "الأعلى تقييماً في الرياض" (Top rated in Riyadh) + link "عرض الكل" (View all). Auto-fit grid of shop cards (`shops` 3730–3738).
5. **Partner CTA band** (905–911), navy: "عندك صالون؟ استقبل حجوزاتك بلا فوضى" + "لوحة تحكم بسيطة لإدارة المواعيد والدوام والإجازات، مع تأكيدات واتساب تلقائية لعملائك." + button **انضم كشريك** (Join as partner).
6. No footer is drawn. Legal and footer links are absent.

**Reusable components:** MarketingHeader (logo, nav links, LocaleSwitch, primary Button), HeroSearchBar (query + location + submit), StatBlock, ValuePropCard, ShopCard (vertical/grid variant with image, OpenStatusBadge overlay, VerifiedBadge, RatingInline, PriceFrom, DistanceChip), CTA band.

**Data fields:**

| Field | Sample | Type (API) |
|---|---|---|
| city label | الرياض | string (from configured operating city) |
| stats.partnerShops | 128 | int |
| stats.professionals | 540 | int |
| stats.averageRating | 4.8 | decimal(2,1) |
| shop.name | صالون الأصالة / باربر هاوس / لمسة الرجل / صالون الرواد | string (localized) |
| shop.area (district) | الملقا / حطين / النرجس / العليا | string (localized) |
| shop.rating | 4.8 / 4.7 / 4.6 / 4.9 | decimal |
| shop.reviewCount | 346 / 212 / 158 / 501 | int |
| shop.minPrice ("يبدأ من … ر.س") | 35 / 40 / 30 / 45 | decimal (SAR) |
| shop.distanceKm | 2.4 / 3.1 / 4.5 / 6.2 | decimal (null when no location) |
| shop.openStatus label (image overlay) | "مفتوح حتى ١١:٠٠ م", "مفتوح حتى ١٢:٠٠ ص", "يفتح ٢:٠٠ م", "مفتوح حتى ١١:٣٠ م" | derived from `isOpenNow` + `closesAt`/`nextOpensAt` (local time). Overlay is navy when open and grey when closed (3726–3729). |
| shop.isVerified | 1/1/0/1 | bool — shield shown in #2C5C8C, or rendered transparent when false (3737) |
| shop.coverImageUrl | placeholder (hatched) | string (URL) |

**Interactions (all [static]):** nav links, EN switch, sign in, search submit, "عرض الكل", shop card click → shop page, "انضم كشريك" → business lead/contact (no partner sign-up flow is designed; shops are admin-created per spec §9).

**Route:** `/[locale]` — Server Component, **indexable**. Needs localized metadata, hreflang, Organization/WebSite JSON-LD, and SearchAction optionally. Top-rated shops are server-fetched, revalidated (ISR ~5 min). The stats need real data or must be hidden (spec §6: no fabricated aggregates).

**API:**
- `GET /api/v1/public/stats?city=riyadh` → `{ partnerShops:int, professionals:int, averageRating:decimal }`
- `GET /api/v1/shops/search?city=riyadh&sort=rating&pageSize=4` → `ShopSummaryDto[]` (see A3)
- Search submit navigates to `/[locale]/search?q=…&area=…`.

**Auth:** public.

---

### A2. `s_c_auth` — التسجيل والدخول والموقع (Sign-up, sign-in & location) — lines 915–990

Three mobile frames (see §C for the full flow analysis).

#### A2.1 Frame "SIGN UP · OTP REQUEST" (918–941)
Layout, top to bottom:
- logo (h58, centered)
- H2 "أهلاً بك في تريمي" (Welcome to TRIMME)
- subtitle "أدخل رقم جوالك وسنرسل لك رمز تحقق عبر واتساب" (Enter your mobile number and we'll send a verification code via WhatsApp)
- label "رقم الجوال" (Mobile number)
- **phone input** with a fixed LTR country-code prefix `+966` and number `50 214 8830` (LTR, grouped 2-3-4), shown in the focus state (1.5px #6D9BCB + 3px ring)
- **terms checkbox** (checked): "أوافق على شروط الاستخدام و سياسة الخصوصية" (I agree to the Terms of Use and Privacy Policy), with links
- sticky-bottom primary button **إرسال الرمز** (Send code)
- "لديك حساب؟ **سجّل الدخول**" (Have an account? Sign in)

Fields: `phoneCountryCode` (+966, fixed/selectable?), `phoneNational` (9 digits; validation copy at 3225: "الرقم غير مكتمل — يجب أن يتكون من ٩ أرقام بعد المفتاح"), `termsAccepted` (bool, required).
No name, email or password field is present. The name input exists only in the component library (401–402, "الاسم الكامل") and the validation demo (3228–3229). The profile shows a name (2003), but the step that captures it is **not designed**.

#### A2.2 Frame "OTP VERIFICATION" (943–970)
Layout:
- back button (chevR, which points right = back in RTL)
- H2 "أدخل رمز التحقق" (Enter verification code)
- "أرسلنا رمزاً مكوناً من ٤ أرقام إلى واتساب +966 50 214 8830" (We sent a 4-digit code to WhatsApp …)
- **4 single-digit boxes**, LTR (`direction:ltr`). The sample state is 4, 1, 9 (focused), — (empty).
- countdown "إعادة الإرسال بعد 00:24" (Resend in 00:24) + disabled link "إعادة إرسال" (Resend) in grey #A9B6C4
- help callout (msg icon, green): "لم يصلك الرمز؟ تأكد أن واتساب مفعّل على نفس الرقم، أو اطلب الرمز عبر رسالة نصية." (Didn't get it? Make sure WhatsApp is active on this number, or request the code by SMS)
- bottom button **تحقق** (Verify) in the **disabled** state (#EEF2F7 / #A9B6C4) until 4 digits are entered

Error state (from the component library, 413–415): a red-bordered field with the message "الرمز غير صحيح — تبقى محاولتان" (Incorrect code — 2 attempts remain). This implies a limited attempt count (≈3) and lockout.

#### A2.3 Frame "LOCATION PERMISSION" (972–987)
Layout:
- full-height map preview placeholder ("map preview · Riyadh")
- bottom sheet:
  - pin icon tile
  - H2 "فعّل الموقع لعرض الأقرب إليك" (Enable location to show what's nearest)
  - "نستخدم موقعك لترتيب المحلات حسب المسافة وحساب وقت الوصول. يمكنك إيقافه في أي وقت من الإعدادات." (We use your location to sort shops by distance and calculate arrival time. You can turn it off anytime in settings.)
  - primary **السماح بالوصول للموقع** (Allow location access)
  - ghost **أدخل الحي يدوياً** (Enter district manually)

The manual district entry screen is **not designed**; the only hint is the Home header location switcher "حي الملقا، الرياض ▾" (1003). Arrival-time calculation is mentioned in the copy but never shown anywhere.

**Reusable components:** Logo, PhoneInput (country prefix + LTR national number), Checkbox with inline links, Button (primary / disabled / ghost), OTPInput (4 cells, LTR, auto-advance), ResendCountdown, InfoCallout (success tone), BottomSheet, MapPreview.

**Interactions:** all [static] in the prototype. The required behaviour follows the design copy:
- Send code is disabled until the phone is valid and the terms are accepted.
- Verify is disabled until 4 digits are entered.
- The resend countdown is shown before resend is enabled; the design shows 00:24 remaining.
- SMS fallback appears as text only; there is no button.
- Wrong code → inline error with the number of remaining attempts.
- Allow location → browser Geolocation API. On denial, fall back to manual district entry.

**Routes:**
- `/[locale]/auth/sign-up`, `/[locale]/auth/sign-in` (the same UI with a different heading; not drawn), `/[locale]/auth/verify` (challenge id in server session/cookie, not URL), `/[locale]/onboarding/location`.
- All are Client Components and noindex.
- `returnTo` param to resume the booking wizard after auth.

**API (as designed; see §C for spec reconciliation):**
- `POST /api/v1/auth/otp/request` `{ phoneE164, purpose: "SignUp"|"SignIn"|"Booking", channel: "WhatsApp"|"Sms", termsAccepted: bool, locale }` → `202 { challengeId, codeLength: 4, expiresAt, resendAvailableAt, channel }`. Rate-limited per phone and per IP.
- `POST /api/v1/auth/otp/resend` `{ challengeId, channel }` → same shape.
- `POST /api/v1/auth/otp/verify` `{ challengeId, code }` → `200 { user: { id, displayName|null, phoneMasked, roles[] }, isNewUser, profileComplete }`, and sets HttpOnly session and refresh cookies. Errors: `otp_invalid { attemptsRemaining }`, `otp_expired`, `otp_locked { retryAfter }`.
- `GET /api/v1/auth/me`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/sign-out`.
- `GET /api/v1/geo/areas?q=&city=` → `[{ id, name, city, centroid:{lat,lng} }]` (manual district).
- `GET /api/v1/geo/reverse?lat=&lng=` → `{ areaName, city }` (Home header label).
- `PUT /api/v1/me/location` `{ lat, lng, areaId?, source: "Gps"|"Manual" }` — optional; can also be kept client-side and passed as query params.

**Auth:** public or guest.

---

### A3. `s_c_home` — الرئيسية والبحث (Home & search) — lines 992–1121

#### A3.1 Frame "HOME · 390px" (995–1072)
Layout:
1. **Header** (998–1008):
   - "موقعك الحالي" (Your current location)
   - pin + "حي الملقا، الرياض" + chevron-down → location picker
   - **bell icon with red unread dot** (1005) → Notifications
   - search field "ابحث عن محل أو خدمة" (Search for a shop or service) → search
2. **Category chips** (1011–1016), horizontal: **الكل** (All, active navy), حلاقة (Haircut), لحية (Beard), أطفال (Kids). [static]
3. **"قريب منك" (Near you)** + link "الخريطة" (Map) (1018–1021). This is followed by a vertical list of horizontal shop cards (`homeShops` 3751–3755):
   - 84px image
   - name + shield (**always shown here, even for the unverified sample** — the template hardcodes the color at 1027; the landing uses `vStyle`)
   - star rating + (reviews) · area
   - open badge (`openShort` "مفتوح" or "يفتح ٢:٠٠ م"; green when open, grey when closed)
   - distance "2.4 كم"
   - "من 35 ر.س"
4. **"خدمات شائعة" (Popular services)** (1042–1051): 2-column tiles (`services` 3757–3760) with scissors icon, name, and "60 ر.س · ٣٠ دقيقة". **These tiles show a price and duration without a shop** — see §E.
5. **"حلاقون بتقييم عالٍ" (Highly rated barbers)** (1053–1063): 3 tiles (`pros` 3761–3763) with round avatar, first name, skill, and star rating.
6. Bottom nav (Home active).

#### A3.2 Frame "SEARCH RESULTS" (1074–1118)
Layout:
1. Header (1077–1088):
   - back button
   - search field filled with "تهذيب لحية" + clear ✕
   - chips: **"فلاتر · ٢"** (Filters · 2, active navy with filter icon — the count of active filters), "الأقرب" (Nearest), "مفتوح الآن" (Open now) [static]
2. Result count (1090): "**18** نتيجة ضمن ٥ كم" (18 results within 5 km).
3. Result cards (`results` 3764–3769):
   - top row: name, star rating · area · distance; open badge at the end (`مفتوح` / `مغلق`)
   - dashed divider
   - **matched-service line** "تهذيب لحية · 35 ر.س · ٢٠ دقيقة" (1106). The service name and duration are hardcoded; the price is `s.from`.
   - green **next-available chip** "أقرب موعد ٤:٠٠ م" (Earliest slot 4:00 PM) or "غداً ١٠:٠٠ ص" (Tomorrow 10:00 AM)
4. Floating pill button **عرض الخريطة** (Show map) (1114–1116) → map view.

**Reusable components:** LocationHeader, IconButton with badge dot, SearchField (with clear), ChipGroup (filter chip, sort chip, category chip), SectionHeader with link, ShopCard (horizontal variant / search-result variant with MatchedServiceRow + NextSlotChip), ServiceTile, ProfessionalTile, BottomNav, FloatingActionPill.

**Data fields:**

| Field | Sample | Type |
|---|---|---|
| location.label | حي الملقا، الرياض | string (reverse-geocoded or manual) |
| unreadNotifications | dot | int (show dot when > 0) |
| categories[] | الكل، حلاقة، لحية، أطفال | `{ id, name, slug }` (admin taxonomy, spec §8) |
| shop.id/slug | — | uuid / string |
| shop.name, area, rating, reviewCount, distanceKm, minPrice | as in A1; home samples reviewCount `٣٤٦/٢١٢/١٥٨` | string, string, decimal, int, decimal, decimal |
| shop.isOpenNow, opensAt | مفتوح / "يفتح ٢:٠٠ م" / مغلق | bool + local time `HH:mm` (or ISO instant) |
| shop.isVerified | shield | bool |
| shop.thumbnailUrl | — | string |
| popularService.name, price, durationMinutes | حلاقة شعر 60 ٣٠ د; تهذيب لحية 35 ٢٠ د; باقة شعر ولحية 85 ٥٠ د; حلاقة أطفال 45 ٢٥ د | **must become a category tile** (name only) or "from X" aggregated across shops; not a global price |
| topProfessional.firstName, specialty, rating (+ shop) | فيصل/تدريج وفيد/4.9; سلطان/لحية/4.8; راكان/كلاسيك/4.7 | string, string, decimal (+ shopSlug, proSlug, photoUrl) |
| search.total, radiusKm | 18, 5 | int, decimal |
| result.matchedService | تهذيب لحية · 35 ر.س · ٢٠ دقيقة | `{ serviceId, name, price, durationMinutes }` — the cheapest matching service of that shop |
| result.nextAvailableAt | "أقرب موعد ٤:٠٠ م" / "غداً ١٠:٠٠ ص" | ISO instant or null. Per mapRules (3787) it is computed live; when nothing is left today, the first slot tomorrow is shown. |
| activeFilterCount | ٢ | derived client-side |

**Interactions:** all [static]. Required behaviour:
- chip click → toggles a filter or sort (URL param)
- search submit → `/search`
- "الخريطة" link / "عرض الخريطة" → `view=map`
- shop card → shop page
- pro tile → pro profile
- bell → notifications
- location label → location sheet

Empty state (4613): "لا توجد محلات ضمن ٥ كم — وسّع نطاق البحث أو غيّر الحي لعرض نتائج أكثر." with CTA "توسيع النطاق إلى ١٥ كم". Skeleton loaders (3199–3212): max 3 s, then the error state.

**Routes:**
- `/[locale]/discover`: RSC shell; the sections depending on location load via a client island with lat/lng. noindex.
- `/[locale]/search`: Client, URL-driven, noindex.

**API:**
- `GET /api/v1/discovery/home?lat=&lng=&areaId=` → `{ locationLabel, nearbyShops: ShopSummaryDto[], categories: CategoryDto[], topProfessionals: ProfessionalSummaryDto[] }` (or three separate calls below).
- `GET /api/v1/service-categories` → `[{ id, slug, name }]`.
- `GET /api/v1/shops/search` params: `q, lat, lng, areaId, radiusKm (default 5), categoryId, serviceQuery, sort=distance|rating|earliest, openNow, verifiedOnly, availableToday, minPrice, maxPrice, page, pageSize (max 50)`. Returns `{ total, radiusKm, items: ShopSearchResultDto[] }`.
  - `ShopSearchResultDto = { id, slug, name, areaName, rating, reviewCount, distanceKm, isOpenNow, closesAt?, nextOpensAt?, isVerified, minPrice, currency:"SAR", thumbnailUrl, matchedService?: { id, name, price, durationMinutes }, nextAvailableAt?: instant, location:{lat,lng} }`.
- `GET /api/v1/professionals/top?lat=&lng=&limit=3` → `[{ id, slug, shopSlug, firstName, displayName, specialty, rating, reviewCount, photoUrl }]`.
- `GET /api/v1/me/notifications/unread-count` (authenticated only).

**Auth:** public (the bell dot only when signed in).

---

### A4. `s_c_map` — الخريطة والفلاتر (Map & filters) — lines 1123–1238

#### A4.1 Frame "MAP VIEW" (1126–1164)
Layout:
- full-bleed map (hatched placeholder)
- overlay header: search field "ابحث في هذه المنطقة" (Search this area) + filter icon button
- **segmented toggle قائمة / خريطة** (List / Map; Map active) (1136–1139)
- **price pins** (1142–1144): "35 ر.س" (selected — navy fill), "40 ر.س", "30 ر.س" (white)
- **user-location dot** (1145), blue with halo
- **bottom sheet** (1147–1162) for the selected shop:
  - grab handle
  - 76px image
  - "صالون الأصالة للحلاقة"
  - ★4.8 (٣٤٦) · 2.4 كم
  - green chip "أقرب موعد ٤:٠٠ م"
  - button **احجز** (Book)

#### A4.2 Frame "FILTER DRAWER · تفاعلي" (1166–1220) — [wired open/close]
- Closed state: floating pill **افتح الفلاتر** (Open filters) → `openFilter` (1217).
- Open state: scrim (rgba(16,40,61,.45)) plus a bottom sheet at 86% height (1170–1215). Contents:
  - Header "التصفية والترتيب" (Filter & sort) + close ✕ (`closeFilter`).
  - **الترتيب (Sort)** — single-choice radio cards: **الأقرب مسافةً** (Nearest, selected), الأعلى تقييماً (Highest rated), أقرب وقت متاح (Earliest available).
  - **الخدمة (Service)** — chips: **تهذيب لحية** (selected), حلاقة شعر, حلاقة أطفال, عناية بالوجه. Single-select as drawn; multi-select is possible.
  - **السعر (Price)** — dual-thumb range slider, label "25 — 90 ر.س".
  - Toggles:
    - **مفتوح الآن فقط** (Open now only) — ON
    - **محلات موثّقة فقط** (Verified shops only) — OFF
    - **يقبل الحجز اليوم** (Accepts booking today) — ON
  - Footer: **مسح** (Clear) [static] and primary **عرض ١٨ نتيجة** (Show 18 results) → `closeFilter`. The live result count sits inside the apply button.
- The scrim is not wired to close. Implement scrim-click and Esc to close, with a focus trap.

#### A4.3 Annotation "قواعد الخريطة والفلاتر" (`mapRules` 3785–3791) — business rules
1. "الدبوس يعرض سعر البداية — السعر الأدنى للخدمة المطلوبة في نتائج البحث، لا سعر المحل العام." → pin label = min price of the **searched service** at that shop (fallback: shop min price).
2. "أقرب موعد متاح على البطاقة — يُحسب لحظياً؛ إذا لم يتبقَّ وقت اليوم يُعرض أول وقت في الغد." → `nextAvailableAt` computed by the availability engine at query time.
3. "الفلاتر تُطبَّق على الخريطة والقائمة معاً — عدد النتائج يظهر داخل زر التطبيق قبل الإغلاق." → one filter state for both views; a count endpoint is needed.
4. "المحلات الموقوفة لا تظهر — الاشتراك المنتهي أو إيقاف الحجز المؤقت يُخفي المحل من الاكتشاف." → discovery excludes shops with an expired/suspended subscription **and** shops with paused bookings.
5. "الدرج يُفتح من الأسفل — في RTL تبقى حركة الأدراج رأسية، والأدراج الجانبية تنزلق من اليمين." → bottom sheets are vertical; side drawers slide from the inline-start (right) side in RTL.

**Reusable components:** MapView (provider adapter), PricePin (default/selected), UserLocationDot, SegmentedControl, BottomSheet (peek + full), ShopCard (map-sheet variant), FilterSheet, RadioCard, Chip, RangeSlider, Switch, Button.

**Data fields:** as `ShopSearchResultDto` plus `pinPrice` (= matchedService.price ?? minPrice) and `location {lat,lng}`. Filter metadata: `priceRange {min:25, max:90}` from the current result set.

**Routes:** `/[locale]/search?view=map&bbox=…` — Client, noindex. Filters are in the URL: `sort, category, minPrice, maxPrice, openNow, verified, today`.

**API:**
- `GET /api/v1/shops/search?...&bbox=minLng,minLat,maxLng,maxLat&view=map` → `{ total, items:[{ id, slug, name, location, pinPrice, isSelectedCandidate… }] }`. The same DTO works for the sheet card.
- `GET /api/v1/shops/search/count?…` → `{ total }` (for the apply button label "عرض N نتيجة"). Or reuse search with `pageSize=0`.
- `GET /api/v1/shops/search/facets?…` → `{ priceMin, priceMax, categories:[{id,name,count}] }`.

**Auth:** public.

---

### A5. `s_c_shop` — صفحة المحل والحلاق (Shop page & professional) — lines 1240–1434

#### A5.1 Frame "SHOP PAGE · تبويبات تفاعلية" (1243–1363) — tabs [wired]
Layout, top to bottom:
1. **Cover** (1247–1255), 180px. One image placeholder (**no gallery / carousel drawn**). Overlay buttons:
   - start side: back (chevR)
   - end side: **QR** icon (show/share the shop QR — behaviour undefined) and **heart** (favorite, red)
2. **Identity block** (1256–1266):
   - 62px logo tile with initial "أ" overlapping the cover
   - H2 "صالون الأصالة للحلاقة" + verified shield. Per `shopNotes` #2, the shield needs a text tooltip on press, not the icon alone.
   - ★ **4.8** (٣٤٦ تقييم) · **2.4 كم**
3. **Open status** (1267–1270): green badge with dot "مفتوح الآن" (Open now) + "يغلق ١١:٠٠ م" (Closes 11:00 PM).
4. **Address row** (1271–1275): pin, "طريق أنس بن مالك، حي الملقا، الرياض", link **الاتجاهات** (Directions → external maps deep link). **No embedded map.**
5. **Tabs** (1276–1280; `shopTabs` 3794–3807): **الخدمات** (Services) / **الحلاقون** (Barbers) / **التقييمات** (Reviews) / **عن المحل** (About). Underline style: active is 700 weight in #10283D with a 2.5px bottom border. Default tab is `services` (3437).
6. **Tab content** (1283–1356):
   - **Services** (1284–1297): rows (`services` 3757–3760), each with name, duration label, price "60 ر.س", and a small **احجز** button [static]. The designed samples are: حلاقة شعر 60 / ٣٠ دقيقة; تهذيب لحية 35 / ٢٠ دقيقة; باقة شعر ولحية 85 / ٥٠ دقيقة; حلاقة أطفال 45 / ٢٥ دقيقة. **The package is a plain row; there is no separate packages section.**
   - **Barbers** (1298–1312): rows (`shopPros` 3814–3818), each with a round photo, name, "skill · years", ★rating (count), and an availability chip — green with the next time when free today, amber when it is tomorrow.
     - فيصل القحطاني · تدريج وفيد · ٨ سنوات · 4.9 (٢١٢) · "٤:٠٠ م" (free)
     - سلطان الحربي · لحية وعناية · ٥ سنوات · 4.8 (١٣٤) · "٦:٣٠ م" (free)
     - راكان المطيري · حلاقة كلاسيك · ١١ سنة · 4.7 (٣٠٨) · "غداً ١٠:٠٠ ص" (not today)
     - Rows are not wired to the profile, but should link to A5.2.
   - **Reviews** (1313–1339):
     - Summary: average **4.8** + 5 stars. Histogram `ratingBars` (3680–3683) `[stars, count, %]` = [5,268,78] [4,52,15] [3,15,5] [2,7,2] [1,4,1]; the counts total 346. Bars for 4–5 stars are blue and 1–3 are grey.
     - Review list (`reviews` 3820–3824) — initials avatar, full name, relative time · **service name**, stars (all 5 hard-coded), text:
       - م ع · محمد العنزي · قبل ٣ أيام · باقة شعر ولحية · "التزام دقيق بالموعد، والتدريج نظيف جداً. المكان مرتب والاستقبال محترم."
       - ن ا · ناصر الزهراني · قبل أسبوع · حلاقة شعر · "الحجز عبر التطبيق وفّر علي الانتظار. دخلت وجلست على الكرسي مباشرة."
       - ي ق · يزيد القرني · قبل ١٢ يوماً · تهذيب لحية · "أسعار واضحة ولا توجد مفاجآت. سأعود بإذن الله."
     - No pagination or "more" control is drawn.
   - **About** (1340–1355):
     - H4 "ساعات العمل" (Working hours): a 7-row table (`shopHours` 3830–3833), with closed days greyed:
       - الأحد–الأربعاء ٩:٠٠ ص — ١١:٠٠ م
       - الخميس ٩:٠٠ ص — ١٢:٠٠ ص
       - الجمعة ٢:٠٠ م — ١٢:٠٠ ص
       - السبت مغلق
       - Closing at "١٢:٠٠ ص" means midnight, so the model must support an end time of 24:00 or past midnight.
     - H4 "السياسات" (Policies), three rows with icons:
       - clock: "الإلغاء مجاني حتى ساعتين قبل الموعد" (Free cancellation up to 2 h before)
       - alert: "التأخر أكثر من ١٥ دقيقة قد يُلغي الموعد" (Arriving more than 15 min late may cancel)
       - card: "الدفع في المحل — نقداً أو شبكة" (Pay at the shop — cash or card)
     - **No shop description text is drawn** (spec §12 requires a description).
7. **Sticky footer** (1358–1361): "تبدأ من" (Starts from) **30** ر.س + primary **احجز الآن** (Book now) → `goBooking` [wired]. The sample 30 does not match the cheapest listed service (35); the value must be computed.

#### A5.2 Frame "PROFESSIONAL PROFILE" (1365–1418)
Layout:
1. Header: back · title "الحلاق" (The barber) · heart (favorite professional).
2. Profile card:
   - 96px photo
   - H2 "فيصل القحطاني"
   - "تدريج وفيد · خبرة ٨ سنوات" (Fades · 8 years' experience)
   - ★ **4.9** (٢١٢ تقييم)
   - two stat tiles: **1,240** "موعد مكتمل" (completed appointments) and **98%** "التزام بالموعد" (punctuality/appointment adherence)
   - shop link chip "يعمل في صالون الأصالة — الملقا" (Works at …)
3. "أقرب الأوقات المتاحة اليوم" (Earliest available today): chips ٤:٠٠ م, ٥:٣٠ م, ٦:٣٠ م + dashed "+٧ أوقات" (+7 times). The chips are [static]; tapping one should start the wizard with the professional and time preselected (it still needs a service).
4. "الخدمات التي يقدمها" (Services he provides): حلاقة شعر 60 ر.س · ٣٠ د; باقة شعر ولحية 85 ر.س · ٥٠ د; تهذيب لحية 35 ر.س · ٢٠ د.
5. "آخر التقييمات" (Latest reviews): one card — خ ا · خالد الدوسري · قبل يومين · ★5 · "أفضل تدريج أخذته. دقيق في الوقت ويشرح الخيارات قبل ما يبدأ."
6. Sticky CTA **احجز مع فيصل** (Book with Faisal) → `goBooking`. The prototype does not preselect the professional; the rebuild must pass `pro=`.

**No phone number or WhatsApp number of the professional is shown** — compliant with spec §8.

#### A5.3 Annotation "ما الذي تحسمه صفحة المحل؟" (1420–1431; `shopNotes` 3835–3841) — rules
Order of information follows the customer's questions: open? how far? price? who cuts my hair? trustworthy?
1. Open status and distance appear above the fold.
2. Verified shield + a text tooltip on press.
3. Prices and durations are visible in the service list before entering booking.
4. "احجز الآن" is sticky at the bottom together with the starting price.
5. Reviews are linked to a specific service (the review DTO needs `serviceName`).

**Reusable components:** CoverImage/Gallery, IconButton (overlay), ShopIdentityHeader, VerifiedBadge+Tooltip, RatingInline, DistanceChip, OpenStatusBadge, AddressRow with Directions link, Tabs (underline), ServiceRow (with Book), ProfessionalRow (with AvailabilityChip), RatingSummary (avg + histogram), ReviewItem, HoursTable, PolicyList, StickyCTAFooter (price + button), ProfileCard, StatTile, TimeChip, FavoriteToggle.

**Data fields (ShopDetailDto):**

| Field | Sample | Type |
|---|---|---|
| id, slug | — | uuid, string |
| name | صالون الأصالة للحلاقة | string (localized ar/en) |
| logoInitial / logoUrl | أ | string |
| coverImageUrl, galleryImages[] | placeholder | string[] (gallery per spec; the design has a single cover) |
| isVerified | true | bool |
| rating, reviewCount | 4.8, 346 | decimal, int |
| distanceKm | 2.4 | decimal? (only when lat/lng supplied) |
| isOpenNow, closesAt, nextOpensAt | مفتوح الآن, يغلق ١١:٠٠ م | bool, local time |
| address (line, area, city) | طريق أنس بن مالك، حي الملقا، الرياض | string (localized) |
| location {lat,lng} | — | for the Directions deep link and a future map |
| minPrice | 30 (sample; should be 35) | decimal |
| services[] | `{ id, name, description?, price, durationMinutes, isPackage?, items? }` | shop-owned (spec §10) |
| professionals[] | `{ id, slug, displayName, specialty, yearsExperience, rating, reviewCount, photoUrl, nextAvailableAt? }` | yearsExperience int |
| ratingSummary | `{ average, count, histogram:[{stars, count, percent}] }` | |
| reviews (paged) | `{ id, authorDisplayName (first name — see §E), authorInitials, createdAt, serviceName, stars, comment, tags? }` | |
| openingHours[] | `{ dayOfWeek, isClosed, intervals:[{open:"09:00", close:"23:00"}] }` | close may be "24:00" |
| policies | `{ freeCancellationHoursBefore: 2, lateToleranceMinutes: 15, paymentNote: "cash/card at shop" }` | from platform/shop settings |
| description | (absent from design) | string (localized) |

**ProfessionalDetailDto:** `{ id, slug, shop:{slug,name,areaName}, displayName, specialty, yearsExperience, rating, reviewCount, completedBookings, punctualityRate (percent, definition needed), photoUrl, services:[{id,name,price,durationMinutes}], todaySlots:{ firstSlots: instant[3], remainingCount:int }, latestReviews: ReviewDto[] }`. **Never include a phone/WhatsApp field.**

**Interactions:**
- [wired]: tab switch, "احجز الآن" and "احجز مع فيصل" → wizard.
- [static]: back, QR, heart (favorite toggle, optimistic with rollback), Directions, per-service احجز (→ wizard with `service=` preselected, starting at the professional step), professional row → pro profile, time chip → wizard.

**Routes:**
- `/[locale]/shops/[shopSlug]` — **Server Component, indexable**. JSON-LD: `HairSalon`/`LocalBusiness` with address, geo, openingHoursSpecification, `aggregateRating` only from stored data, and BreadcrumbList.
  - Render all four tab panels on the server; the tab switcher is a client island with `?tab=` sync so services and reviews are crawlable.
  - Reviews pagination: `?reviewsPage=`.
  - The distance is client-injected; it is not part of the cached HTML.
- `/[locale]/shops/[shopSlug]/professionals/[proSlug]` — **Server, indexable** (Person + worksFor). The today-slots block is a client island (uncached).

**API:**
- `GET /api/v1/shops/{slug}?lat=&lng=` → `ShopDetailDto` (without reviews list)
- `GET /api/v1/shops/{shopId}/services` → `ServiceDto[]` (active only, ordered)
- `GET /api/v1/shops/{shopId}/professionals?withNextAvailable=true` → `ProfessionalSummaryDto[]`
- `GET /api/v1/shops/{shopId}/reviews?page=&pageSize=` → `{ summary, items: ReviewDto[] }`
- `GET /api/v1/shops/{shopSlug}/professionals/{proSlug}` → `ProfessionalDetailDto`
- `GET /api/v1/availability/next?shopId=&professionalId=&date=today&limit=3` → `{ slots: instant[], remainingCount }`
- `PUT /api/v1/me/favorites/shops/{shopId}` / `DELETE …`; `PUT /api/v1/me/favorites/professionals/{professionalId}` / `DELETE …` (customer only). The page needs `isFavorite` when authenticated: `GET /api/v1/me/favorites/ids`.

**Auth:** public. The favorite toggle requires a customer; prompt sign-in with `returnTo`.

---

### A6. `s_c_booking` — مسار الحجز التفاعلي (Interactive booking flow) — lines 1436–1663

The full step-by-step detail is in **§B**. This section is the summary.

**Frames:**
1. **"BOOKING FLOW · تفاعلي بالكامل"** (1439–1598). This is the wizard: a header with a progress bar, 5 step bodies, a sticky footer, and a success overlay.
2. **"WHATSAPP CONFIRMATION"** (1600–1636). A mock of the WhatsApp chat the customer receives. It documents template content, not a web screen:
   - Chat header "TRIMME · حساب أعمال موثّق" (verified business account).
   - Date pill "اليوم".
   - Bubble 1 (confirmation):
     - "تم تأكيد موعدك ✓"
     - "صالون الأصالة للحلاقة"
     - "الخدمة: باقة شعر ولحية"
     - "الحلاق: فيصل القحطاني"
     - "الموعد: الخميس ١٨ سبتمبر، ٥:٣٠ م"
     - "المدة: ٥٠ دقيقة · المبلغ: 85 ر.س"
     - "العنوان: طريق أنس بن مالك، الملقا"
     - Buttons **الاتجاهات** and **إدارة الموعد** (Manage booking). These imply a WhatsApp template with URL buttons that deep-link to `/ar/account/bookings/{id}`.
     - Timestamp "9:42 م".
   - Bubble 2 (reminder): "تذكير: موعدك بعد ٣ ساعات في صالون الأصالة مع فيصل القحطاني الساعة ٥:٣٠ م." (2:30 م). **This is a 3-hour reminder, but the spec requires 30 minutes** — see §E.
   - System note: "قوالب الرسائل تُدار من لوحة الإدارة — تأكيد، تعديل، إلغاء، تذكير" (templates managed in admin: confirm, update, cancel, reminder). This is consistent with spec §16.
   - Placeholders visible: shop name, service name, professional name, date, time, duration, amount, address. **Duration, amount and address are not in the spec's placeholder whitelist example** (§16 lists customer, professional, shop, service, date, time, time remaining). Add them to the whitelist.
3. **Annotation "جرّب المسار"** (1638–1650). Copy: "كل خطوة في هذا النموذج تعمل فعلاً: اختر خدمة مختلفة وستتغير مدة الموعد وشبكة الأوقات ووقت الانتهاء في الملخص." In the prototype, the grid does **not** actually change except for one reason string (see §B.4). Rules (`bookingRules` 3999–4005):
   1. "المدة تحدد الشبكة — خدمة ٥٠ دقيقة تُظهر أوقاتاً أقل من خدمة ٢٠ دقيقة في نفس اليوم."
   2. "«أي حلاق متاح» أولاً — الخيار الافتراضي يفتح أكبر عدد من الأوقات ويقلل الرجوع خطوة للخلف." ("Any available barber" should be the **default** selection. The prototype state defaults to `p2`, at 3436.)
   3. "لا وقت وهمي — المعطّل يبقى مرئياً ليُفهم امتلاء اليوم، لكنه غير قابل للضغط ومعه سبب."
   4. "الملخص قبل التأكيد — المحل، الخدمة، الحلاق، المدة، السعر، التاريخ، والوقت في شاشة واحدة."
   5. "التأكيد ليس نهاية — شاشة النجاح تقود إلى «مواعيدي» لا إلى طريق مسدود."
4. **Annotation "حالة الوقت المعطّل — نص بديل واضح"** (1651–1659). These are canonical reason texts for disabled slots and days:
   - «محجوز مع حلاق آخر — جرّب ٦:٣٠ م» (Booked with another barber — try 6:30 PM)
   - «داخل استراحة صلاة العصر» (Within the Asr prayer break)
   - «لا يتسع لمدة ٥٠ دقيقة قبل إغلاق المحل» (Doesn't fit 50 minutes before the shop closes)
   - «الحلاق في إجازة هذا اليوم» (The barber is on leave this day)

**Route:** `/[locale]/shops/[shopSlug]/book` — Client Component, noindex. Its step and selections are held in search params. See §B for endpoints, DTOs and auth.

---

### A7. `s_c_appointments` — المواعيد والتفاصيل (Appointments & details) — lines 1665–1804

#### A7.1 Frame "MY APPOINTMENTS · تبويب تفاعلي" (1668–1740) — tabs [wired]
Layout:
- H2 "مواعيدي" (My appointments).
- **Segmented tabs** (`apptTabs` 4009–4019): **قادمة** (Upcoming, default) / **سابقة** (Past). The template reserves 3 placeholders; only 2 tabs exist.
- **Upcoming list** (1681–1712; `upcoming` 4022–4025). Each card has:
  - A date block (day number "18" and month "سبتمبر", light blue).
  - Service name + status badge with dot.
  - "shop · professional".
  - A clock icon + time range.
  - A footer with two actions: **التفاصيل** (Details) and **إعادة جدولة** (Reschedule), both [static].
  - Samples:
    - 18 سبتمبر · باقة شعر ولحية · **مؤكد** · صالون الأصالة · فيصل القحطاني · ٥:٣٠ م — ٦:٢٠ م
    - 25 سبتمبر · حلاقة أطفال · **بانتظار التأكيد** · باربر هاوس · سلطان الحربي · ١١:٠٠ ص — ١١:٢٥ ص
  - Below the list sits a dashed "rebook" card: plus icon, "احجز موعدك القادم" (Book your next appointment), "نفس المحل، نفس الحلاق، بنقرتين" (Same shop, same barber, two taps), and a chevron. It opens the wizard pre-filled from the last booking.
- **Past list** (1713–1732; `past` 4029–4034). Each card has a grey date block, service, status badge, "shop · professional", and **one contextual action** (4026–4028):
  - 04 سبتمبر · حلاقة شعر · **مكتمل** · صالون الأصالة · فيصل القحطاني → link **قيّم الزيارة** (Rate the visit)
  - 21 أغسطس · تهذيب لحية · **مكتمل** · صالون الرواد · ماجد العتيبي → text "تم التقييم ★ 5.0" (Rated)
  - 09 أغسطس · عناية بالوجه · **ملغي** · لمسة الرجل · راكان المطيري → **إعادة الحجز** (Rebook)
  - 28 يوليو · حلاقة شعر · **لم يحضر** · باربر هاوس · سلطان الحربي → **إعادة الحجز**
- Bottom nav (مواعيدي active).
- Empty state (4614): "جدولك فارغ حالياً — احجز حلاقتك القادمة من محلاتك المفضلة بنقرتين." with CTA "استكشف المحلات".

#### A7.2 Frame "APPOINTMENT DETAILS" (1742–1789)
Layout:
1. Header: back · "تفاصيل الموعد" (Appointment details) · **⋯ more** (1750). The menu contents are **undefined**.
2. **Hero card** (1754–1763):
   - Status badge "مؤكد".
   - Date "الخميس ١٨ سبتمبر".
   - Time range "٥:٣٠ م — ٦:٢٠ م" (28px).
   - **Countdown** "يتبقى يومان و٣ ساعات" (2 days and 3 hours remaining).
   - Two buttons: **أضف للتقويم** (Add to calendar → .ics) and **الاتجاهات** (Directions).
3. **Detail rows** (`detailRows` 4036–4042):
   - store icon — المحل (Shop): صالون الأصالة
   - scissors — الخدمة: باقة شعر ولحية
   - user — الحلاق: فيصل القحطاني
   - clock — المدة: ٥٠ دقيقة
   - qr — رقم الحجز (Booking ref): **TR-48219**
   - Total row "الإجمالي" **85** ر.س (hardcoded, 1776). This is the snapshot price.
4. **Cancellation policy card** (1779–1782): "سياسة الإلغاء" — "الإلغاء مجاني حتى ٣:٣٠ م من يوم الموعد. بعد ذلك يتحول الطلب إلى «طلب إلغاء» يراجعه المحل." (Free cancellation until 3:30 PM on the appointment day; after that it becomes a "cancellation request" reviewed by the shop). The cutoff is the start time minus 2 h.
5. Action row (1783–1786):
   - **إعادة جدولة** (Reschedule): outlined, [static]. It goes to A8.2.
   - **إلغاء الموعد** (Cancel appointment): danger-soft #FBEAEA/#9B2C2C, calls `openCancel` [wired]. The sheet itself is only drawn in the `s_c_rate` screen, so clicking it here sets the state but no sheet appears on this screen.

#### A7.3 Annotation "قواعد شاشة المواعيد" (`apptNotes` 4043–4049) — rules
1. The card answers four questions without opening details: when, which service, which shop and professional, and the status.
2. The countdown "يتبقى …" reduces forgetting more than the date alone. The API should provide `startAt` and let the client compute the countdown.
3. "زر التقييم يظهر فقط على المواعيد المكتملة، ويختفي بعد التقييم أو بعد ٧ أيام." → `canReview` = Completed ∧ not reviewed ∧ now ≤ completedAt + 7 days.
4. "الإلغاء إجراء مدمّر، لذا يُعامل بلون خطأ + نافذة تأكيد + ذكر الأثر على المحل."
5. "إعادة الجدولة تحتفظ بالموعد القديم حتى يتأكد الجديد، فلا يفقد العميل مكانه." → reschedule is an atomic move: hold the old slot until the new one commits, in one transaction.

**Reusable components:** SegmentedTabs, AppointmentCard (upcoming variant with action footer; past variant with contextual action), DateBlock, StatusBadge, RebookCard, EmptyState, BottomNav, BookingHero (status + date + time range + countdown + actions), SummaryList (icon/key/value), TotalRow, PolicyCallout, Button (outline, danger-soft), OverflowMenu.

**Data fields (BookingListItemDto / BookingDetailDto):**

| Field | Sample | Type |
|---|---|---|
| id | — | uuid |
| reference | TR-48219 | string (human-readable, unique) |
| status | Confirmed / Pending / Completed / Cancelled / NoShow (/ Arrived) | enum |
| cancelledBy | (for ملغي) | enum? |
| startAt, endAt | 18 Sep 17:30–18:20 (Asia/Riyadh) | ISO instant (UTC) + shop timeZone |
| shop | {id, slug, name: صالون الأصالة, address, location} | nested |
| professional | {id, slug, displayName: فيصل القحطاني} | nested (null only while unassigned — see "any" in §B) |
| service snapshot | {serviceId, name: باقة شعر ولحية, durationMinutes: 50, price: 85, currency: SAR} | snapshot (spec §10) |
| customerNote | (from wizard) | string? |
| cancellation | { freeUntil: instant (start − 2h), mode: "Direct" \| "RequestOnly" \| "NotAllowed" } | derived server-side |
| canReschedule | bool | derived |
| review | { canReview: bool, reviewDeadline: instant, submittedStars?: 5.0 } | derived |
| rebookTemplate | {shopSlug, serviceId, professionalId} | for "إعادة الحجز" / "احجز موعدك القادم" |

**Routes:**
- `/[locale]/account/bookings?tab=upcoming|past`: Server (auth, `dynamic`) with client tabs. Paginated past list. noindex.
- `/[locale]/account/bookings/[bookingId]`: Server + client action buttons. noindex.

**API:**
- `GET /api/v1/me/bookings?scope=upcoming|past&page=&pageSize=` → `{ items: BookingListItemDto[], total }`
- `GET /api/v1/me/bookings/{id}` → `BookingDetailDto` (404 when the booking is not the caller's)
- `GET /api/v1/me/bookings/{id}/calendar.ics` → `text/calendar`
- `POST /api/v1/me/bookings/{id}/cancel`, `POST /api/v1/me/bookings/{id}/reschedule` — see A8.

**Auth:** customer (owner only, enforced in the API).

---

### A8. `s_c_rate` — الإلغاء وإعادة الجدولة والتقييم (Cancel, reschedule & rate) — lines 1806–1923

#### A8.1 Frame "CANCEL CONFIRMATION · تفاعلي" (1809–1841) — [wired]
- The background is the details screen dimmed to 50%: "الخميس ١٨ سبتمبر", "٥:٣٠ م — ٦:٢٠ م".
- Closed state: a sticky bottom button **إلغاء الموعد** → `openCancel` (1837–1839).
- Open state: a scrim plus a **bottom sheet** (1822–1836) containing:
  - Grab handle and a red alert tile.
  - H3 **"إلغاء موعد الخميس ٥:٣٠ م؟"** (Cancel Thursday's 5:30 PM appointment?).
  - Body: "سيُخطر صالون الأصالة فوراً، ويُتاح الوقت لعملاء آخرين. لا يمكن التراجع بعد التأكيد." (Al-Asala salon will be notified immediately and the slot becomes available to others. This cannot be undone.)
  - "سبب الإلغاء (اختياري)" (Cancellation reason, optional). Chips: **ظرف طارئ** (Emergency, drawn selected), تغيّر الوقت (Time changed), سأحجز لاحقاً (Will book later). The chips are [static] and single-select.
  - Primary danger button **نعم، ألغِ الموعد** (Yes, cancel) → `doCancel`: closes the sheet and shows the toast **"تم إلغاء الموعد وأُخطر المحل"** (Appointment cancelled and the shop notified).
  - Ghost button **إبقاء الموعد** (Keep appointment) → `closeCancel`.
- The design does not draw the "طلب إلغاء" (cancellation request) variant that applies after the 2-hour cutoff; it exists only in the copy at 1781 and 3503. See §E.

#### A8.2 Frame "RESCHEDULE" (1843–1873)
- Header: back · "إعادة الجدولة" (Reschedule).
- **Amber info callout** (1851–1854): "موعدك الحالي: الخميس ١٨ سبتمبر ٥:٣٠ م. سيبقى محجوزاً حتى تؤكد الموعد الجديد." (Your current appointment … stays booked until you confirm the new one.)
- "اختر تاريخاً جديداً" (Pick a new date): a 4-column grid of 8 day cells (`reschedDates` 4063–4072) with day-of-week and date number. Samples: 18 خمي, 19 جمع, **20 سبت (off, struck through)**, **21 أحد (selected)**, 22 إثن, **23 ثلا (off)**, 24 أرب, 25 خمي. There is no availability-count note under each day (unlike the wizard), and no reason is shown.
- "الأوقات المتاحة — الأحد ٢١ سبتمبر": a 3-column slot grid (`reschedSlots` 4073–4082). Samples:
  - ١٠:٠٠ ص, ١٠:٤٥ ص, ٢:٠٠ م, ٤:١٥ م, ٥:٠٠ م, ٧:٣٠ م (ok)
  - ١١:٣٠ ص and ٣:٠٠ م (off, struck through)
  - **٦:٠٠ م** (selected)
- The cells are plain divs with no reason toasts [static].
- Footer: "الجديد: الأحد ٢١ سبتمبر · ٦:٠٠ م" (New: …) + primary **تأكيد الموعد الجديد** (Confirm new appointment) → toast **"تم تحديث موعدك إلى الأحد ٢١ سبتمبر ٦:٠٠ م"** [wired].
- The reschedule keeps the same service and professional; changing either is not designed.

#### A8.3 Frame "RATE AFTER VISIT · تفاعلي" (1875–1920) — [wired]
- Header: close ✕ · "تقييم الزيارة" (Rate the visit).
- Centered block: 80px professional photo, H3 **"كيف كانت زيارتك مع فيصل؟"** (How was your visit with Faisal?), and the meta line "باقة شعر ولحية · صالون الأصالة · أمس ٥:٣٠ م" (service · shop · relative date and time).
- **5 star buttons**, 48px each (1888–1899), `setStar1..5`. Filled stars are #D89A2E and empty ones #DCE3EA.
- Label under the stars (`starLabel` 4095), by star count:
  - 0: "اختر تقييمك" (Choose your rating)
  - 1: "سيئة" (Poor)
  - 2: "مقبولة" (Acceptable)
  - 3: "جيدة" (Good)
  - 4: "ممتازة" (Excellent)
  - 5: "استثنائية" (Exceptional)
- "ما الذي أعجبك؟" (What did you like?) tag chips (`ratingTags` 4096–4103): الالتزام بالوقت (Punctuality), جودة الحلاقة (Cut quality), النظافة (Cleanliness), التعامل (Service/manners), السعر (Price). The chips are not clickable. The prototype auto-highlights the first two when stars ≥ 4. The rebuild should make them multi-select toggles.
- "تعليق (اختياري)" (Comment, optional) textarea. Placeholder: "شاركنا تفاصيل تجربتك لتساعد غيرك".
- Privacy callout (eye-off icon): **"يظهر تقييمك باسمك الأول فقط، ولا يمكن تعديله بعد النشر."** (Your review shows your first name only and cannot be edited after publishing.)
- Footer CTA has 3 states (1915–1917; 3475–3477):
  - `rateIdle`: disabled, "اختر عدد النجوم أولاً" (Choose the number of stars first).
  - `rateReady`: primary **إرسال التقييم** (Submit review) → `submitRating`.
  - `rateDone`: green success bar **"شكراً لتقييمك"** plus the toast **"نُشر تقييمك — شكراً لك"** (Your review is published).
- In the prototype the stars stay clickable after submit. The rebuild must lock the form once the review is submitted.

**Reusable components:** BottomSheet/AlertDialog (destructive), ChoiceChips, Button (danger, ghost), Toast, InfoCallout (warning tone), DateGrid cell (normal/selected/off), SlotGrid, StickyFooter with summary line, StarRatingInput (radio group semantics, 48px targets), TagToggleGroup, Textarea, InfoCallout (neutral with eye-off icon).

**Data / API:**
- Cancel:
  - `POST /api/v1/me/bookings/{id}/cancel` with header `Idempotency-Key` and body `{ reasonCode: "Emergency"|"TimeChanged"|"BookLater"|null, rowVersion }`.
  - Before the cutoff → `200 { status: "Cancelled", cancelledBy: "Customer" }`.
  - After the cutoff (design) → `202 { status: <unchanged>, cancellationRequest: { id, status: "Pending" } }`.
  - Errors: `409 booking_state_invalid`, `412 concurrency`.
  - Side effects: slot freed, shop notified (SignalR + in-app), WhatsApp cancellation to the customer and professional via the outbox, and the obsolete reminder jobs are cancelled (spec §16).
- Reschedule:
  - `GET /api/v1/me/bookings/{id}/reschedule/days?from=&days=` and `GET /api/v1/me/bookings/{id}/reschedule/slots?date=`. These are the same shapes as the wizard's availability endpoints (§B), computed with the booking's own interval excluded.
  - `POST /api/v1/me/bookings/{id}/reschedule` with `Idempotency-Key` and body `{ startAt, professionalId?, rowVersion }` → `200 BookingDetailDto`, or `409 slot_unavailable { alternatives: instant[] }`. The old slot is held until the new one commits (one transaction plus the exclusion constraint).
- Review:
  - `GET /api/v1/me/bookings/{id}/review-context` → `{ professional:{displayName, photoUrl}, shopName, serviceName, startAt, canReview, reviewDeadline }`.
  - `GET /api/v1/review-tags` → `[{ code, label }]`.
  - `POST /api/v1/me/bookings/{id}/review` with body `{ stars: 1..5, tagCodes: string[], comment?: string(≤1000) }` → `201 ReviewDto`.
  - Errors: `409 review_exists`, `403 booking_not_completed`, `410 review_window_closed`.
  - Per assumption #4, the review is attributed to both the professional and the shop. Rating aggregates are updated transactionally (spec §17).

**Routes:**
- Cancel: a dialog on `/[locale]/account/bookings/[id]`. An intercepting route is optional. Client.
- `/[locale]/account/bookings/[id]/reschedule` — Client, noindex.
- `/[locale]/account/bookings/[id]/review` — Client, noindex. It is also reached from the Notifications item "قيّم زيارتك الأخيرة" and the Past-tab link.

**Auth:** customer (booking owner). The review additionally requires the booking to be Completed, not yet reviewed, and within the 7-day window.

---

### A9. `s_c_profile` — المفضلة والإشعارات والحساب (Favorites, notifications & account) — lines 1925–2039

#### A9.1 Frame "FAVORITES" (1928–1965)
- H2 "المفضلة" (Favorites).
- Count line "٣ محلات · حلاقان" (3 shops · 2 barbers). The frame actually shows 3 shops and 1 barber, which is inconsistent.
- Shop rows (reusing `homeShops`): 64px image, name, ★rating · area, open badge, and a **filled red heart button** (unfavorite) [static].
  - صالون الأصالة · 4.8 · الملقا · مفتوح
  - باربر هاوس · 4.7 · حطين · مفتوح
  - لمسة الرجل · 4.6 · النرجس · يفتح ٢:٠٠ م
- Professional row (1952–1956): round photo, "فيصل القحطاني", "صالون الأصالة · ★ 4.9", and a primary **احجز** (Book) button that opens the wizard with the professional preselected.
- Bottom nav (المفضلة active).
- No empty state is drawn for favorites.

#### A9.2 Frame "NOTIFICATIONS" (1967–1993)
- H2 "الإشعارات" + link **تعليم الكل كمقروء** (Mark all as read) [static].
- Items (`notifications` 4113–4129). Each item has an icon tile coloured by tone (ok = green, warn = amber, info = blue), a title, a body, a relative time and an unread dot (blue). Unread rows are white with a border; read rows are transparent.

  | Icon / tone | Title | Body | When | Unread | Proposed `type` |
  |---|---|---|---|---|---|
  | check / ok | تم تأكيد موعدك (Your appointment is confirmed) | صالون الأصالة · الخميس ١٨ سبتمبر ٥:٣٠ م | قبل ٥ دقائق | yes | `BookingConfirmed` |
  | clock / info | تذكير بموعدك غداً (Reminder: your appointment tomorrow) | باربر هاوس · ١١:٠٠ ص مع سلطان الحربي | قبل ساعتين | yes | `BookingReminder` |
  | star / warn | قيّم زيارتك الأخيرة (Rate your last visit) | حلاقة شعر مع فيصل القحطاني — تقييمك يساعد غيرك | أمس | no | `ReviewRequested` |
  | refresh / info | تم تعديل موعدك (Your appointment was changed) | المحل نقل الموعد من ٤:٠٠ م إلى ٤:٣٠ م (the shop moved it) | قبل ٣ أيام | no | `BookingRescheduledByShop` |
  | tag / info | خدمة جديدة في محلك المفضل (New service at your favorite shop) | صالون الأصالة أضاف «عناية بالوجه» — 70 ر.س | قبل أسبوع | no | `FavoriteShopNewService` |

- Implications:
  - Shop-initiated reschedule exists as an event. The shop screens do not show it; check the shop-dashboard analysis.
  - The "day-before" reminder is a third reminder cadence alongside the design's "3 h" and the spec's "30 min".
  - Favorites drive the new-service notifications.
- Items should deep-link to the booking, the review form or the shop.
- The bottom nav uses `nav.home`, so Home is active.

#### A9.3 Frame "PROFILE & SETTINGS" (1995–2036)
- Header:
  - Avatar with initials "ع ش".
  - Name **عبدالله الشمري**.
  - The customer's **own** phone **+966 50 214 8830**, LTR (2004). This is acceptable: it is the customer's own data.
  - Edit icon button → personal info.
  - Two stat tiles: **24** "موعد مكتمل" (completed appointments) and **3** "محل مفضل" (favorite shops).
- Settings groups (`settingsGroups` 4133–4148). The group labels are rendered in the Latin letter-spaced caption style.

  | Group | Item | Icon | Value |
  |---|---|---|---|
  | التفضيلات (Preferences) | اللغة (Language) | msg | العربية |
  | | إشعارات واتساب (WhatsApp notifications) | bell | **مفعّلة** (Enabled, green) |
  | | الموقع (Location) | pin | **مفعّل** (Enabled, green) |
  | الحساب (Account) | البيانات الشخصية (Personal info) | user | — |
  | | **طريقة الدفع (Payment method)** | card | **في المحل** (At the shop) — see §E |
  | | الخصوصية والبيانات (Privacy & data) | shield | — |
  | الدعم (Support) | الأسئلة الشائعة (FAQ) | info | — |
  | | تواصل معنا (Contact us) | msg | — |

- **تسجيل الخروج** (Sign out) is a red text button (2028).
- Bottom nav (حسابي active).
- **Absent:** security/sessions ("revoke all sessions"), change phone number, delete account, and email. Spec §9/§12 requires security/session settings.

**Reusable components:** FavoriteShopRow, FavoriteProRow, HeartToggle, NotificationItem (tone variants, unread state), ProfileHeader (avatar initials, name, phone LTR), StatTile, SettingsGroup/SettingsRow (icon, label, value, chevron), Button (text danger).

**Data / API:**
- Favorites:
  - `GET /api/v1/me/favorites` → `{ shops: [{ id, slug, name, areaName, rating, isOpenNow, opensAt?, thumbnailUrl }], professionals: [{ id, slug, shopSlug, shopName, displayName, rating, photoUrl }] }`
  - `PUT/DELETE /api/v1/me/favorites/shops/{id}`
  - `PUT/DELETE /api/v1/me/favorites/professionals/{id}`
  - Optimistic UI with rollback (spec §5).
- Notifications:
  - `GET /api/v1/me/notifications?page=&pageSize=` → `{ items: [{ id, type, tone, title, body, createdAt, readAt?, link: { kind: "Booking"|"Review"|"Shop", id|slug } }], unreadCount }`. Title and body are rendered server-side from a localized template, or the API returns `type` + `params` for client rendering; the latter is preferred for ar/en.
  - `POST /api/v1/me/notifications/{id}/read`
  - `POST /api/v1/me/notifications/read-all`
  - Optional SignalR customer hub for live updates.
- Profile:
  - `GET /api/v1/me/profile` → `{ displayName, initials, phoneE164 (own), phoneDisplay, locale, whatsappOptIn, locationEnabled, stats: { completedBookings, favoriteShops } }`
  - `PATCH /api/v1/me/profile` `{ displayName }`
  - `PUT /api/v1/me/preferences` `{ locale: "ar"|"en", whatsappOptIn: bool }`. "الموقع" is a device or browser permission, not a server setting; it stores the preference only.
  - `POST /api/v1/auth/sign-out`
  - Spec-required and not designed: `GET /api/v1/me/sessions`, `DELETE /api/v1/me/sessions/{id}`, `POST /api/v1/me/sessions/revoke-all`.

**Routes:** `/[locale]/account/favorites`, `/[locale]/account/notifications`, `/[locale]/account` (plus sub-pages `/account/personal`, `/account/privacy`, `/account/language` or an inline switch, and the not-designed `/account/security`). All require a customer and are noindex.

**Auth:** customer.

---

### A10. `s_c_qr` — صفحة رمز QR (QR landing page) — lines 2041–2116

#### A10.1 Frame "QR LANDING · قبل التسجيل" (QR landing — before sign-up) (2043–2082)
- Cover (150px) with a navy badge containing a QR icon: **"دخلت عبر رمز المحل"** (You entered via the shop's code).
- Identity: logo "أ", "صالون الأصالة للحلاقة" + verified shield, and "حي الملقا · مفتوح حتى ١١:٠٠ م" (district · open until 11 PM).
- Info callout (blue): **"أنت داخل صفحة هذا المحل مباشرة. اختر خدمتك ثم أدخل رقم جوالك لإتمام الحجز."** (You're on this shop's page directly. Choose your service, then enter your mobile number to complete the booking.)
- "أقرب الأوقات اليوم" (Earliest times today): chips **٤:٠٠ م** (highlighted navy), ٥:٣٠ م, ٦:٣٠ م [static].
- "الخدمات" (Services): rows with name, duration and price, reusing `services`. There is no per-row book button.
- Sticky CTA **احجز الآن** → `goBooking` [wired]. Caption: **"يُطلب رقم الجوال في خطوة التأكيد فقط"** (The mobile number is only requested at the confirmation step).
- Compared with the full shop page, there are no tabs, reviews, distance, address row or favorite/QR buttons.
- The professional-QR variant ("رمز لكل حلاق — يفتح صفحة الحلاق مباشرة — مناسب لمرآة الكرسي", 2101) is described but **not drawn**. Use A5.2 with the QR badge.

#### A10.2 Panel "مواد الطباعة — بطاقة QR للمحل" (Print materials — shop QR card) (2084–2114)
This is not a customer screen; it belongs to the admin QR generation feature (spec §14/§17). It shows:
- A print card: navy background, logo, QR, "صالون الأصالة", "امسح الرمز واحجز دورك" (Scan and book your turn).
- Notes:
  - "رمز لكل محل" — opens the shop page with services and available times.
  - "رمز لكل حلاق" — opens the professional page directly, for the chair mirror.
  - "كل مسح يُحتسب" — conversion from scan to confirmed booking is measured in the admin dashboard.
- Buttons: **PNG / SVG / PDF** download and **ملصق A5** (A5 poster).

**Reusable components:** QrEntryBadge, ShopIdentityHeader (compact), InfoCallout, TimeChip, ServiceRow (price-only variant), StickyCTAFooter with caption.

**Data:** `QrResolveDto = { code, targetType: "Shop"|"Professional", shop: { slug, name, areaName, isVerified, isOpenNow, closesAt, logoUrl, coverImageUrl }, professional?: { slug, displayName }, services: ServiceDto[], todaySlots: { firstSlots: instant[3] }, attributionToken }`.

**Route:** `/[locale]/q/[code]`. The component library shows the QR target as a short vanity URL `trimme.sa/s/alasalah-malqa` (659), so a locale-less short path `/s/[code]` → 302 to `/[locale]/q/[code]` (locale from Accept-Language, default `ar`) should also be supported. This is a Server Component. On the request it:
1. Resolves the code.
2. Records a `QrVisit` server-side. No invasive tracking (spec §17): store a hashed visitor id and timestamp only.
3. Sets a short-lived first-party `qr_attr` cookie (HttpOnly) with the attribution token.
4. Renders the QR variant of the shop page, or redirects to `/[locale]/shops/[slug]?via=qr`.

The page is **noindex** with a canonical pointing to the shop or professional page. Unknown or disabled codes return a 404 state.

**API:**
- `GET /api/v1/qr/{code}` → `QrResolveDto`. `POST /api/v1/qr/{code}/visits` records the visit; this may be folded into the GET on the server side. Both are rate-limited (spec §18).
- The attribution token is passed through the wizard to `POST /api/v1/bookings` (`qrAttributionToken`) so conversions can be attributed.

**Auth:** public. The copy says the phone number is requested at confirmation; see §C.4.

---

## B. Booking wizard detail (`s_c_booking` 1439–1598; logic 3843–3998)

### B.0 Entry points and initial state
- **Entry points:**
  - shop page "احجز الآن" (1360)
  - professional profile "احجز مع فيصل" (1415)
  - QR landing "احجز الآن" (2078)
  - Implied, not wired: per-service "احجز" on the shop page (1293), professional time chips (1391–1393), favorites professional "احجز" (1955), past-appointment "إعادة الحجز", and the "احجز موعدك القادم" rebook card (1706–1710)
- **Prototype initial state** (3436): `bkStep:1, bkService:'srv1', bkPro:'p2', bkDate:18, bkTime:'٥:٣٠ م', bkConfirmed:false`. Every step therefore has a preselection, and **Next is never disabled**. The prototype has no validation.
- **Rebuild rules:**
  - Preselect only what the entry point supplies (`service`, `pro`).
  - Default the professional to **«أي حلاق متاح»**, per rule 2 at 4001.
  - Disable Next until the current step has a valid choice.
  - Selections persist when going back. The prototype does this because state is kept.

### B.1 Chrome shared by all steps
- **Header** (1443–1460):
  - back button: `bkBack` goes to the previous step. On step 1 it returns to `c-shop` (3990).
  - title `bkTitle` (3976)
  - subtitle "صالون الأصالة · حي الملقا" (shop · district; hardcoded, 1449)
  - step pill `bkStepLabel` "خطوة N من ٥" (Step N of 5)
  - a 3px progress bar at 20/40/60/80/100% (1453–1459)
- **Footer** (1576–1582): a summary line `bkFooterNote` (ellipsized) + price `bkFooterPrice` = "{price} ر.س" + a full-width CTA `bkCta` = "التالي" (Next) on steps 1–4 and **"تأكيد الحجز"** (Confirm booking) on step 5 (3979).
- **Footer summary by step** (3984):

  | Step | Title (`bkTitle`) | Footer note |
  |---|---|---|
  | 1 | اختر الخدمة (Choose the service) | `{service}` |
  | 2 | اختر الحلاق (Choose the barber) | `{service} · {pro}` |
  | 3 | اختر التاريخ (Choose the date) | `{service} · {pro}` |
  | 4 | اختر الوقت (Choose the time) | `{dateLabel} · {pro}` |
  | 5 | مراجعة الحجز (Review booking) | `{dateLabel} · {time}` |

- **Component-library states** that apply to the confirm CTA (3620–3628):
  - Loading: "... جارٍ التأكيد" (Confirming…)
  - Error: "تعذّر الحجز" (Booking failed; red)
  - Disabled: "غير متاح"

### B.2 Step 1 — Service (1464–1480; `SRV` 3843–3849, `bkServices` 3866–3873)
Each service is a radio-card button (`optBase`: selected = 1.5px #6D9BCB border + #F8FBFE fill + 3px ring) with:
- a radio dot
- name and description
- price (number only) and duration label

| id | Name | Description | Price (SAR) | Duration (min) |
|---|---|---|---|---|
| srv1 | حلاقة شعر (Haircut) | قص وتصفيف مع غسيل (cut & style with wash) | 60 | 30 |
| srv2 | تهذيب لحية (Beard trim) | تشكيل وترطيب اللحية (shape & moisturize) | 35 | 20 |
| srv3 | باقة شعر ولحية (Hair + beard package) | قص وتصفيف + تهذيب لحية | 85 | 50 |
| srv4 | حلاقة أطفال (Kids' haircut) | للأعمار حتى ١٢ سنة (up to age 12) | 45 | 25 |
| srv5 | عناية بالوجه (Facial care) | تنظيف عميق وماسك مرطب | 70 | 40 |

- Single select; **one service per booking** (assumption #7). The package is a normal option with a combined duration.
- srv5 appears here but not in the shop page's service list (4 items, 3757). The wizard and the shop page must both read the same shop services endpoint.
- Package vs service is not visually distinguished.

### B.3 Step 2 — Professional (1482–1495; `PRO` 3850–3855, `bkPros` 3874–3888)
Each professional is a radio-card with:
- a 42px initial avatar (navy when selected)
- name and subline
- a status tag: green when free, grey when unavailable

| id | Name | Subline | Avatar | Tag | Selectable |
|---|---|---|---|---|---|
| any | **أي حلاق متاح** (Any available barber) | نختار لك الأقرب وقتاً (we pick the earliest for you) | ★ | أوقات أكثر (More times) | yes |
| p1 | فيصل القحطاني | تدريج وفيد · ★ 4.9 | ف | متاح ٤:٠٠ م (Available 4:00 PM) | yes |
| p2 | سلطان الحربي | لحية وعناية · ★ 4.8 | س | متاح ٦:٣٠ م | yes (**prototype default**) |
| p3 | راكان المطيري | حلاقة كلاسيك · ★ 4.7 | ر | **في إجازة** (On leave) | **no** — the card is at 60% opacity; tapping it shows the toast **"راكان في إجازة حتى ٢٤ سبتمبر"** (Rakan is on leave until 24 Sep) |

- Only professionals assigned to the chosen service should be listed (spec §10 `ProfessionalService`). The design does not filter by service.
- "Any available barber":
  - The availability engine must compute the **union** across eligible professionals.
  - `POST /bookings` must accept `professionalId = null` and **assign one server-side** at commit, inside the same transaction and collision check.
  - The confirmation, appointments, WhatsApp and review screens must then show the assigned professional. The prototype would print "أي حلاق متاح" as the barber name (3962, 3968).
  - **The spec (§2, §12) does not mention this option. It is a design addition that needs sign-off.**

### B.4 Step 3 — Date (1497–1517; `DATES` 3890–3894, `bkDates` 3895–3911)
- **Header:** "سبتمبر ٢٠٢٦" (month and year) + "التوقيت بتوقيت الرياض" (Times are in Riyadh time).
- **Grid:** 4 columns, **12 days**. Each cell (min-height 76) shows the short day name, the date number, and a note (availability count or reason).
- **Cell states:**
  - `ok`: white.
  - selected: navy fill with white number.
  - `off`: grey #F1F4F7, number struck through, note in grey. It stays tappable and shows a reason toast.

| Date | Day label | Note | State | Toast on tap |
|---|---|---|---|---|
| 15 | إثن | اليوم (Today) | ok | — |
| 16 | ثلا | ٩ أوقات | ok | — |
| 17 | أرب | ١٢ وقت | ok | — |
| 18 | خمي | ٦ أوقات | ok (default) | — |
| 19 | جمع | ٤ أوقات | ok | — |
| 20 | سبت | **مغلق** (Closed) | off | **"المحل مغلق يوم السبت"** (The shop is closed on Saturday) |
| 21 | أحد | ١١ وقت | ok | — |
| 22 | إثن | ١٤ وقت | ok | — |
| 23 | ثلا | **إجازة الحلاق** (Barber's leave) | off | **"الحلاق في إجازة هذا اليوم"** (The barber is on leave this day) |
| 24 | أرب | ١٠ أوقات | ok | — |
| 25 | خمي | ٨ أوقات | ok | — |
| 26 | جمع | ٥ أوقات | ok | — |

- **Info callout** (1512–1515): "الأيام المشطوبة مغلقة لدى المحل أو الحلاق المختار. اختر «أي حلاق متاح» لرؤية أيام أكثر." (Struck-out days are closed for the shop or the chosen barber. Choose "any available barber" to see more days.)
- **In the prototype the day list is static.** It does not change with the chosen service or professional; for example, day 23 is "barber leave" even when "any" is chosen.
- The journey panel says the date strip covers **14 days** (3557), but the prototype shows 12. The horizon should come from a configurable setting (spec §11 "booking horizon").
- **Weekday/date pairs are wrong for 2026.** In 2026, 15 Sep is a Tuesday and 18 Sep is a Friday. The labels (15 = Monday, 18 = Thursday) match September 2025. Do not reuse these as fixtures; generate them from real dates in `Asia/Riyadh`.

### B.5 Step 4 — Time (1519–1543; `mkSlot` 3913–3927, `bkPeriods` 3928–3941)
- **Duration banner** (blue, clock icon): `bkDurationNote` = "مدة {service}: {durLabel} — نعرض الأوقات التي تتسع لها فقط" (Duration of {service}: {duration} — we only show times that fit it).
- **Periods:** 3 sections, each with a label, a secondary count or break text on the end side, and a 3-column slot grid. Slot buttons are 44px high.
  - `ok`: white.
  - selected: navy.
  - `off`: grey, struck through, still tappable, shows a **reason toast**.
  - Only `ok` slots can become selected (3914).

| Period | Header note | Slot | State | Reason (toast) |
|---|---|---|---|---|
| صباحاً (Morning) | ٣ أوقات (3 times — **4 are actually ok**) | ٩:٣٠ ص | ok | |
| | | ١٠:٠٥ ص | off | **محجوز مع عميل آخر — جرّب ١٠:٤٥ ص** (Booked by another customer — try 10:45 AM) |
| | | ١٠:٤٥ ص | ok | |
| | | ١١:٣٠ ص | ok | |
| | | ١٢:٠٠ م | off | **لا يتسع لمدة {durLabel} قبل الاستراحة** (Doesn't fit {duration} before the break). This is the only dynamic reason; it changes with the service. |
| | | ١٢:٤٠ م | ok | |
| ظهراً (Afternoon) | **استراحة ٣:٣٠ — ٤:٠٠** (Break 3:30–4:00; the header shows the break instead of a count) | ١:٠٠ م | off | **داخل استراحة الغداء ١:٠٠ — ١:٤٥** (Within the lunch break 1:00–1:45) |
| | | ٢:١٥ م | ok | |
| | | ٢:٤٥ م | ok | |
| | | ٣:٣٠ م | off | **داخل استراحة صلاة العصر** (Within the Asr prayer break) |
| | | ٤:٠٠ م | ok | |
| | | ٤:٣٠ م | ok | |
| مساءً (Evening) | ٥ أوقات | ٥:٠٠ م | ok | |
| | | ٥:٣٠ م | ok (**default selected**) | |
| | | ٦:٠٥ م | off | **محجوز مع حلاق آخر — جرّب ٦:٣٠ م** (Booked with another barber — try 6:30 PM) |
| | | ٦:٣٠ م | ok | |
| | | ٧:١٥ م | ok | |
| | | ٨:٣٠ م | ok | |

- The annotation panel adds two more canonical reasons (1654–1657): **"لا يتسع لمدة ٥٠ دقيقة قبل إغلاق المحل"** (doesn't fit before closing) and **"الحلاق في إجازة هذا اليوم"** (barber on leave).
- **Irregular times** (١٠:٠٥, ٦:٠٥, ١٢:٤٠, ٧:١٥) confirm a **5-minute slot step** (assumption #2; spec §11 "8:05, 8:10").
- **Footnote** (1538–1541, eye-off icon): "لا تُعرض الأوقات التي مضت أو التي لا تتسع لمدة خدمتك كاملة. اضغط وقتاً معطلاً لمعرفة السبب." (Past times and times that can't fit your full service are not shown. Tap a disabled time to see why.) The first sentence says those slots are hidden; the grid shows other unavailable slots as disabled with reasons.
- **Reason codes the design needs** (proposal):

  | Code | Arabic copy | Params |
  |---|---|---|
  | `BookedOtherCustomer` | محجوز مع عميل آخر — جرّب {suggested} | suggestedStartAt |
  | `BookedWithOtherProfessional` | محجوز مع حلاق آخر — جرّب {suggested} | suggestedStartAt. This fires when the time is booked for the chosen professional. The copy "with another barber" is ambiguous and needs a copy review. |
  | `InBreak` | داخل استراحة {breakLabel} {start} — {end} | breakLabel (الغداء / صلاة العصر / custom), start, end |
  | `DoesNotFitBeforeBreak` | لا يتسع لمدة {duration} قبل الاستراحة | durationMinutes |
  | `DoesNotFitBeforeClose` | لا يتسع لمدة {duration} قبل إغلاق المحل | durationMinutes |
  | `ProfessionalOnLeave` (day or professional) | الحلاق في إجازة هذا اليوم / {name} في إجازة حتى {date} | name, untilDate |
  | `ShopClosed` (day) | المحل مغلق يوم {weekday} | weekday |
  | `Past` / `LeadTime` | (hidden per footnote) | — |

- Empty day (4615): "اليوم ممتلئ بالكامل — كل أوقات الخميس محجوزة لدى فيصل. أقرب وقت متاح: الجمعة ١٠:٠٠ ص." with CTA "انتقل إلى الجمعة" (jump to the next available day). The API must return `nextAvailableDate` when a day is full.
- Load error (4616): "تعذّر تحميل الأوقات — لم يُحجز أي وقت. تحقق من الاتصال ثم أعد المحاولة." with CTA "إعادة المحاولة" (Retry).
- **In the prototype the grid is static.** It does not change with service, professional or date. The "جرّب المسار" copy (1641) claims the grid changes, but only `bkDurationNote`, the one reason string and the end time actually change.

### B.6 Step 5 — Review (1545–1573; `bkSummary` 3966–3972)
1. **Shop header row:** logo "أ", "صالون الأصالة للحلاقة", "طريق أنس بن مالك، الملقا".
2. **Summary rows** (icon · key · value):
   - scissors · **الخدمة** (Service) · {service name}
   - user · **الحلاق** (Barber) · {pro name}
   - calendar · **التاريخ** (Date) · `dateLabel` = "{full weekday} {day} سبتمبر", e.g. "الخميس 18 سبتمبر". The day number comes out in Latin digits because it is a JS number.
   - clock · **الوقت** (Time) · "{start} — {end}", e.g. "٥:٣٠ م — ٦:٢٠ م"
   - info · **المدة** (Duration) · {durLabel}
3. **Total row:** **"الإجمالي (يُدفع في المحل)"** (Total, paid at the shop) · `bkTotal` = service price, in SAR. This is informational only and has no payment UI; it complies with spec §2/§12.
4. **"ملاحظة للحلاق (اختياري)"** (Note to the barber, optional): a textarea with placeholder "مثال: تدريج قصير من الجانبين" [static]. → `customerNote` (max length TBD, e.g. 300). It is visible to the shop and the professional, so it must be sanitized.
5. **Green WhatsApp callout:** "سيصلك تأكيد على واتساب فوراً، وتذكير قبل الموعد بثلاث ساعات. الإلغاء مجاني حتى ساعتين قبل الموعد." (You'll get a WhatsApp confirmation right away and a reminder 3 hours before. Free cancellation up to 2 hours before.) → **reminder offset conflict, §E.**
6. Footer CTA **تأكيد الحجز** → `bkNext` sets `bkConfirmed = true`.

- No auth, phone or OTP UI appears in this step, although the QR copy says the phone is requested "at the confirmation step" (2059, 2079). See §C.4.
- No terms or policy acknowledgement is required at confirmation.

### B.7 Confirmation (success overlay 1584–1596)
- A full-screen white overlay (z-index 4) that replaces the wizard. It contains:
  - An 88px green check.
  - H2 **"تم تأكيد حجزك"** (Your booking is confirmed).
  - "أرسلنا تفاصيل الموعد إلى واتساب. يمكنك إدارة الموعد من «مواعيدي» في أي وقت." (We sent the details to WhatsApp. Manage it from "My appointments" anytime.)
- Summary box:
  - الخدمة `bkServiceName`
  - الحلاق `bkProName`
  - الموعد (Appointment) `bkWhen` = "{dateLabel} · {time}"
  - رقم الحجز (Booking number) **TR-48219** (hardcoded)
- Buttons:
  - primary **عرض موعدي** (View my appointment) → `bkGoAppts`: resets the wizard and goes to My appointments. The rebuild should open the booking's detail page.
  - ghost **حجز موعد آخر** (Book another) → `bkReset`: returns to step 1.
- **Status conflict:** the headline says "confirmed", but the lifecycle starts at `Pending` and the shop confirms it (3575–3576, sample at 4024). If the booking is created as Pending, the success copy must say something like "تم إرسال طلب الحجز" (booking request sent) or the platform must auto-confirm. **Decision needed.**

### B.8 Duration and end-time computation
- Duration = `service.durationMinutes`, a single service per booking; there is no buffer or preparation time in the prototype. The availability-rules panel (3565) mentions "مدة الخدمة + وقت التجهيز" (service duration + preparation time), which implies a per-service **buffer/prep minutes** field.
- End time = start + duration. The prototype does this with `addMin(label, mins)` (3943–3955) on **Arabic-Indic 12-hour label strings**. The function is buggy:
  - It flips AM/PM only when `h > 12`. So "١١:٣٠ ص" + 50 min → "١٢:٢٠ ص" (should be ١٢:٢٠ م).
  - "١٢:٤٠ م" + 50 → "١:٣٠ ص" (should be ١:٣٠ م).
- **Rebuild:**
  - The server computes `endAt = startAt + duration (+ buffer)` on UTC instants. It returns both `startAt` and `endAt` in every slot and booking DTO.
  - The client only formats them, with `Intl.DateTimeFormat(locale, { timeZone: 'Asia/Riyadh', hour: 'numeric', minute: '2-digit' })`.
  - The price and duration shown at review and after booking are the **snapshot** stored on the booking (spec §10).

### B.9 Step order vs spec
- **Spec:** `shop → service → professional → date → time → review → confirmation` (§2).
- **Design:** (shop page) → 1 service → 2 professional → 3 date → 4 time → 5 review → success overlay. **The order is the same.**
- Differences and nuances:
  1. The step counter counts only the 5 in-wizard steps. The sitemap node says "مسار الحجز — ٦ خطوات" (3528), and the journey panel lists 7 stages including discovery and the shop (3552–3560). Treat the 5-step wizard plus the success screen as canonical.
  2. The professional step adds **"أي حلاق متاح"** (not in spec).
  3. Entry from the professional profile ("احجز مع فيصل", time chips) or favorites fixes the professional **before** the service. The wizard should still show service first, with the professional preselected on step 2. If the preselected professional does not offer the chosen service, show a warning and clear the selection. A time chipped on the profile must be re-validated after the service is chosen, because the slot depends on the service duration.
  4. The spec's "service/package" wording (§12) implies packages are selectable. The design merges packages into the service list (srv3).
  5. **Where authentication happens is not drawn.** See §C.4.

### B.10 Wizard API and DTOs
- `GET /api/v1/shops/{shopId}/services?bookableOnline=true` → `[{ id, name, description, price, currency, durationMinutes, bufferMinutes?, isPackage, packageItems?: [{ serviceId, name }], displayOrder }]`
- `GET /api/v1/shops/{shopId}/services/{serviceId}/professionals?from=` → `{ anyOption: { nextAvailableAt }, items: [{ id, displayName, initial, specialty, rating, photoUrl, status: "Available"|"OnLeave"|"Unavailable", nextAvailableAt?, unavailableUntil?, reasonCode? }] }`
- `GET /api/v1/availability/days?shopId=&serviceId=&professionalId=(omit = any)&from=YYYY-MM-DD&days=` → `{ timeZone: "Asia/Riyadh", horizonDays, days: [{ date, status: "Available"|"ShopClosed"|"ProfessionalOff"|"FullyBooked"|"Past", availableCount, reasonCode?, reasonParams? }] }`
- `GET /api/v1/availability/slots?shopId=&serviceId=&professionalId=&date=&includeUnavailable=true|false` → `{ date, timeZone, durationMinutes, slotStepMinutes, nextAvailableDate?, periods: [{ key: "Morning"|"Afternoon"|"Evening", availableCount, breaks: [{ startAt, endAt, kind, label }], slots: [{ startAt, endAt, status: "Available"|"Unavailable", reasonCode?, reasonParams?, suggestedStartAt? }] }] }`
  - Spec §11 says only bookable slots are returned. `includeUnavailable=true` is the proposal to support the design's disabled-with-reason slots. It needs a decision (§E).
  - Unavailable entries never create bookability; the server re-checks everything on create.
  - Period boundaries should be configurable. In the design the morning period ends at 12:40 PM, and the afternoon covers 1:00–4:30 PM.
- `POST /api/v1/bookings` with header `Idempotency-Key` and body `{ shopId, serviceId, professionalId: uuid|null, startAt, customerNote?, qrAttributionToken? }`.
  - Success → `201 BookingDetailDto { id, reference, status, startAt, endAt, shop, professional (assigned), serviceSnapshot { name, price, currency, durationMinutes }, customerNote }`.
  - Errors:
    - `401` when there is no session; the client runs the OTP flow and retries with the same key.
    - `409 slot_unavailable { alternatives: instant[] }` when another request wins the slot. The UI must return to step 4 with a toast, a case the design does not draw.
    - `422` validation.
    - `423 shop_bookings_paused`.
  - Side effects: outbox → WhatsApp customer confirmation, professional alert (never the customer phone), scheduled reminders, SignalR shop event, and QR attribution.

**Auth:** browsing steps 1–4 are public; `POST /bookings` requires a customer session.

---

## C. Auth flow detail (as designed)

### C.1 Identity model shown
- **Phone number + one-time code only.** There is no password anywhere in the customer design. Evidence:
  - The sign-up frame has only a phone field and a terms checkbox (918–941).
  - The OTP frame has 4 digits (943–970).
  - The design assumption #1 states it explicitly: **"الحساب برقم الجوال + رمز OTP — لا كلمات مرور في النسخة الأولى؛ نفس الرقم المستخدم في رسائل واتساب."** (Account = mobile number + OTP; **no passwords in v1**; the same number receives WhatsApp messages; 3501.)
- **OTP channel:** **WhatsApp**, with an **SMS fallback** mentioned in the help text only ("أو اطلب الرمز عبر رسالة نصية", 963). There is no SMS button.
- **Code length:** **4 digits** (950–955). Entry is LTR.
- **Resend:** a countdown "إعادة الإرسال بعد 00:24" (the design shows 24 s remaining; a 30 s cooldown is plausible) and a disabled "إعادة إرسال" link.
- **Attempts:** "الرمز غير صحيح — تبقى محاولتان" (component library, 415). This implies about 3 attempts per code, then a lockout or a new code.
- **Phone format:** the `+966` prefix is fixed (no country picker drawn) and the national part has 9 digits (validation copy at 3225). Normalize to E.164.
- **Terms:** a required checkbox with links to "شروط الاستخدام" (Terms of Use) and "سياسة الخصوصية" (Privacy Policy). The two legal pages are not designed.

### C.2 Screens and steps
1. **Sign up / request code** (918–941) → "إرسال الرمز".
2. **Sign in:** only a link "لديك حساب؟ سجّل الدخول" (937) and the landing button "تسجيل الدخول" (833). **No separate sign-in screen is drawn.** With phone-OTP, sign-in is identical to sign-up (phone → code). The API should be a single "request code" call; `isNewUser` in the verify response decides whether profile completion follows.
3. **OTP verification** (943–970) → "تحقق".
4. **Profile completion (name):** **not drawn.** A name exists in the profile (2003), the component library (401, "الاسم الكامل") and the validation demo (3228). Reviews show the customer's first name (1911). A name step after the first verification is required but undesigned.
5. **Location permission** (972–987):
   - "السماح بالوصول للموقع" → browser geolocation.
   - "أدخل الحي يدوياً" → manual district. **The manual district picker is not drawn.** The Home header's "حي الملقا، الرياض ▾" (1003) implies a location sheet.
   - The Profile setting "الموقع: مفعّل" (4137) toggles the preference.
6. **Sign out:** "تسجيل الخروج" (2028).
7. **Guest booking via QR:** "اختر خدمتك ثم أدخل رقم جوالك لإتمام الحجز" (2059) and "يُطلب رقم الجوال في خطوة التأكيد فقط" (2079). This means an **inline phone + OTP step at booking confirmation** for unauthenticated users. It is **not drawn** in the wizard.

### C.3 What the design does NOT show (identity-relevant)
- password creation or entry
- **forgot / reset password**
- email capture or verification
- **session list / revoke all sessions / security settings**
- change phone number (re-verification)
- delete account
- lockout messaging beyond "attempts remaining"
- CAPTCHA or abuse throttling UI
- explicit expired-session handling (the spec requires it; the design has no customer screen for it)

### C.4 Reconciliation needed with spec §9 (decision for the identity design)
- **Spec §9:**
  - Customers sign in "using the flow shown in the imported design, with a verified mobile number available for WhatsApp messages".
  - "support … password reset, email/mobile verification as applicable … secure password hashing through ASP.NET Core Identity".
  - §12 lists "Sign up, sign in, verification, forgot/reset password".
- **The design is passwordless phone OTP.** Options:
  - (a) **Passwordless OTP for customers** via ASP.NET Core Identity with a phone-number user store and a custom `PhoneNumberTokenProvider`/TOTP token provider (4-digit, WhatsApp delivery, SMS fallback). Forgot/reset password becomes N/A for customers. It remains for shop and admin accounts, which use email + password.
  - (b) Add a password to customer sign-up, which means undesigned screens: set password, sign in with password, forgot/reset.
  - The design points to (a). **Record this as an ADR.**
- A 4-digit code is weak. Enforce a short expiry (≤5 min), ≤3 attempts per code, per-phone and per-IP rate limits (spec §18), and lockout.
- The QR "phone at confirmation" flow means the booking wizard's review step needs an inline auth sub-step (phone → OTP → optional name) before `POST /bookings`. The alternative is a redirect to `/auth/sign-up?returnTo=…` with the wizard state kept in the URL. The design copy implies the inline version.

---

## D. Features the spec conditions on the design — presence check

| Feature | Present? | Where (lines) | Notes |
|---|---|---|---|
| **Favorites** | **Yes** | Heart on shop cover (1253), heart on pro profile (1373), Favorites screen (1928–1965), bottom-nav tab "المفضلة" (3740), profile stat "محل مفضل" (2010), notification "خدمة جديدة في محلك المفضل" (4118) | Both shops **and** professionals can be favorited → in MVP scope (spec §12: "only if present"). |
| **Notifications (in-app)** | **Yes** | Bell with unread dot (1005), Notifications screen (1967–1993), "تعليم الكل كمقروء" | 5 event types (A9.2). The WhatsApp preference is in settings (4136). |
| **Packages** | **Partially** | Only as a service row "باقة شعر ولحية" (3759, 3846, 1400) | Modeled as one service with combined duration (assumption #7, 3507). No packages section, badge, or item breakdown on the shop page. Spec §10 requires package items for reporting. |
| **Gallery** | **No** (single cover only) | Shop cover 180px (1247), QR cover 150px (2046) | Spec §12 lists a gallery. The component library section "رمز QR والصور" (654–672) has only a **single cover-image** drop zone ("اسحب صورة الغلاف هنا", JPG/PNG · 1600×900 · up to 4MB) plus an avatar/logo upload. That upload belongs to the shop/admin side. No multi-image gallery, carousel or lightbox exists anywhere in the design. |
| **Shop map on shop page** | **No** | Address row + "الاتجاهات" link only (1271–1275) | Spec §12 lists "map". Needs a static mini-map or embed that is not in the design. |
| **Map / list toggle** | **Yes** | Segmented "قائمة / خريطة" (1136–1139); "عرض الخريطة" floating pill on results (1114–1116); "الخريطة" link on Home (1020) | Price pins, user dot, selected-shop bottom sheet. |
| **Filters** | **Yes** | Chips on results (1083–1087); filter drawer (1171–1215) | See the filter table below. |
| **Sorting** | Yes | Drawer radio (1177–1188) + "الأقرب" chip | الأقرب مسافةً (distance), الأعلى تقييماً (rating), أقرب وقت متاح (earliest availability). |
| **Search** | Yes | Landing (842–847), Home (1007), Results (1081), Map "ابحث في هذه المنطقة" (1133) | Free text "service or shop name" + location. |
| **Professional profile within a shop** | Yes | 1365–1418 | Includes stats, today's slots, services, latest review. |
| **Reviews** | Yes | Shop tab (1313–1339), pro profile (1404–1412), rate screen (1875–1920) | Tags + comment + stars; first-name display rule. |
| **Reschedule** | Yes | 1843–1873; entry buttons at 1702 and 1784 | Same service and professional; the old slot is held until the new one is confirmed. |
| **Cancellation** | Yes | 1809–1841, policy at 1781 | Reason chips; "cancellation request" after the cutoff is copy-only. |
| **Add to calendar** | Yes | 1760 | → `.ics` endpoint. |
| **Language switch** | Yes | Landing "EN" (832); Profile "اللغة: العربية" (4135) | No English-mode screens are drawn. |
| **QR landing** | Yes (shop only) | 2043–2082 | Badge "دخلت عبر رمز المحل", intro callout, today's first slots, services, CTA, and the "phone at confirmation" note. The professional QR is described (2101) but not drawn. |
| **Location permission / manual** | Permission yes; manual **not drawn** | 972–987 | |
| **Share** | No | — | The QR icon on the shop cover (1252) may mean share/show the QR; the behaviour is undefined. |

**Filters, exactly as designed:**

| Filter | Control | Default in mock | Spec §12 item |
|---|---|---|---|
| Sort: distance / rating / earliest available | radio cards (drawer), "الأقرب" chip | distance | "distance sorting" ✔ |
| Service | chips: تهذيب لحية، حلاقة شعر، حلاقة أطفال، عناية بالوجه; Home category chips الكل، حلاقة، لحية، أطفال | تهذيب لحية | service ✔ |
| Price range | dual slider "25 — 90 ر.س" | 25–90 | (not in spec list; design addition) |
| Open now only | switch + "مفتوح الآن" chip | on | open-now ✔ |
| Verified shops only | switch | off | (design addition) |
| Accepts booking today | switch | on | ≈ date/availability (today only) — partial |
| Rating (minimum) | **absent** | — | spec lists rating → only available as a sort |
| Distance radius | **absent as a control** (text "ضمن ٥ كم" 1090; empty-state CTA "توسيع النطاق إلى ١٥ كم" 4613) | 5 km | spec lists distance → partial |
| Specific date / availability on a chosen date | **absent** | — | spec lists date & availability "where supported by the design" → only "today" |

---

## E. Deviations / conflicts with the spec (with line numbers)

### E.1 Payment / checkout / price-paying UI
- **No checkout, card entry or online payment anywhere in the customer flow.** ✔ Compliant with §2/§12.
- Price display is informational: "الإجمالي (يُدفع في المحل)" (1560), "الإجمالي" (1775–1776), policy "الدفع في المحل — نقداً أو شبكة" (1352), WhatsApp "المبلغ: 85 ر.س" (1620). These are acceptable. Keep price as a snapshot display only.
- **Flag:** Profile setting **"طريقة الدفع — في المحل"** (Payment method — at the shop; 4141, card icon). This is a payment-settings entry point. For v1, render it as a read-only info row ("الدفع في المحل") or remove it. Do not build a payment-method management screen (spec §2 allows only a future-facing abstraction).
- The `card` icon is also used for the pay-at-shop policy (1352). Fine.

### E.2 Phone numbers
- The customer's **own** number is shown on the sign-up (929), OTP (950) and profile (2004) screens. ✔ Acceptable, since it is the owner's data.
- **No professional phone or WhatsApp number** appears on any customer screen (pro profile 1365–1418, shop barbers tab 1298–1312, wizard 3850–3855, WhatsApp mock 1600–1636). ✔ Compliant with §8.
- The WhatsApp mock is a customer message and contains no professional number. ✔
- Not a customer-screen issue, but related: the reviews and cancel copy never expose customer phones to shops. ✔ (The shop side is covered elsewhere; `stateCards` 4617 shows "رقم العميل غير متاح لحسابك", which is consistent.)

### E.3 Conflicts requiring a decision
1. **Auth model:** passwordless phone + 4-digit WhatsApp OTP (3501, 918–970), vs spec §9/§12 password security and forgot/reset password. See §C.4.
2. **Reminder offset:** the design says **3 hours** before ("تذكير قبل الموعد بثلاث ساعات" 1570; "موعدك بعد ٣ ساعات" 1630; lifecycle "تذكير تلقائي قبل ٣ ساعات" 3576), and the notifications list shows a **day-before** reminder ("تذكير بموعدك غداً" 4115). **Spec §16: 30 minutes** before, for both customer and professional. Use the spec value, and make the offset an admin platform setting (§14 "reminder offset"). Update the review-step copy to read the configured value.
3. **Pending vs confirmed:** the success screen says "تم تأكيد حجزك" (1586) and "تم تأكيد موعدك" (landing card 859, WhatsApp 1614). But the lifecycle (3575–3576) and the sample booking "بانتظار التأكيد" (4024) show a manual shop confirmation. Spec §11 starts at `Pending → Confirmed`. Decide whether online bookings are auto-confirmed (a per-shop setting?) and align the copy and the WhatsApp template (confirmation vs "request received").
4. **"Cancellation request" after cutoff:** "بعد ذلك يتحول الطلب إلى «طلب إلغاء» يراجعه المحل" (1781, 3503). This state or entity is **not in the spec's state machine** (§11). It needs either a `CancellationRequest` sub-entity with shop approve/deny, or a simpler "cannot cancel online after the cutoff" rule. The "طلب إلغاء" UI is not drawn.
5. **Unavailable slots shown vs hidden:**
   - The design shows unavailable slots, disabled with reason toasts (3930–3939; rule "المعطّل يبقى مرئياً" 4002).
   - The design's own footnote says past or non-fitting slots are not shown (1540).
   - Spec §11: "The API returns only genuinely bookable slots."
   - Proposal: return bookable slots as `Available` and, optionally, `Unavailable` entries with a reason code for display only. Hide past and lead-time slots. **Needs sign-off.**
6. **"أي حلاق متاح" (any professional)** (3851; rule 4001): not in the spec. It requires server-side assignment in the booking transaction.
7. **Packages** are modeled as a single service with a combined duration (3507, 3846). Spec §10 requires packages to have a duration and price **and expand into configured service items** for reporting. The shop page has no packages section (spec §12 lists "packages").
8. **Global service prices on Home:** "خدمات شائعة" tiles show price and duration with no shop (1042–1050, data 3757). This implies a global price per service, which contradicts spec §10 ("no global price or global duration"). Render these tiles as category shortcuts, optionally with "from X ر.س" aggregated across nearby shops.
9. **Reviewer name display:**
   - The shop reviews (3821–3823) and the pro-profile review (1408) show **full names** (e.g. "محمد العنزي").
   - The rate screen promises "يظهر تقييمك باسمك الأول فقط" (1911).
   - Follow the promise: `authorDisplayName` = first name + surname initial, or first name only.
10. **Review attribution:** the design attributes the review to both the professional and the shop (3504), with a 7-day window (3504, 3578, 4046). The spec only says "once, by the booking's customer, after completion" (§7, §17). The 7-day window is an additional rule; adopt it and make it configurable.
11. **Discovery hiding:** paused bookings **hide the shop from discovery** (3789, 3569). The spec says "paused-booking state" blocks availability; hiding the whole shop is stricter. Confirm the intended behaviour (hide vs show with "الحجز متوقف مؤقتاً").
12. **Cancellation / lateness policies** are presented as fixed text (1350–1351, 1570, 1781): free cancellation 2 h before, lateness over 15 min may cancel. These must come from platform/shop booking-policy settings (§14 "booking policy"), not from hardcoded copy.
13. **WhatsApp template placeholders:** the design's confirmation uses duration, amount and address (1620–1621), and has URL buttons "الاتجاهات" and "إدارة الموعد" (1624–1625). Extend the §16 whitelist and support template buttons.

### E.4 Missing from the design
Each item carries one label:
- **[SPEC]**: absent from design, spec requires it (the section is cited).
- **[GAP]**: a design gap that the spec does not mandate.
- **[DECISION]**: needed or not depending on a pending decision (§C.4 or §E.3).

- **[DECISION §C.4 / §E.3 #1]** Forgot/reset password (§9, §12). Required if customers get passwords; N/A under the passwordless ADR (where it stays required for shop/admin accounts, outside customer scope).
- **[SPEC §12]** Sign-in screen as distinct from sign-up. Only a link is drawn (937). Under phone OTP it can reuse the sign-up UI with a different heading.
- **[SPEC §9]** Profile-completion step (name) after the first OTP (§9 "self-register"). A name is displayed (2003) but never captured.
- **[SPEC §12]** Manual location selection screen ("manual location selection"). Only the button is drawn (984).
- **[SPEC §9, §12]** Security/session settings: session list, sign out other devices, revoke all sessions.
- **[GAP]** Change mobile number with re-verification. Delete account / data request (the "الخصوصية والبيانات" row at 4142 has no screen).
- **[SPEC §12]** Shop gallery.
- **[SPEC §12]** Map on the shop page.
- **[SPEC §12]** Shop description text.
- **[SPEC §12, §10]** Packages section on the shop page. The item expansion is **[DECISION §E.3 #7]**.
- **[GAP]** Filters for minimum rating, distance radius and a specific date. §12 asks for them only "where supported by the design", and the design supports only rating as a sort, a fixed 5 km radius and "today".
- **[SPEC §11]** Booking-conflict UI at confirm, when another request won the slot and the API returns a typed conflict response. Not drawn.
- **[DECISION §E.3 #4]** The "cancellation request" UI after the cutoff. It exists as copy only (1781).
- **[SPEC §5]** Expired-session and permission-denied states for customer pages. Only the generic state cards exist (4612–4619).
- **[SPEC §6]** English (LTR) versions of the screens. Only "EN" (832) and the "اللغة" setting (4135) are drawn.
- **[SPEC §5]** Desktop/tablet layouts (390/768/1440) of every customer screen except the landing page.
- **[SPEC §17]** Professional QR landing variant ("unique QR destinations for … professionals"). Described at 2101; not drawn.
- **[GAP]** Footer, legal pages (terms, privacy — linked from 933) and FAQ/contact pages (rows at 4145–4146).
- **[GAP]** Empty state for favorites. Notifications pagination and empty state.
- **[GAP]** Landing nav targets. "المحلات" (829) → proposed `/[locale]/search`; "عرض الكل" (881) → `/[locale]/search?sort=rating`. "للأعمال" and "عن تريمي" (829) and "انضم كشريك" (910) have no designed pages.
- **[SPEC §6] / open question** An indexable public shop listing. `/search` is noindex because it is personalized and param-driven, so the only indexable discovery page is the landing page. §6 wants public pages in the sitemap. Proposal (not designed): `/[locale]/shops` or `/[locale]/cities/[city]`, a server-rendered listing sorted by rating, plus all shop and professional pages in `sitemap.xml`.

### E.5 Data and copy inconsistencies inside the prototype (do not replicate)
- The shop footer says "تبدأ من 30" (1359), but the cheapest service is 35 (3758).
- The results list reuses `s.from` with a hardcoded "تهذيب لحية … ٢٠ دقيقة" (1106).
- The Home shop card always shows the verified shield (1027), even for an unverified shop (`v:0` in 3733).
- The wizard has 5 services (3843–3849); the shop page lists 4 (3757–3760).
- The Favorites header says "٣ محلات · حلاقان" (1936), but the screen shows 3 shops and 1 professional.
- The morning period header says "٣ أوقات" but 4 slots are available (3929–3931). The afternoon header shows the break "٣:٣٠ — ٤:٠٠", while a separate lunch break 1:00–1:45 appears in the reasons (3934).
- The weekday/date pairs match 2025, not 2026 (3890–3894; "الخميس ١٨ سبتمبر" throughout, and "سبتمبر ٢٠٢٦" at 1500).
- `addMin` 12-hour AM/PM bug (3943–3955; see §B.8).
- The wizard's default professional is `p2` (3436), but the design rule says "any" is the default (4001).
- The date strip has 12 days (3890); the journey says 14 (3557). The sitemap says 6 steps (3528); the wizard has 5 (3977).
- Number systems are mixed for the same fields (0.1).
- The rating tags are auto-highlighted rather than user-selectable (4096–4103).
- The stars remain editable after submit.
- The Notifications bottom nav highlights Home (1988).
- `openCancel` on the details screen (1785) has no sheet on that screen; the sheet is drawn only in `s_c_rate` (1822–1836).
- `settingsGroups` labels use the Latin caption style (2016) with Arabic text.

---

## F. Out-of-scope findings for the parent (not customer screens, but they contradict the spec)
- The sitemap annotations for the shop role (3537, 3539) say "الخدمات المتاحة — تفعيل فقط" (services: activation only) and "تعديل الأسعار — ممنوع" (editing prices forbidden). Spec §7/§10/§13 says the shop **creates and edits its own services, prices and durations**.
- The admin sitemap (3544) has "نقل حلاق بين المحلات" (transfer barber between shops), and the admin nav label (3410) is "الحلاقون والنقل والخدمات". Spec §14 says "assign to one shop … **no transfer action**".
- `sitemap` (3545) has "الخدمات العامة — سعر · مدة" (global services with price and duration), a global price/duration catalogue. This contradicts spec §10.

