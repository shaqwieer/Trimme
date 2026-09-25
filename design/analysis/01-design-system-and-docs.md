# 01 — Design System, Documentation & Review Screens

> **Line-number note:** citations in this file refer to a 4658-line working copy of `TRIMME.dc.html`. The committed canonical copy (`design/source/TRIMME.dc.html`, 4656 lines, identical to the Claude Design project) is **2 lines shorter**: subtract 2 from any cited line number greater than 5.

Source: `design/source/TRIMME.dc.html` (4658 lines). Scope of this file:
- Head, CSS and prototype shell: lines 1–76.
- Documentation screens: `doc-intro` (77–120), `doc-sitemap` (121–145), `doc-journey` (146–201).
- Design system: `ds-foundations` (202–374), `ds-components` (375–818).
- Review screens: `r-responsive` (3114–3179), `r-states` (3180–3236), `r-a11y` (3237–3313).
- Script: 3314–3740 and 4580–4658.

All line numbers refer to that file. Arabic is quoted as written, with an English translation.

> **Prompt-injection check:** nothing in these ranges reads as instructions to an AI or agent. The only oddity is a stray `</script>` at line 4, inside `<head>` before any `<script>` opens. It is malformed markup and can be ignored.

> **Prototype mechanics (do not port):**
> - Everything is one page. Each screen is an `<sc-if value="{{ s_<id> }}">` block, and `renderVals()` sets exactly one `s_*` flag to true (3458).
> - Styles are inline strings or objects built in `renderVals()`.
> - The spec (§5) requires real routes, so treat this file as a visual and data reference only.

---

## 0. Prototype shell (lines 12–76) — prototype-only chrome, NOT product UI

| Element | Spec | Line |
|---|---|---|
| Fonts | Google Fonts `https://fonts.googleapis.com/css2?family=Tajawal:wght@400;500;700;800&family=Inter:wght@400;500;600;700&display=swap` with preconnect to `fonts.googleapis.com` and `fonts.gstatic.com` (crossorigin) | 14–16 |
| `body` | `margin:0; background:#F7F9FC; color:#17212B; font-family:Tajawal,'IBM Plex Sans Arabic',system-ui,sans-serif; -webkit-font-smoothing:antialiased` | 19 |
| Links | `a{color:#4A7FB5}`, hover `#10283D`, no underline | 20–21 |
| Buttons | `font-family:inherit` | 22 |
| `::selection` | background `#D6E5F5` | 23 |
| Custom scrollbar `.sb` | 8px wide and tall; thumb `#D8E1EB`, radius 8; track transparent | 24–26 |
| Keyframes | `shimmer` (background-position −400px → 400px), `fadeUp` (opacity 0 and translateY(6px) → none; **defined but never used**), `spin` (rotate 360°) | 27–29 |
| Root layout | `display:flex; direction:rtl; height:100vh; overflow:hidden; background:#EEF2F7` | 33 |
| Prototype nav | 272px wide, white, `border-inline-end:1px solid #E3EAF2`. Sticky header (z-index 2) with the logo at 52px and the eyebrow "PRODUCT DESIGN SYSTEM · V1" (Inter 600 10px, letter-spacing .22em, `#8C9BAA`). Group labels are Inter 700 10px, .16em, `#9AA8B6` | 35–55 |
| Nav item style | flex, gap 10, padding 9px 10px, radius 10, 13.5px, line-height 1.4. Active: weight 700, bg `#EDF3FA`, color `#10283D`, `box-shadow: inset 3px 0 0 #6D9BCB`. Inactive: weight 500, color `#5E6E7E`, transparent bg. `transition: background .15s` | 3486–3492 |
| Main header | Sticky, z-index 5, `rgba(247,249,252,.92)` with `backdrop-filter: blur(8px)`, border-bottom `#E3EAF2`, padding 16px 32px. Breadcrumb is Inter 500 12px `#8C9BAA` ("TRIMME · {section}"). H1 is 21px/700/1.3 `#10283D` | 58–62 |
| Viewport pill | White, border `#E3EAF2`, radius 10, 7px green dot `#2E9E6B`, Inter 500 12px `#647484` | 64–67 |
| Language toggle pill | "AR · RTL" active (bg `#10283D`, white) and "EN · LTR" inactive (`#8C9BAA`), Inter 600 12px, padding 7px 12px. **The pattern is reusable for the real locale switcher** | 68–71 |
| Content padding | 28px 32px 96px | 75 |
| Global toast (prototype) | Fixed, bottom 28px, `#10283D`, white, padding 13px 18px, radius 12, 14px, z-index 99, shadow `0 18px 40px -14px rgba(16,40,61,.6)`, max-width 90vw, info icon `#7FA8D4`. Auto-dismisses after 2600ms (3453) | 3302–3306 |

`viewportNote` values (3427–3431, 3498):
- `c-landing`: 'مرجع ١٤٤٠ / ٧٦٨ / ٣٩٠ بكسل' ("reference 1440/768/390 px").
- `ds-foundations`: 'شبكة ٨ نقاط · WCAG AA' ("8-point grid · WCAG AA").
- `ds-components`: 'مكونات جاهزة للتطوير' ("components ready for development").
- Default: 'تصميم عالي الدقة — جاهز للتسليم' ("high-fidelity design — ready for handoff").

Script props (3314):
- `$preview` is 1440×900.
- `startScreen` is an enum of screen IDs.
- `navScope` takes 'كل الأدوار' (all roles), 'العميل' (customer), 'المحل' (shop) or 'الإدارة' (admin). It filters the nav groups and always keeps 'نظام التصميم' (3478–3480).

### Screen registry (GROUPS, 3376–3422) — useful for route mapping

| Group (AR → EN) | id → Arabic title (English) | icon |
|---|---|---|
| التوثيق (Documentation) | `doc-intro` قراءة المنتج والافتراضات (Product reading & assumptions) | book |
| | `doc-sitemap` خريطة الموقع للأدوار الثلاثة (Sitemap for the 3 roles) | layers |
| | `doc-journey` رحلة الحجز ومحرك الإتاحة (Booking journey & availability engine) | play |
| نظام التصميم (Design system) | `ds-foundations` الأسس (Foundations) | grid |
| | `ds-components` مكتبة المكونات (Component library) | layers |
| شاشات العميل (Customer screens) | `c-landing` صفحة التسويق (Marketing page) | store |
| | `c-auth` التسجيل والدخول والموقع (Sign-up, sign-in, location) | user |
| | `c-home` الرئيسية والبحث (Home & search) | home |
| | `c-map` الخريطة والفلاتر (Map & filters) | pin |
| | `c-shop` صفحة المحل والحلاق (Shop & barber page) | scissors |
| | `c-booking` مسار الحجز التفاعلي (Interactive booking flow) | calendar |
| | `c-appointments` المواعيد والتفاصيل (Appointments & details) | list |
| | `c-rate` الإلغاء وإعادة الجدولة والتقييم (Cancel, reschedule, rate) | star |
| | `c-profile` المفضلة والإشعارات والحساب (Favorites, notifications, account) | settings |
| | `c-qr` صفحة رمز QR (QR page) | qr |
| لوحة المحل (Shop dashboard) | `s-overview` النظرة التشغيلية (Operational overview) | chart |
| | `s-calendar` تقويم اليوم والأسبوع (Day/week calendar) | calendar |
| | `s-appointments` قائمة المواعيد والتفاصيل (Appointments list & details) | list |
| | `s-walkin` حجز عميل حضوري (Walk-in booking) | plus |
| | `s-hours` الدوام والاستراحات والإجازات (Hours, breaks, time off) | clock |
| | `s-services` الخدمات والاشتراك (Services & subscription) | tag |
| | `s-settings` إعدادات المحل والإشعارات (Shop settings & notifications) | settings |
| لوحة الإدارة (Admin dashboard) | `a-overview` نظرة عامة على المنصة (Platform overview) | chart |
| | `a-shops` المحلات وتفاصيل المحل (Shops & shop details) | store |
| | `a-pros` الحلاقون والنقل والخدمات (Barbers, **transfer**, services) [!] see §7 | users |
| | `a-services` الخدمات العامة والإسناد (**Global services** & assignment) [!] see §7 | tag |
| | `a-appointments` المواعيد والعملاء (Appointments & customers) | list |
| | `a-subs` الاشتراكات والتجديد (Subscriptions & renewal) | card |
| | `a-reviews` التقييمات وQR وواتساب (Reviews, QR, WhatsApp) | star |
| | `a-roles` الأدوار والصلاحيات والسجل (Roles, permissions, log) | shield |
| المراجعة (Review) | `r-responsive` السلوك التجاوبي (Responsive behaviour) | grid |
| | `r-states` الحالات الفارغة والأخطاء (Empty states & errors) | alert |
| | `r-a11y` الوصولية ومراجعة RTL (Accessibility & RTL review) | eyeOff |

---

## 1. Design tokens

### 1.1 Colour — brand steel-blue / navy scale

| Proposed token | Hex | Role / where used |
|---|---|---|
| `--color-brand-50` | `#F8FBFE` | Selected service-card background (515) |
| `--color-brand-100` | `#EDF3FA` | "أزرق فاتح" (light blue) from the palette (3588). Selected-item bg, secondary button bg, active nav bg, input focus ring (406), date cell "available", avatar bg, icon tiles, "Arrived" badge bg |
| `--color-brand-100-border` | `#E0E9F3` | Border on the light-blue swatch (3588) |
| `--color-brand-150` | `#E0EAF6` | Secondary button hover (382) |
| `--color-brand-info-bg` | `#E8F1FB` | "Confirmed" status bg (3635) |
| `--color-brand-200` | `#DCE7F2` | Grid-column preview (352), body text on navy panels (80, 165, 3288) |
| `--color-selection` | `#D6E5F5` | `::selection` (23) |
| `--color-brand-200-border` | `#CBDCEC` | "Available" calendar legend border (486) |
| `--color-brand-300` | `#B9CBDD` | Spacing chips ≤8 (3611), chart bars ≤80% (3686), sitemap child dots (3520) |
| `--color-brand-300-alt` | `#AEC4DA` | Paragraph on navy (83) |
| `--color-on-navy-muted` | `#8FB0D0` | Tag text on navy (172), toast close icon (699), inactive sidebar item (3715) |
| `--color-on-navy-accent` | `#7FA8D4` | Eyebrow and icons on navy (81, 170, 326, 656, 3293, 3304) |
| **`--color-brand-500`** | **`#6D9BCB`** | "الأزرق الأساسي" (primary blue) from the palette (3585). Use for selected surfaces, active borders, charts, focus ring, step dots and the app-mark background. **Never use it as text on white** (252, 4634) |
| `--color-brand-600` | `#4A7FB5` | Links (20), icons in sitemap/journey (126, 156), "Confirmed" dot (3635) |
| `--color-brand-600-alt` | `#5F86AD` | "BUSINESS" / "PLATFORM ADMIN" eyebrow on the navy sidebar (2122, 2665; outside scope) |
| **`--color-brand-700`** | **`#2C5C8C`** | "أزرق داكن للنص" (dark blue for text) from the palette (3586). Blue text on white and on light-blue, secondary button text, links, verified-shop shield (563) |
| `--color-navy-800` | `#1B3A55` | Primary button hover and loading bg (381, 389, 3622, 3627) |
| **`--color-navy-900`** | **`#10283D`** | "الكحلي العميق" (deep navy) from the palette (3587). Primary buttons, headings, dark hero panels, selected date or slot, toast, dashboard sidebar |
| `--color-navy-950` | `#0A1B2A` | Primary button pressed (3623) |

### 1.2 Colour — neutrals (text, borders, surfaces)

| Proposed token | Hex | Role |
|---|---|---|
| `--color-text-primary` | `#17212B` | "النص الأساسي" (primary text) from the palette (3591). Body text, headings, tooltip bg (702) |
| `--color-text-strong` | `#3D5266` | Strong secondary text, icon colour, outline-button text, slot text, numerals (3607) |
| `--color-text-secondary` | `#647484` | "النص الثانوي" (secondary text) from the palette (3592). Descriptions and supporting data |
| `--color-text-muted` | `#5E6E7E` | Inactive prototype nav (3490), "No-show" and "Suspended" fg (3639) |
| `--color-text-tertiary` | `#8C9BAA` | Breadcrumb, hex labels, helper text (410), empty-state icon [!] fails AA as text |
| `--color-text-eyebrow` | `#9AA8B6` | Uppercase eyebrows, weekday heads [!] fails AA |
| `--color-text-placeholder` | `#A9B6C4` | Placeholder, meta, counts, disabled text [!] fails AA |
| `--color-neutral-400` | `#98A7B5` | "محايد" (neutral) from the palette (3596). Disabled, no-show, past. Inactive bottom-nav item (3708) |
| `--color-text-disabled` | `#AEBAC6` | Unavailable slot text (3673) |
| `--color-text-past` | `#C2CCD6` | Past or closed calendar day, past slot (3654–3655, 3674) |
| `--color-text-token-label` | `#B6C2CE` | Token name in the type-scale table (268) |
| `--color-breadcrumb-sep` | `#C9D4E0` | Breadcrumb chevron (746) |
| `--color-border` | `#E6ECF3` | "الحدود" (borders) from the palette (3590). Card borders and dividers |
| `--color-border-input` | `#E1E8F0` | Input borders (1.5px), slot border, calendar nav buttons |
| `--color-border-strong` | `#D4DFEA` | Outline button (1.5px), unchecked radio, dot separators (567), dashed past slot |
| `--color-border-dashed` | `#CBD8E5` | Dashed upload or add zones (670, 689) |
| `--color-border-subtle` | `#EEF2F7` | Inner dividers, card inner borders, sticky-header divider |
| `--color-border-panel` | `#EAF0F6` | Inner panels on the page bg (108, 239) |
| `--color-border-row` | `#F1F5F9` | Table and list row dividers (265, 677, 755) |
| `--color-border-shell` | `#E3EAF2` | Prototype shell borders (35, 58) |
| `--color-border-slot-off` | `#EBEFF4` | Border of an unavailable slot (3673) |
| `--color-scrollbar` | `#D8E1EB` | Scrollbar thumb (25) |
| `--color-switch-off` | `#DDE4EC` | Switch track, off (3689) |
| `--color-dot-forbidden` | `#D9E2EC` | Sitemap "forbidden" node dot (3520) |
| `--color-surface` | `#FFFFFF` | Cards |
| `--color-bg-page` | `#F7F9FC` | "خلفية الصفحة" (page background) from the palette (3589). Inner panels, table header |
| `--color-bg-app` | `#EEF2F7` | App backdrop (33), disabled bg (392, 3626), skeleton base |
| `--color-bg-subtle` | `#F1F5F9` | Segmented-control track (447), tag chips (570), empty-state icon tile, rating track |
| `--color-bg-muted` | `#F1F4F7` | Closed date cell, unavailable slot, "No-show" and "Suspended" bg |
| `--color-bg-tile` | `#FBFCFE` | Icon-grid tiles, breakpoint cards (313, 348) |
| `--color-bg-card-header` | `#F9FBFD` | Sitemap column header (125) |
| `--color-skeleton-highlight` | `#F7FAFD` | Shimmer highlight (724, 4620) |
| `--color-placeholder-stripe-a` / `-b` | `#E7EDF4` / `#EFF3F8` | Image placeholder: `repeating-linear-gradient(135deg, a 0 8px, b 8px 16px)`. Avatars use 6/12px |

### 1.3 Colour — semantic

| Proposed token | Hex | Role |
|---|---|---|
| `--color-success-500` | `#2E9E6B` | "نجاح" (success) from the palette (3593): confirmed, completed, sent. Switch on, valid-field border, toast check bg [!] white on it is only 3.38:1 |
| `--color-success-700` (fg) | `#1F6B48` | Success text and badge fg, positive KPI delta (629) |
| `--color-success-50` (bg) | `#E6F4EC` | Success badge bg |
| `--color-warning-500` | `#D89A2E` | "تحذير" (warning) from the palette (3594): pending, subscription expiring. **Also the rating-star colour** (542) and the coffee/breaks icon (685) |
| `--color-warning-700` (fg) | `#8A5A12` | Warning text, "في إجازة" (on vacation) chip text (548) |
| `--color-warning-50` (bg) | `#FDF3E2` | Warning badge bg, vacation chip |
| `--color-warning-border` | `#F2E1C4` | Warning note border (252) |
| `--color-danger-500` | `#C44545` | "خطأ" (error) from the palette (3595): cancel, errors, delete. Error input border, destructive dialog primary button |
| `--color-danger-700` (fg) | `#9B2C2C` | Danger text, destructive-button text, error message, negative KPI delta (634) |
| `--color-danger-50` (bg) | `#FBEAEA` | Danger button bg, danger icon tile |
| `--color-danger-100` | `#F6DADA` | Danger button hover (384) |
| `--color-danger-surface` | `#FEF8F8` | Error input bg, error card bg (414, 730) |
| `--color-danger-border` | `#E3B4B4` | Error-state button border (735, 3628) |
| `--color-danger-card-border` | `#F0DADA` | Error card border (730) |
| `--color-danger-body` | `#8C6A6A` | Error card body text (734) |
| `--color-info-*` | reuse brand: `#E8F1FB` / `#2C5C8C` / `#4A7FB5` | "Confirmed" uses the info triad |
| `--color-rating` | `#D89A2E` | Stars |
| `--color-overlay-navy-88` | `rgba(16,40,61,.88)` | "Open" badge on a cover image (557, 3727) |
| `--color-overlay-slate-92` | `rgba(100,116,132,.92)` | "Closed / opens at" badge on a cover image (3727) |
| `--color-overlay-white-94` | `rgba(255,255,255,.94)` | Favourite button on a cover image (558) |
| `--overlay-image-text` | navy at 40% | "overlay 40% navy for text on image" (326) |
| On-navy surfaces | `rgba(255,255,255,.06)`, `.07`, `.1` | Rows, tags and active item inside navy panels (169, 172, 3715) |
| Header glass | `rgba(247,249,252,.92)` + `blur(8px)` | Sticky header (58) |

**WhatsApp:** no WhatsApp brand green (`#25D366`) is used anywhere in the file.
- The WhatsApp confirmation mock (outside this scope, lines 1602–1611) uses the chat wallpaper `#E9E3DB` / `#EDE8E1` in 14px stripes.
- Its header is navy `#10283D` with a white round "T" avatar.
- WhatsApp is otherwise represented by the `msg` icon in brand colours.
- Do not introduce WhatsApp green unless the client asks for it.

### 1.4 Booking & subscription status colours (STATUS map, 3633–3644)

The format is `label → [bg, fg, dot]`. The same map drives lifecycle badges, table badges and responsive cards.

| Arabic label | English / spec enum | bg | fg | dot | fg-on-bg contrast (computed) |
|---|---|---|---|---|---|
| بانتظار التأكيد | Awaiting confirmation → `Pending` | `#FDF3E2` | `#8A5A12` | `#D89A2E` | 5.38:1 ✓ |
| مؤكد | Confirmed → `Confirmed` | `#E8F1FB` | `#2C5C8C` | `#4A7FB5` | 6.10:1 ✓ |
| حضر العميل | Customer arrived → `Arrived` | `#EDF3FA` | `#3D5266` | `#6D9BCB` | 7.24:1 ✓ |
| مكتمل | Completed → `Completed` | `#E6F4EC` | `#1F6B48` | `#2E9E6B` | 5.69:1 ✓ |
| ملغي | Cancelled → `Cancelled*` (spec splits customer vs shop cancellation, §7) | `#FBEAEA` | `#9B2C2C` | `#C44545` | 6.47:1 ✓ |
| لم يحضر | No-show → `NoShow` | `#F1F4F7` | `#5E6E7E` | `#98A7B5` | 4.75:1 ✓ |
| نشط | Active → `Active` (subscription) | `#E6F4EC` | `#1F6B48` | `#2E9E6B` | ✓ |
| قرب الانتهاء | Expiring soon → `ExpiringSoon` | `#FDF3E2` | `#8A5A12` | `#D89A2E` | ✓ |
| منتهي | Expired → `Expired` | `#FBEAEA` | `#9B2C2C` | `#C44545` | ✓ |
| موقوف | Suspended → `Suspended` | `#F1F4F7` | `#5E6E7E` | `#98A7B5` | ✓ |

Suggested tokens: `--status-{pending|confirmed|arrived|completed|cancelled|noshow}-{bg|fg|dot}` and `--sub-{active|expiring|expired|suspended}-{bg|fg|dot}`. The subscription tokens alias the success, warning, danger and neutral triads.

### 1.5 Typography

**Families:**
- Arabic: **Tajawal**, weights 400/500/700/800 are loaded (16). Fallback stack is `'IBM Plex Sans Arabic', system-ui, sans-serif` (19).
- Latin and Latin numerals: **Inter**, weights 400/500/600/700 are loaded (16).

**Loading gaps to fix:**
- Tajawal has **no 600** weight. `fontWeight 600` in Tajawal (sitemap root nodes, 3516) will synthesise or snap to 700.
- **Inter 800** is used for the app mark "T" (223), the WhatsApp avatar (outside scope) and `font:800` elsewhere, but it is **not loaded**. Add 800 to the Inter request.
- **IBM Plex Sans Arabic** is in the fallback stack but never loaded.

**Google Fonts URL (as-is):**
`https://fonts.googleapis.com/css2?family=Tajawal:wght@400;500;700;800&family=Inter:wght@400;500;600;700&display=swap`

**Recommended:** use `next/font/google` with Tajawal `['400','500','700','800']` and Inter `['400','500','600','700','800']`, `display:'swap'`, and subsets `arabic` and `latin`.

**Rule (262):** "العربية بخط Tajawal بارتفاع سطر ١.٧–١.٩ لاستيعاب التشكيل والنقاط؛ الإنجليزية والأرقام اللاتينية بخط Inter." This translates as: "Arabic in Tajawal with line-height 1.7–1.9 to fit diacritics and dots; English and Latin numerals in Inter."
- The mixed-content example (273) wraps each Latin number or name in an Inter 600/700 span inside Tajawal text: "الموعد يوم الخميس **18** سبتمبر الساعة ٥:٣٠ م مع **Faisal** — **85** ر.س".

**Declared type scale (3600–3607):**

| Token | Size / weight / line-height | Family | Sample |
|---|---|---|---|
| `--text-display` | 32 / 700 / 1.35 | Tajawal | احجز حلاقتك القادمة ("Book your next haircut") |
| `--text-h1` | 24 / 700 / 1.4 | Tajawal | صالون الأصالة للحلاقة |
| `--text-h2` | 19 / 700 / 1.45 | Tajawal | اختر الوقت المناسب ("Choose the right time") |
| `--text-h3` | 16 / 700 / 1.5 | Tajawal | باقة شعر ولحية ("Hair & beard package") |
| `--text-body` | 15 / 400 / 1.85 | Tajawal | حلاقة كلاسيكية مع غسيل وتصفيف |
| `--text-caption` | 13.5 / 400 / 1.8 | Tajawal | حي الملقا · ٢٫٤ كم |
| `--text-numeric` | 12 / 600 / 1.2, letter-spacing .04em, colour `#3D5266` | **Inter** | SAR 85 · 50 MIN |

**Additional roles observed (propose tokens):**

| Proposed token | Spec | Where |
|---|---|---|
| `--text-eyebrow` | Inter 700, 10–11px, line-height 1, letter-spacing .1–.16em (also .22em in the shell), uppercase Latin, colour `#9AA8B6` or `#A9B6C4` | Card tags "BUTTON", "INPUT" (379…); section eyebrows (207, 430) |
| `--text-page-title` | 21 / 700 / 1.3, `#10283D` | Header H1 (61) |
| `--text-hero-h2` | 24 / 1.4 on navy | 82 |
| `--text-section-title` | 19px bold | Card H2s (103, 236) |
| `--text-card-title` | 16–17px bold | H3s (87, 166, 379) |
| `--text-button` | 14.5 / 700 | Buttons (381) |
| `--text-button-sm` | 13–14 / 700 | Dialog and QR buttons |
| `--text-label` | 13 / 700 | Form labels (401). 12.5 in the validation card (3223) |
| `--text-input` | 14.5 / 400 | 402. Phone digits use Inter 500 15 with letter-spacing .06em (408) |
| `--text-otp` | Inter 600 15, letter-spacing .3em, LTR | 414 |
| `--text-helper` | 12.5, `#8C9BAA` (helper) or `#9B2C2C` 500 (error) | 410, 415 |
| `--text-badge` | 12 / 700 (component), 12.5 / 700 (lifecycle), 10.5 / 700 (compact) | 3631, 3572, 4591 |
| `--text-kpi` | Inter 700 25 / 1.2 | 628 |
| `--text-rating-big` | Inter 700 32 / 1 | 597 |
| `--text-nav-label` | 10.5 / 700 | Bottom nav (787) |
| `--text-small` | 11–12px | Meta |

- Observed font sizes (whole file): 9.5, 10, 10.5, 11, 11.5, 12, 12.5, 13, 13.5, 14, 14.5, 15, 15.5, 16, 16.5, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 30, 32, 38, 40, 44, 56, 96.
  - The most frequent are 13 (173×), 12.5 (143×), 13.5 (105×) and 14 (97×).
  - Recommend collapsing to: 10, 11, 12, 12.5, 13, 13.5, 14, 14.5, 15, 16, 17, 19, 21, 24, 32. Icon glyph sizes (15–23, 56) are not type.
- Weights used: 400, 500, 600, 700 (dominant, 469×) and 800.
- Line-heights used: 1, 1.1, 1.2, 1.3, 1.4, 1.6, 1.7, 1.75, 1.8, 1.85, 1.9, 1.95.
- Letter-spacings used: −.02em (app mark), .04em, .06em, .1em, .12em, .14em, .16em, .2em, .22em, .3em (OTP).

**Numerals rule (resolve before build):**
- Times and dates use **Arabic-Indic digits in 12-hour format**, e.g. ٥:٣٠ م. Dates are Gregorian with the day name (4641).
- Prices, ratings, counts, KPIs, calendar day numbers and phone numbers use **Latin digits in Inter** (85, 4.8, 18, +966).
- Distance is inconsistent: `٢٫٤ كم` (3606) vs `2.4 كم` (567).
- Suggested rule: Latin/Inter for all measurable quantities (price, distance, rating, counts, phone, OTP) and Arabic-Indic for clock times and date text. Confirm with the client.
- Currency is written "ر.س" in Tajawal 12px `#647484` after the Latin number (522).

### 1.6 Spacing

- Declared scale (279–288, 3610): **4, 8, 16, 24, 32, 48, 64**, labelled "شبكة ٨ نقاط" (8-point grid).
- "٤ بكسل كنصف وحدة للحالات الضيقة فقط" means "4px as a half unit for tight cases only". Chips ≤8 are drawn in `#B9CBDD`, the rest in `#6D9BCB`.

| Token | px |
|---|---|
| `--space-0-5` | 4 |
| `--space-1` | 8 |
| `--space-2` | 16 |
| `--space-3` | 24 |
| `--space-4` | 32 |
| `--space-6` | 48 |
| `--space-8` | 64 |

**Observed off-scale values** (the design does not follow its own grid strictly; add these as half steps or round them):
- Gaps: 1, 2, 3, 5, 6, 7, 9, 10, 11, 12, 13, 14, 18, 20, 22, 28, 44.
- Paddings: 3, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 18, 20, 22, 26, 28, 30, 40.
- The most common are gap 10 (100×), gap 8/12 (83× each), padding 14 (115×), 12 (90×), 18 (85×), 22 (74×) and 20 (67×).
- **Recommendation:** extend to a 2px-granular scale, `2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 28, 32, 40, 48, 64`, because the card paddings 22/26/28 are part of the look.

**Recurring layout values:**

| Value | Where |
|---|---|
| Card padding 22 (components), 24–28 (doc/foundation sections), 20–22 (rule cards) | Throughout |
| Grid gaps 20 (card grids), 14 (inner grids), 10–12 (chips/buttons) | Throughout |
| Content padding 28/32 | 75 |
| Max content width 1180 | doc-intro |
| Max content width 1240 | sitemap, journey, foundations |
| Max content width 1280 | components, review screens |

### 1.7 Grid & breakpoints (345–370, 4592–4596, 813)

| Breakpoint | Columns | Margin | Nav pattern | Line |
|---|---|---|---|---|
| Mobile · 390px | 4 cols | 16 margin, 16 gutter | Bottom bar (شريط سفلي) | 349–350 |
| Tablet · 768px | 8 cols | 24 margin | Sliding side drawer (درج جانبي منزلق) | 356–357 |
| Desktop · 1440px | 12 cols | 40 margin | **Fixed sidebar 264px** (شريط جانبي ٢٦٤ ثابت) | 363–364 |

Responsive rule text (4593): "≥1200 لوحة كاملة بشريط ثابت · 768–1199 درج منزلق · <768 شريط سفلي وبطاقات". Translation: at ≥1200 the full dashboard has a fixed sidebar; at 768–1199 a sliding drawer; below 768 a bottom bar and cards.

**[!] Inconsistencies to decide:**
- The drawer threshold is **<992px** in the ds-components sidebar note (813) but **<1200px** in r-responsive (4593).
- The sidebar is **264px** in foundations (364), while the prototype shell nav is 272px (35). The shell is not product UI.
- The prototype uses **no `@media` queries**; everything is `repeat(auto-fit, minmax(...))` grids.
- **Proposed Tailwind screens:** `sm: 390px` (design reference only), `md: 768px`, `lg: 992px` or `1200px` (pick one; the rule text favours 1200), `xl: 1440px`.

### 1.8 Radii (291–296, observed)

| Token | px | Declared use |
|---|---|---|
| `--radius-field` | 10 | "حقول" (fields): inputs, selects, small buttons (36px QR buttons), pagination 9 |
| `--radius-button` | 12 | "أزرار" (buttons): 46px buttons, toast, inner panels, table container |
| `--radius-card` | 14 | "بطاقات" (cards): service, shop and appointment cards, dialog |
| `--radius-section` | 16 | "أقسام" (sections): top-level white cards |

Observed extras:

| Proposed token | px | Where |
|---|---|---|
| `--radius-xs` | 3–4 | Legend swatches, grid previews, spacing chips |
| `--radius-badge` | 6–7 | Badges and tag chips 7; skeleton lines 6 |
| `--radius-sm` | 8–9 | Menu items 9, calendar cells 9, break chips 9, segmented tab 9, sitemap rows 8 |
| — | 11, 13 | 11 for state CTA and resp cards; 13 for a11y demo cards |
| `--radius-pill` | 20 | Switch track |
| — | 40 | Phone device frame (prototype only) |
| `--radius-full` | 50% | Avatars, dots, step numbers, radio |
| Chart bar | 5px 5px 3px 3px | 3686 |

### 1.9 Borders

| Width | Use |
|---|---|
| 1px solid `#E6ECF3` | Card and container borders |
| 1.5px solid | Inputs (`#E1E8F0`), outline buttons and radio (`#D4DFEA`), selected card (`#6D9BCB`), focused input (`#6D9BCB`), error input (`#C44545`), valid input (`#2E9E6B`), slots (`#E1E8F0`) |
| 1.5px dashed `#CBD8E5` | Upload drop zone, "add break" chip |
| 1px dashed `#D4DFEA` | Past-slot legend |
| 2.5px solid `#10283D` | Active underline tab (453) |
| 2px `#E6ECF3` | Lifecycle timeline connector (185) |
| 2px white | Ring around the avatar camera badge (668) |

### 1.10 Shadows / elevation (298–302 and observed)

| Token | Value | Use |
|---|---|---|
| `--shadow-e1` | `0 1px 2px rgba(16,40,61,.04)` | "E1 · بطاقة" (card): every white card (92×) |
| `--shadow-e2` | `0 6px 16px -6px rgba(16,40,61,.16)` | "E2 · قائمة" (list/menu) |
| `--shadow-e3` | `0 18px 40px -12px rgba(16,40,61,.26)` | "E3 · نافذة" (window/modal): dropdown menu (458). The dialog preview uses `.2` (704) |
| `--shadow-btn-hover` | `0 6px 16px -6px rgba(16,40,61,.35)` | Primary hover (3622) |
| `--shadow-toast` | `0 18px 40px -14px rgba(16,40,61,.6)` | Toast (3303) |
| `--shadow-segment-active` | `0 1px 2px rgba(16,40,61,.08)` | Active segmented tab (448) |
| `--shadow-knob` | `0 1px 2px rgba(16,40,61,.25)` | Switch knob (3690) |
| `--ring-focus` | `0 0 0 3px #fff, 0 0 0 6px #6D9BCB` | Button focus (3624): a 3px white gap plus a 3px brand ring |
| `--ring-input-focus` | `border-color:#6D9BCB; box-shadow:0 0 0 3px #EDF3FA` | Input focus (406) |
| `--shadow-dot-halo` | `0 0 0 3px {dot}22` | Lifecycle dots (3573) |
| (prototype) device frame | `0 26px 60px -24px rgba(16,40,61,.4)` | Phone mockups outside scope |
| (outside scope) bottom sheet | `0 -18px 40px -20px rgba(16,40,61,.5)`, `0 -14px 34px -18px …` | Drawers and sheets |

Two focus treatments exist: the button ring (3624) and the input ring (406). Proposal: the button/interactive ring is canonical for `:focus-visible`, and inputs keep the border plus soft ring.

### 1.11 Z-layers

| Token | Value | Use |
|---|---|---|
| `--z-sticky-inner` | 2 | Sticky nav header (36); overlay layers outside scope (3776) |
| `--z-sheet` | 3–4 | Sheets and drawers (3780, 3995; outside scope) |
| `--z-header` | 5 | Sticky main header (58); drawer at 4291 |
| `--z-toast` | 99 | Toast (3303, 4154) |

Proposed canonical set: base 0, sticky 10, header 20, overlay/backdrop 30, drawer/sheet 40, dialog 50, toast 60.

### 1.12 Motion

| Token | Value | Use |
|---|---|---|
| `--motion-shimmer` | `shimmer 1.4s linear infinite`, gradient `linear-gradient(90deg,#EEF2F7 0%,#F7FAFD 40%,#EEF2F7 80%)`, `background-size:400px 100%` | Skeletons (724–727, 4620) |
| `--motion-spin` | `spin .7s linear infinite` | Button spinner: 15px, 2px border `rgba(255,255,255,.35)`, top `#fff` (390) |
| `--duration-fast` | 150ms | Nav bg (3492); colour/transform (4091) |
| `--duration-base` | 250ms | Overlay opacity; toast opacity and transform (outside scope, 3776/4155) |
| `--duration-slow` | 300–320ms | Drawer transform `cubic-bezier(.32,.72,0,1)` (3781, 4059, 4292), progress width .3s (3978) |
| `--ease-drawer` | `cubic-bezier(.32,.72,0,1)` | Drawers |
| Pressed | `transform: scale(.98)` | 3623 |
| Toast lifetime | 2600ms | 3453 |
| `fadeUp` | 6px rise | Defined, unused |

Respect `prefers-reduced-motion`: the design does not mention it, so add it.

---

## 2. Component library (ds-components 375–818, plus foundations states)

Every component card sits in a white section: radius 16, padding 22, E1 shadow. Its header is an H3 16px plus a Latin eyebrow tag (Inter 700 10px, .14em, `#A9B6C4`). The grid is `repeat(auto-fit,minmax(330px,1fr))` with gap 20.

### 2.1 Button (379–395; states 3616–3629)

**Base:**
- Height **46px** ("≥ ٤٤ للمس", meaning ≥44 for touch).
- Padding 0 20, radius 12, font 14.5/700, no border unless outline.
- Icon precedes text in RTL (394).

| Variant | bg | text | border | hover | Line |
|---|---|---|---|---|---|
| Primary | `#10283D` | `#fff` | — | `#1B3A55` | 381 "احجز الآن" (Book now) |
| Secondary | `#EDF3FA` | `#2C5C8C` | — | `#E0EAF6` | 382 "ثانوي" |
| Outline | `#fff` | `#3D5266` | 1.5px `#D4DFEA` | border `#6D9BCB` | 383 "محدد" |
| Danger (soft) | `#FBEAEA` | `#9B2C2C` | — | `#F6DADA` | 384 "إلغاء الموعد" (Cancel appointment) |
| Danger (solid, dialog) | `#C44545` | `#fff` | — | — | 709 |
| Icon button outline | 46×46 `#fff`, `#3D5266`, 1.5px `#D4DFEA`, icon 19px | | | | 387 (heart) |
| Icon button secondary | 46×46 `#EDF3FA`, `#2C5C8C` | | | | 388 (msg) |
| Loading | `#1B3A55`, spinner + "جارٍ التأكيد" (Confirming…), `cursor:default` | | | | 389–391 |
| Disabled | `#EEF2F7`, text `#A9B6C4`, `cursor:not-allowed` | | | | 392 |

**Sizes:**

| Size | Height | Padding | Radius | Font | Where |
|---|---|---|---|---|---|
| lg | 46 | 0 20 | 12 | 14.5 | Default |
| md | 44 | — | 12 | 14 | Dialog, 709–710 |
| md | 42 | 0 18 | 12 | 14 | Empty-state CTA, 721 |
| sm | 40 | 0 16 | 11 | 13.5 | State CTA, 4606 |
| xs | 36 | 0 14 | 10 | 13 | QR actions 661–662, retry 735 |

**Interaction states (foundations 332–342, 3620–3629).** Each state is communicated by more than one cue: colour + border + icon or text (333).

| State | Visual | Note (AR → EN) |
|---|---|---|
| Default | Navy fill | كحلي ممتلئ (solid navy) |
| Hover | `#1B3A55` + `0 6px 16px -6px rgba(16,40,61,.35)` | تفتيح ٦٪ + ظل (lighten 6% + shadow) |
| Pressed | `#0A1B2A`, `scale(.98)` | تعتيم + تصغير ٩٨٪ (darken + scale to 98%) |
| Focus | `0 0 0 3px #fff, 0 0 0 6px #6D9BCB` | حلقة ٣ بكسل خارجية (3px outer ring) |
| Selected | bg `#EDF3FA`, text `#10283D`, 1.5px `#6D9BCB` (label "٥:٣٠ م") | أزرق فاتح + حد (light blue + border) |
| Disabled | bg `#EEF2F7`, text `#A9B6C4` ("غير متاح", unavailable) | شفافية + لا مؤشر (transparency + no pointer) |
| Loading | `#1B3A55`, opacity .92, "... جارٍ التأكيد" | نص + مؤشر دوّار (text + spinner) |
| Error | bg `#FBEAEA`, text `#9B2C2C`, 1.5px `#E3B4B4` ("تعذّر الحجز", booking failed) | حد أحمر + رسالة (red border + message) |

### 2.2 Inputs & validation (397–426; 3216–3233)

| Part | Spec |
|---|---|
| Label | 13px/700, margin-bottom 6. Optional marker "(اختياري)" in `#A9B6C4` 500 (422) |
| Text input | Height 46, 1.5px `#E1E8F0`, radius 10, padding 0 14, 14.5px. Value example: عبدالله الشمري |
| Phone input (focused) | Border `#6D9BCB` with ring `0 0 0 3px #EDF3FA`. Country-code segment "+966" has bg `#F7F9FC`, `border-inline-end` `#E1E8F0`, Inter 600 13.5, `direction:ltr`. Number is Inter 500 15, .06em, LTR, grouped `50 214 8830`. Helper text uses the info icon + "يُستخدم لإرسال تأكيد الموعد عبر واتساب" ("Used to send the appointment confirmation via WhatsApp") at 12.5 `#8C9BAA` (406–410) |
| OTP (error) | Border `#C44545`, bg `#FEF8F8`, Inter 600 15, .3em, LTR, trailing alert icon. Error "الرمز غير صحيح — تبقى محاولتان" ("Incorrect code — 2 attempts left") at 12.5/500 `#9B2C2C` (413–415) |
| Search field | Height 46, 1.5px `#E1E8F0`, radius 10, search icon 18px, placeholder "ابحث عن محل أو خدمة" ("Search for a shop or service") in `#8C9BAA` (418) |
| Select | Width 132, height 46, value "الأقرب" (Nearest) + chevD `#8C9BAA` (419) |
| Textarea | Min-height 76, padding 12 14, 14px, line-height 1.8. Placeholder "مثال: تدريج قصير من الجانبين" ("e.g. short fade on the sides"). Label "ملاحظة للحلاق (اختياري)" ("Note for the barber (optional)") (421–424) |
| Validation card | Height 44 variant. Phone error "الرقم غير مكتمل — يجب أن يتكون من ٩ أرقام بعد المفتاح" ("Number incomplete — must be 9 digits after the code"). Valid state has border `#2E9E6B` + check icon. Rule (3231): "الرسالة تحت الحقل مباشرة، وتبدأ بما يجب فعله لا بما أخطأ فيه المستخدم" ("Message directly under the field, starting with what to do rather than what the user did wrong") |

### 2.3 Status badges (429–443; 3631–3648)

- **Anatomy:** `inline-flex`, gap 6, padding 4px 10px, radius 7, 12px/700, bg and fg from STATUS, and a **6px dot** in the dot colour.
- **Lifecycle variant (3572–3573):** padding 3px 10px, 12.5px/700, no inline dot. The timeline dot is 10px with a `0 0 0 3px {c}22` halo.
- **Compact variant (4591):** 10.5px, padding 3px 7px, no dot.
- Rule (442): "كل شارة تحمل نقطة لون + نصاً صريحاً، فلا يعتمد الفهم على اللون وحده" ("Every badge carries a colour dot + explicit text, so understanding never depends on colour alone").
- Appointment set: Pending, Confirmed, Arrived, Completed, Cancelled, NoShow. Subscription set: Active, ExpiringSoon, Expired, Suspended.
- **Other chips:**

| Chip | Spec | Line |
|---|---|---|
| Tag chip | 11.5px, bg `#F1F5F9`, `#3D5266`, padding 4 9, radius 7 | 570 |
| "On vacation" chip | 12px/700, `#8A5A12` on `#FDF3E2`, radius 7, padding 3 8 | 548 |
| "Open until" image badge | 11.5/700, white on `rgba(16,40,61,.88)`, radius 7, padding 4 9. Closed variant uses `rgba(100,116,132,.92)` | 557 |
| Break chip (removable) | White, 1px `#E1E8F0`, radius 9, padding 7 11, 12.5px, trailing x | 687 |
| Add chip | 1.5px dashed `#CBD8E5`, `#2C5C8C` 700, plus icon | 689 |

### 2.4 Tabs & menu (445–465)

| Component | Spec | Lines |
|---|---|---|
| **Segmented tabs** | Track `#F1F5F9`, padding 4, radius 12, gap 4. Item: flex 1, padding 10, radius 9, 14px. Active item is white, 700, `0 1px 2px rgba(16,40,61,.08)`; inactive is `#647484`. Items: قادمة / سابقة / ملغاة (Upcoming / Past / Cancelled) | 447–451 |
| **Underline tabs** | Row gap 22, border-bottom 1px `#E6ECF3`. Active has padding-bottom 10, 2.5px `#10283D` underline, 700, 14.5px; inactive is `#647484`. Items: الخدمات / الحلاقون / التقييمات / عن المحل (Services / Barbers / Reviews / About) | 452–457 |
| **Dropdown / context menu** | Width 212, white, 1px `#E6ECF3`, radius 12, E3 shadow, padding 6. Items: padding 10 12, radius 9, 14px, icon 17px `#3D5266`, gap 10. Highlighted item bg is `#F7F9FC`. Divider is 1px `#EEF2F7` with margin 5 8. Destructive item is `#9B2C2C` | 458–464 |

Menu items: تأكيد الموعد (Confirm), إعادة جدولة (Reschedule), تغيير الحلاق (Change barber [!] see §7), divider, إلغاء (Cancel).

### 2.5 Date picker / month calendar (467–489; 3650–3668)

- **Header:** month "سبتمبر ٢٠٢٦" at 15/700, with two 32×32 nav buttons (1px `#E1E8F0`, radius 9). **chevR first, then chevL** (RTL: right arrow = previous).
- **Grid:** 7 columns, gap 4. Weekday heads are Inter 700 11 `#9AA8B6`: أحد إثن ثلا أرب خمي جمع سبت, **week starts Sunday**.
- **Cells:** 34px tall, radius 9, Inter 600 13.

| Cell state | Style |
|---|---|
| ok / available | bg `#EDF3FA`, `#2C5C8C` |
| sel | bg `#10283D`, `#fff` |
| off / closed | bg `#F1F4F7`, `#C2CCD6`, line-through |
| past | `#C2CCD6`, no bg |
| empty | Leading blanks |

- **Legend:** مختار (selected), متاح (available; bg `#EDF3FA` + border `#CBDCEC`), مغلق (closed; `#F1F4F7`). Swatches are 10×10, radius 3.
- **Demo data:** days <15 past, 18 selected, 19 and 26 closed, 2 leading blanks. Sept 1 2026 is a Tuesday, so this is correct.
  - [!] Sept 18 2026 is a **Friday**, yet the copy says "الخميس 18 سبتمبر" (Thursday 18 Sept) (273, 706). The closed 19/26 are Saturdays, which matches "السبت مغلق" (Saturday closed) in the hours editor. **Fix the weekday in seed data.**
- The **date strip** (14-day horizontal strip, journey step 5, 3557) is not drawn in ds-components. See the booking flow screen (outside scope, ~1440+).

### 2.6 Time-slot picker (491–511; 3670–3678)

- Intro (493): "خطوات مرنة (٥ / ١٠ / ١٥ دقيقة) حسب مدة الخدمة والفجوات الفعلية" ("Flexible steps (5/10/15 min) based on service duration and actual gaps").
- **Groups:** صباحاً (morning) and مساءً (evening), each with an eyebrow (Inter 700 11, .1em, `#9AA8B6`). Grid is `repeat(auto-fill,minmax(82px,1fr))`, gap 8.
- **Slot:** height **42**, radius 10, 13.5/700, 1.5px `#E1E8F0`, `#3D5266`, white.

| State | Style |
|---|---|
| sel | `#10283D` bg, border `#10283D`, white |
| off (booked or break) | bg `#F1F4F7`, `#AEBAC6`, border `#EBEFF4`, line-through |
| past | `#C2CCD6`, dashed `#E1E8F0` border |

- **Legend (507–509):**
  - "مختار — ٥:٣٠ م حتى ٦:٢٠ م" (selected shows the start–end span).
  - "محجوز أو داخل استراحة — يظهر السبب عند اللمس" ("Booked or within a break — reason shown on touch").
  - "وقت مضى — لا يُعرض بعد انتهاء اليوم" ("Past time — not shown after the day ends").
- Demo times show irregular steps: ٩:٠٠، ٩:١٥، ٩:٣٠، ٩:٤٥، ١٠:٠٥، ١٠:٣٠، ١١:٠٠، ١١:٤٠ ص / ٤:٠٠، ٤:٣٠، ٥:٠٠، ٥:٣٠، ٦:٣٠، ٧:٠٠، ٨:٠٥، ٨:٣٠ م.
- [!] Slot height is 42 here, but the a11y rule says slots are 44 (4638) and r-responsive says ≥44 (4595). **Use 44.**
- [!] The spec (§11) says unavailable slots are *not returned* by the API. The design shows them disabled with a reason, so the API must return blocked slots with a reason code if this UI is kept (or the client decides).

### 2.7 Service row (selectable) & professional card (513–551)

**Service row, selected (515–525):**
- Container: 1.5px `#6D9BCB`, bg `#F8FBFE`, radius 14, padding 14 16, gap 14.
- Leading 22px navy circle with a white check (13px).
- Title 15/700 "باقة شعر ولحية"; description 12.5 `#647484`.
- Trailing block (text-align end): price Inter 700 15 `#10283D` "85" + "ر.س" Tajawal 12 `#647484`; duration 12px "٥٠ دقيقة" (50 minutes).

**Service row, unselected (526–536):** 1px `#E6ECF3` border, and an empty 22px circle with 1.5px `#D4DFEA` border (radio).

**Professional card (538–549):**
- Flex 1, 1px `#E6ECF3`, radius 14, padding 14, centred.
- Avatar 56px circle (striped placeholder), margin-bottom 10.
- Name 14/700; specialty 12 `#647484`.
- Rating row: star `#D89A2E` 14px + Inter 600 12.5 `#3D5266` "4.9" + "(212)" in `#A9B6C4`.
- **Unavailable variant:** opacity .72 and the "في إجازة" (on vacation) chip in place of the rating.

### 2.8 Shop card & appointment card (553–591)

**Shop card:**
- 1px border, radius 14, overflow hidden.
- Cover is 104px tall (placeholder stripes). Top start holds the "مفتوح حتى ١١:٠٠ م" (Open until 11:00 PM) badge. Top end holds a 32px white favourite button (heart 17px).
- Body padding 14 16:
  - Name 15.5/700 + verified shield `#2C5C8C`.
  - Meta row (12.5 `#647484`, gap 10): ★ `#D89A2E` + "4.8" (700 `#3D5266`) · "(٣٤٦ تقييم)" · "حي الملقا" · "2.4 كم" (Inter 600). Separators "·" are `#D4DFEA`.
  - Tag chips follow.
- The data model (3730–3738) adds `from` (starting price), `dist`, the `open` string and `v` (verified). The closed badge applies when the string doesn't start with "مفتوح" (open).

**Appointment card (576–590):**
- Border, radius 14, padding 14 16, gap 14.
- Date block: 52px wide, bg `#EDF3FA`, radius 10, padding 8 0. Day is Inter 700 17 `#10283D` "18"; month is 11/700 `#2C5C8C` "سبتمبر".
- Body: service 14.5/700 + status badge (confirmed); line "صالون الأصالة · فيصل القحطاني" 12.5 `#647484`; time "٥:٣٠ م — ٦:٢٠ م" 12.5/700 `#3D5266`.
- Trailing chevL `#A9B6C4` 18px (the RTL forward arrow).

### 2.9 Rating & review (593–621)

- **Summary:** big "4.8" (Inter 700 32/1 `#10283D`) + 5 stars (13px `#D89A2E`).
- **Distribution bars (3680–3683):**
  - Row: label Inter 600 11 `#8C9BAA`, width 10; track 6px `#F1F5F9` radius 4; count Inter 500 11 `#A9B6C4`, width 28.
  - Fill is `#6D9BCB` for 4–5 stars and `#D4DFEA` for 1–3.
  - Data: 5→268 (78%), 4→52 (15%), 3→15 (5%), 2→7 (2%), 1→4 (1%).
- Divider 1px `#EEF2F7`.
- **Review item:**
  - Initials avatar: 36px, `#EDF3FA`/`#2C5C8C`, 13/700, "م ع".
  - Name 14/700; meta 11.5 `#A9B6C4` "قبل ٣ أيام · باقة شعر ولحية" ("3 days ago · Hair & beard package").
  - Stars 12px; body 13.5/1.85 `#3D5266`.

### 2.10 KPI tiles & chart (623–651)

- **KPI tile:**
  - 1px border, radius 12, padding 14.
  - Label 12.5 `#647484`; value Inter 700 25/1.2 `#10283D`.
  - Delta 12/700: positive `#1F6B48` "+12% عن أمس" ("vs yesterday"), negative `#9B2C2C` "+1.4 نقطة".
  - Examples: مواعيد اليوم 34 (Today's appointments); معدل عدم الحضور 6.2% (No-show rate). For no-show, "+" is bad, so the red colour is semantic, not sign-based.
- **Bar chart "الحجوزات حسب ساعة اليوم" (Bookings by hour of day), "آخر ٧ أيام" (last 7 days):**
  - Height 92, gap 5.
  - Bars: radius 5 5 3 3, min 6px. Values >80% are `#10283D`, others `#B9CBDD`.
  - Labels Inter 500 9.5 `#A9B6C4`.
  - Data: 9:22, 10:34, 11:41, 12:28, 1:18, 2:24, 3:46, 4:62, 5:88, 6:96, 7:74, 8:52.

### 2.11 QR & upload (653–672)

- **QR card:**
  - 96×96 navy tile, radius 12, `#7FA8D4` qr icon at 56px.
  - Title "صالون الأصالة — الملقا"; URL `trimme.sa/s/alasalah-malqa` (Inter 500 12 `#8C9BAA`, LTR, ellipsis).
  - Buttons: تنزيل (Download; 36px navy + download icon) and مشاركة (Share; outline).
  - The URL pattern is `/s/{slug}`.
- **Avatar uploader:** 68px circle, with a 26px navy camera badge at the bottom inline-end and a 2px white ring.
- **Drop zone:**
  - 1.5px dashed `#CBD8E5`, radius 12, padding 14.
  - Text "اسحب صورة الغلاف هنا" ("Drag the cover image here").
  - Constraints: "JPG/PNG · 1600×900 · حتى 4MB" (up to 4MB).

### 2.12 Hours editor & switch (674–692; 3689–3697)

- **Row:** switch + day (width 52, 13.5/700) + time range + trailing edit icon `#A9B6C4`. Row divider `#F1F5F9`, padding 10 0.
- **Switch:**
  - Track 38×22, radius 20, padding 2. On is `#2E9E6B` (knob at flex-end); off is `#DDE4EC`.
  - Knob 18px white with `0 1px 2px rgba(16,40,61,.25)`.
  - Note: `justify-content:flex-end` for "on" puts the knob on the **left** in RTL. This is standard mirrored switch behaviour and is probably correct.
- **Time text:** 13.5; on is `#3D5266` 500, off is `#A9B6C4` 400 "مغلق" (Closed).
- **Demo data:** الأحد/الإثنين ٩:٠٠ ص — ١١:٠٠ م; الجمعة ٢:٠٠ م — ١٢:٠٠ ص (crosses midnight, so support overnight shifts); السبت مغلق.
- **Breaks panel:**
  - bg `#F7F9FC`, radius 12, padding 14.
  - Header coffee icon `#D89A2E` + "الاستراحات" (Breaks).
  - Chips: "صلاة العصر · ٣:٣٠ — ٤:٠٠" (Asr prayer) and "غداء · ١:٠٠ — ١:٤٥" (Lunch), plus an "إضافة" (Add) dashed chip.

### 2.13 Toast, tooltip, dialog (694–713)

- **Toast:**
  - Navy `#10283D`, white, radius 12, padding 13 16, gap 12.
  - Leading 24px green circle `#2E9E6B` with a check.
  - Text 14px "تم تأكيد موعدك وأُرسلت رسالة واتساب" ("Your appointment is confirmed and a WhatsApp message was sent").
  - Close x in `#8FB0D0`.
  - The info variant (3303) uses the info icon in `#7FA8D4`.
- **Tooltip:** bg `#17212B`, white, 12.5px, padding 7 11, radius 9. "هذا الوقت داخل استراحة الحلاق" ("This time is within the barber's break").
- **Confirm dialog:**
  - Border, radius 14, padding 20, shadow `0 18px 40px -12px rgba(16,40,61,.2)`.
  - Icon tile 44px radius 12, `#FBEAEA`/`#C44545` alert 21px.
  - Title 16.5 "إلغاء موعد الخميس ٥:٣٠ م؟" ("Cancel Thursday 5:30 PM appointment?").
  - Body 13.5/1.8 `#647484` "سيُخطر المحل فوراً ويُتاح الوقت لعملاء آخرين. لا يمكن التراجع عن هذا الإجراء." ("The shop will be notified immediately and the time released to others. This cannot be undone.")
  - Two flex-1 44px buttons: solid danger "نعم، ألغِ الموعد" (Yes, cancel) and outline "تراجع" (Go back).

### 2.14 Empty / skeleton / error (715–738; r-states 3180–3234)

- **Empty state (card):**
  - Centred, padding 22.
  - Icon tile 48px, radius 14, `#F1F5F9`/`#8C9BAA`, 23px.
  - Title 15/700 "لا توجد مواعيد قادمة" ("No upcoming appointments"); body 13 `#647484` "احجز حلاقتك القادمة من المحلات القريبة منك" ("Book your next haircut from nearby shops").
  - CTA 42px navy "استكشف المحلات" (Explore shops).
- **Skeleton row:** 52px box (radius 10–11) + two 12px lines (radius 6) at 60%/38%, with the shimmer. The r-states variant uses widths `(70−8i)%` and `(46−6i)%`.
- **Inline error:**
  - Border `#F0DADA`, bg `#FEF8F8`, radius 12, padding 16.
  - Alert icon `#C44545` 20px.
  - Title 14/700 `#9B2C2C` "تعذّر تحميل الأوقات المتاحة" ("Couldn't load available times"); body 13 `#8C6A6A` "تحقق من الاتصال ثم أعد المحاولة. لن يُحجز أي وقت قبل تأكيدك." ("Check your connection and retry. No time will be booked before you confirm.")
  - Retry button: 36px, 1.5px `#E3B4B4`, `#9B2C2C`, refresh icon, "إعادة المحاولة".

### 2.15 Table, breadcrumb, pagination (743–774)

- **Breadcrumb:** 13px `#647484`, gap 8, chevL separators `#C9D4E0` 14px. The current item is `#17212B` 700. Example: الإدارة › المحلات › صالون الأصالة (Admin › Shops › …).
- **Table:**
  - Container 1px border, radius 12.
  - Grid `2fr 1.4fr 1fr 1fr 40px`, gap 12.
  - Header: padding 12 16, bg `#F7F9FC`, bottom border `#E6ECF3`, Inter 700 12 `#647484`. Columns: المحل / الحي / الحلاقون / الاشتراك (Shop / District / Barbers / Subscription).
  - Rows: padding 13 16, divider `#F1F5F9`, 13.5px. Name is 700; district is `#647484`; count is Inter 600 `#3D5266`; subscription is a status badge; the last column is a `more` (kebab) icon `#A9B6C4`.
  - Demo rows: صالون الأصالة/الملقا/6/نشط; باربر هاوس/حطين/4/قرب الانتهاء; لمسة الرجل/النرجس/3/منتهي; صالون الرواد/العليا/8/نشط.
- **Pagination:**
  - Summary "عرض 1–4 من 128 محلاً" ("Showing 1–4 of 128 shops").
  - Buttons are 36×36, radius 9, 1px `#E1E8F0`. The active page is navy/white Inter 700 13.
  - Order in RTL: chevR (previous, disabled `#A9B6C4`), 1, 2, 3, chevL (next).

### 2.16 Header (778–791) & customer bottom nav

- **App header:**
  - 1px border, radius 12, padding 12 14, gap 12.
  - Logo at 30px, then a search field (38px, radius 10, "ابحث" (Search), icon 16).
  - Bell 19px `#3D5266`; initials avatar 32px `#EDF3FA`/`#2C5C8C` 12/700 "ع ش".
- **Bottom nav (3706–3709, 786–790):**
  - Five items: الرئيسية (Home, home), استكشاف (Explore, search), مواعيدي (My appointments, calendar), المفضلة (Favorites, heart), حسابي (My account, user).
  - Each item is a column: icon 20px + label 10.5/700, gap 4, padding 6 0.
  - Active is `#10283D`; inactive is `#98A7B5`. The real screens use min-height 46 (3743).
  - Rule (790): "الشريط السفلي ٥ عناصر بحد أدنى ٤٤×٤٤، والعنصر النشط بلون ونص ثقيل معاً" ("Bottom bar: 5 items, min 44×44; active item uses colour and bold together"). The demo applies weight 700 to all labels, so make active 700 and inactive 500 to honour the rule.

### 2.17 Dashboard sidebar (793–814; 3711–3717)

- Demo width **168px** (the spec'd real width is 264).
- bg `#10283D`, padding 14 10, gap 3.
- Logo 26px centred with `filter:brightness(1.35)`.
- **Items:** padding 8 10, radius 8, 12px, icon 15px, gap 9.
  - Active: `#fff`, bg `rgba(255,255,255,.1)`, 700.
  - Inactive: `#8FB0D0`, 500.
- Demo items: النظرة العامة (Overview), التقويم (Calendar), المواعيد (Appointments), الدوام (Hours), الاشتراك (Subscription).
- **Content area:** bg `#F7F9FC`, top bar 34px (search, bell, avatar 20px), KPI placeholders 52px, panel 84px.
- Note (813): "الشريط على اليمين في العربية ويتحول إلى درج منزلق دون ٩٩٢ بكسل" ("Sidebar is on the right in Arabic and becomes a sliding drawer below 992px"). This conflicts with 1200 in r-responsive.

### 2.18 Foundations-level patterns

| Pattern | Spec | Lines |
|---|---|---|
| Doc hero panel | Navy, radius 16, padding 28 | 80 |
| Info card | White, 1px `#E6ECF3`, radius 14, padding 20 22, E1; icon 18px `#6D9BCB` | 86–97 |
| Numbered assumption tile | bg `#F7F9FC`, border `#EAF0F6`, radius 12; 26px navy number square, radius 8, Inter 700 12 | 108–114 |
| Journey step tile | bg `#F7F9FC`; 22px circle `#6D9BCB` with Inter 700 11 number; icon `#4A7FB5` | 153–160 |
| Rules list on navy | Row bg `rgba(255,255,255,.06)`, radius 10, padding 11 14; tag Inter 500 11.5 `#8FB0D0` on `rgba(255,255,255,.07)` radius 6 | 169–173 |
| Vertical timeline | 18px rail, 10px halo dot, 2px `#E6ECF3` connector | 181–195 |
| Sitemap tree node | Indent `10 + depth×18` px (logical start); depth-0 is 13.5/600 `#17212B` with a 7px `#6D9BCB` dot; children are 400 `#647484` with a 5px `#B9CBDD` dot; forbidden nodes have a `#D9E2EC` dot; meta Inter 500 11 `#A9B6C4` | 3511–3522 |
| Rule card | White, radius 16, icon tile 32–36px radius 10–11 `#EDF3FA`/`#2C5C8C` | 3171, 3280 |
| State card | Header 14 18 + tag eyebrow; body padding 26 20 24 centred; icon tile 46px radius 14 (flat `#F1F5F9`/`#8C9BAA`, warn `#FDF3E2`/`#8A5A12`, bad `#FBEAEA`/`#C44545`); body max-width 36ch; CTA 40px radius 11 in solid/ghost/bad tones | 4599–4611 |

### 2.19 Components referenced but NOT drawn in this scope

Take their anatomy from the screen sections, not from here:

| Component | See |
|---|---|
| 14-day date strip | Booking flow ~1440+ |
| Filter drawer | 1167+ |
| Shop-dashboard appointment detail drawer | s-appointments, ~3776–3781 overlay/drawer styles |
| Booking stepper/progress | ~1440+; progress width transition at 3978 |
| Walk-in form | s-walkin |
| Map view | 1127+ |
| WhatsApp message preview | 1601+ |
| Bottom sheet | ~4055–4059 |
| Admin location picker | Spec §8 |

---

## 3. Icon set (ICONS, 3319–3374)

- Custom SVG, 24×24 viewBox, `fill:none`, `stroke:currentColor`, **stroke-width 1.75**, round caps and joins, sized `1em`, so the icon size follows the font-size of its wrapper.
- Foundations (310): "عائلة خطية بسماكة ١.٧٥ ومقاسات ١٦ / ٢٠ / ٢٤. الأيقونات الاتجاهية تُعكس في RTL" ("Line family, stroke 1.75, sizes 16/20/24. Directional icons mirror in RTL").
- The handoff (4646) explicitly says **Lucide at 1.75 stroke, 3 sizes only**. The icons actually render at 13–23px, and 56px on the QR tile. Normalise to 16/20/24.

46 icons:

| Key | Lucide | Key | Lucide |
|---|---|---|---|
| home | `House` | settings | `Settings` (drawn as a sun-like circle with 8 spokes; semantically settings) |
| search | `Search` | users | `Users` |
| calendar | `Calendar` | store | `Store` |
| heart | `Heart` | grid | `LayoutGrid` |
| bell | `Bell` | alert | `TriangleAlert` |
| user | `User` | trash | `Trash2` |
| pin | `MapPin` | edit | `Pencil` |
| star | `Star` (filled for ratings) | phone | `Phone` |
| clock | `Clock` | more | `EllipsisVertical` |
| scissors | `Scissors` | logout | `LogOut` |
| chevR | `ChevronRight` ↔ mirror | pause | `Pause` |
| chevL | `ChevronLeft` ↔ mirror | info | `Info` |
| chevD | `ChevronDown` | camera | `Camera` |
| x | `X` | download | `Download` |
| check | `Check` | shield | `Shield` (or `ShieldCheck` for "verified shop", 563) |
| plus | `Plus` | chart | `ChartColumn` |
| filter | `ListFilter` | ban | `Ban` |
| qr | `QrCode` | coffee | `Coffee` |
| msg | `MessageCircle` | plane | `Plane` (drawn as a send/paper-plane shape; means vacation) |
| card | `CreditCard` | list | `List` |
| eyeOff | `EyeOff` | layers | `Layers` |
| refresh | `RefreshCw` | book | `BookOpen` |
| play | `Play` | tag | `Tag` |

- The foundations icon grid (3614) shows 28 of them: home, search, calendar, clock, pin, star, heart, bell, user, users, store, scissors, msg, qr, filter, settings, shield, card, coffee, plane, pause, ban, check, alert, edit, trash, chart, tag.
- **RTL:** the design uses chevL as "forward/next/open" in RTL (589, 746, 771) and chevR as "back/previous" (472, 767). The LTR card flips to chevR (3255).
  - Implement `ChevronForward` / `ChevronBack` wrappers (`rtl:-scale-x-100`) rather than hard-coding left or right.
  - Stars need `fill: currentColor`: the prototype's star is outline-only, but the ratings read as filled.

---

## 4. Documentation screens content

### 4.1 doc-intro (77–119)

**Hero (navy), eyebrow "PRODUCT READING".**
- Title: "تريمي ليس تطبيق حجز فحسب، بل طبقة جدولة موثوقة فوق صالونات مستقلة". Translation: "TRIMME is not just a booking app, but a reliable scheduling layer over independent salons."
- Body: "القيمة الحقيقية ليست في قائمة المحلات، بل في أن الوقت المعروض قابل للحجز فعلاً. لذلك بُني النظام حول محرك إتاحة واحد يجمع دوام المحل، دوام الموظف، مدة الخدمة، الحجوزات القائمة، الاستراحات، والإجازات — ولا يعرض للعميل أي وقت لا يمكن تأكيده." Translation: "The real value isn't the shop list, but that every displayed time is actually bookable. The system is built around a single availability engine combining shop hours, staff hours, service duration, existing bookings, breaks and vacations — and never shows the customer a time that can't be confirmed."

**Three principle cards:**
1. **الوقت هو المنتج** ("Time is the product"), clock icon. "The time-selection screen is the most important screen in the whole product and gets the highest visual budget: morning/evening periods, visible service duration, and a clear reason for every disabled time."
2. **واتساب هو قناة الثقة** ("WhatsApp is the trust channel"), msg icon. "لا يوجد دفع إلكتروني في النسخة الأولى" ("No online payment in v1"). Trust is built through instant confirmation, a pre-appointment reminder, and delivery status visible to admins.
3. **صلاحيات محكمة** ("Tight permissions"), shield icon. "المحل ينفّذ ولا يدير: لا ينشئ موظفين ولا يغيّر الأسعار، ولا يرى رقم العميل إطلاقاً. المنع مصمَّم كرسالة واضحة لا كخلل." Translation: "The shop executes, it doesn't administer: it doesn't create staff **or change prices**, and never sees the customer's number. Restrictions are designed as clear messages, not bugs." [!] The price clause conflicts with the spec; see §7.

**"الافتراضات الموثّقة" (Documented assumptions)**, subtitle "اتُخذت لسد الفجوات في المتطلبات — قابلة للمراجعة مع العميل" ("made to fill requirement gaps — reviewable with the client"). Data at 3500–3509.

| # | Title (AR) | English | Detail |
|---|---|---|---|
| ١ | الحساب برقم الجوال + رمز OTP | Account = mobile number + OTP | No passwords in v1; the same number is used for WhatsApp. [!] See §7 |
| ٢ | مدة الخدمة تحدد طول الفتحة | Service duration defines slot length | Grid computed at a 5-minute step, then filtered to slots that fit the full duration before the next booking |
| ٣ | الإلغاء متاح حتى ساعتين قبل الموعد | Cancellation allowed up to 2h before | After that the button becomes «طلب إلغاء» (Request cancellation) and notifies the shop instead of cancelling instantly |
| ٤ | التقييم يفتح بعد «مكتمل» فقط | Rating opens only after Completed | 7-day window, one rating per appointment, attributed to both the barber and the shop |
| ٥ | الاشتراك يوقف الظهور لا البيانات | Subscription expiry stops visibility, not data | On expiry the shop disappears from discovery; its existing appointments remain valid |
| ٦ | رقم العميل مخفي عن المحل نهائياً | Customer number permanently hidden from shop | Contact goes through platform WhatsApp; the UI explains why instead of showing an empty field |
| ٧ | خدمة واحدة لكل موعد في v1 | One service per appointment in v1 | Packages modelled as one service with a combined duration (e.g. hair & beard — 50 min). [!] See §7 |
| ٨ | الحجز الحضوري يخصم من نفس الشبكة | Walk-ins consume the same grid | What the shop records manually blocks the time for customers immediately |

### 4.2 doc-sitemap (121–144; data 3511–3550)

Three columns. Depth is shown by indent: "↳" marks a child and "↳↳" a grandchild. Meta text follows in parentheses. ✗ marks a "forbidden" node (grey dot).

**العميل — CUSTOMER · MOBILE FIRST** (user icon)
- الرئيسية Home (قريب منك · خدمات · حلاقون — near you · services · barbers)
- استكشاف Explore (بحث + فلاتر — search + filters)
  - ↳ نتائج البحث Search results
  - ↳ عرض الخريطة Map view
  - ↳ صفحة المحل Shop page
    - ↳↳ الحلاق Barber
    - ↳↳ الخدمة Service
- مسار الحجز Booking flow (٦ خطوات — "6 steps" [!] the journey says 7)
  - ↳ خدمة ← حلاق ← تاريخ ← وقت (service → barber → date → time)
  - ↳ المراجعة والتأكيد Review & confirm
- المواعيد Appointments (قادمة · سابقة — upcoming · past)
  - ↳ تفاصيل الموعد Appointment details (إلغاء · إعادة جدولة — cancel · reschedule)
  - ↳ التقييم Rating (بعد الاكتمال — after completion)
- المفضلة Favorites
- الإشعارات Notifications
- الحساب والإعدادات Account & settings (اللغة · الخصوصية — language · privacy)
- صفحة QR QR page (دخول مباشر — direct entry)

**المحل — SHOP · DASHBOARD** (store icon)
- النظرة التشغيلية Operational overview (مؤشرات اليوم — today's KPIs)
- التقويم Calendar (يوم · أسبوع · حلاق — day · week · barber)
- المواعيد Appointments (فلاتر + حالة — filters + status)
  - ↳ تفاصيل الموعد Details (درج جانبي — side drawer)
    - ↳↳ تغيير الحالة Change status
- حجز حضوري Walk-in booking
- ساعات الدوام Working hours
  - ↳ الاستراحات Breaks
  - ↳ الإجازات والتعطيل Vacations & closures
  - ↳ إيقاف الحجز مؤقتاً Pause booking temporarily
- الخدمات المتاحة Available services (تفعيل فقط — "activation only" [!] conflicts with spec)
- الاشتراك Subscription (الحالة والانتهاء — status & expiry)
- الإشعارات Notifications
- إعدادات المحل Shop settings
  - ↳ ✗ إنشاء أو حذف حلاق Create/delete barber — ممنوع (forbidden) ✓ matches spec
  - ↳ ✗ تعديل الأسعار Edit prices — ممنوع (forbidden) [!] conflicts with spec
  - ↳ ✗ رقم العميل Customer number — مخفي (hidden) ✓ matches spec

**إدارة المنصة — PLATFORM ADMIN** (shield icon)
- نظرة عامة Overview (مؤشرات تشغيلية — operational KPIs)
- المحلات Shops (إضافة · تعديل · حساب — add · edit · account)
  - ↳ تفاصيل المحل Shop details
  - ↳ اشتراك المحل Shop subscription (تجديد · سجل — renew · history)
- الحلاقون Barbers (إضافة · تعطيل — add · disable)
  - ↳ ~~نقل حلاق بين المحلات~~ Transfer barber between shops. **[Do not build] Must not be built or displayed** (spec §7)
  - ↳ إسناد الخدمات للحلاق Assign services to barber
- الخدمات العامة Global services (سعر · مدة — price · duration). **[Do not build] Global price/duration must not be built** (spec §10). Re-scope as categories plus moderation
  - ↳ إسناد الخدمات للمحلات Assign services to shops. [Do not build] Same re-scope (services are shop-owned)
- المواعيد Appointments (كل المنصة — platform-wide)
- العملاء Customers (الملف والسجل — profile & history)
- الاشتراكات Subscriptions
- التقييمات Reviews (مراجعة · إخفاء — review · hide)
- رموز QR QR codes (توليد + تحليلات — generate + analytics)
- رسائل واتساب WhatsApp messages (سجل الحالة — status log)
- إعدادات النظام System settings
- الأدوار والصلاحيات Roles & permissions
- سجل النشاط Activity log

Missing versus the spec (not in the sitemap):
- Customer: forgot/reset password and security/session settings (§12).
- Shop: service CRUD, professional schedule visibility, public-profile/location editing (§13).
- Admin: SuperAdmin subscription-plan catalogue and pricing, WhatsApp template editor with versions, platform settings items (§14).

### 4.3 doc-journey (146–200; data 3552–3581)

- Title: "رحلة الحجز الأساسية — من الاكتشاف إلى التقييم" ("Core booking journey — from discovery to rating").
- Subtitle: "سبع خطوات، ثلاث نقرات جوهرية. الهدف: أقل من ٤٥ ثانية من فتح المحل إلى تأكيد الموعد" ("Seven steps, three essential taps. Goal: under 45 seconds from opening the shop to confirming").

| # | Step | Description |
|---|---|---|
| ١ | الاكتشاف Discovery (pin) | Nearby shops sorted by distance and rating, or direct entry via QR |
| ٢ | صفحة المحل Shop page (store) | Open status, distance, services with price and duration, barbers, reviews |
| ٣ | الخدمة Service (scissors) | One choice sets the appointment duration and therefore the time grid |
| ٤ | الحلاق Barber (user) | «أي حلاق متاح» ("Any available barber") is the **default option**, increasing the chance of an early slot |
| ٥ | التاريخ Date (calendar) | **14-day strip**; closed days are disabled with a visible reason |
| ٦ | الوقت Time (clock) | Morning/noon/evening periods (صباح/ظهر/مساء) with flexible steps; booked slots disabled with an explanation. [!] ds-components shows only AM/PM groups |
| ٧ | التأكيد Confirmation (check) | Full summary, then instant WhatsApp confirmation and add-to-calendar |

**"محرك الإتاحة — ما الذي يحذف وقتاً من الشبكة؟"** (Availability engine — what removes time from the grid?), 3562–3570:

| Rule | Tag |
|---|---|
| دوام المحل — Shop hours | أساس (base) |
| دوام الحلاق داخل دوام المحل — Barber hours within shop hours | تقاطع (intersection) |
| مدة الخدمة + وقت التجهيز — Service duration + prep time | طول الفتحة (slot length) |
| المواعيد القائمة والحجوزات الحضورية — Existing appointments & walk-ins | حذف (remove) |
| الاستراحات المتكررة — Recurring breaks | حذف (remove) |
| الإجازات والتعطيل المؤقت — Vacations & temporary closure | حذف اليوم (remove the day) |
| إيقاف الحجز الإلكتروني — Pause online booking | إخفاء المحل (hide the shop) |

**"دورة حياة الموعد"** (Appointment lifecycle), subtitle "كل انتقال يطلق رسالة واتساب مقابلة، ويُسجَّل في سجل النشاط" ("Every transition fires a matching WhatsApp message and is logged in the activity log"). Data at 3574–3581.

| Status | Actor | Note |
|---|---|---|
| بانتظار التأكيد Pending | العميل يحجز (customer books) | Appears instantly in the shop dashboard with a **sound alert**; the time is held temporarily |
| مؤكد Confirmed | المحل (shop) | WhatsApp to the customer + automatic reminder **3 hours before** [!] spec says 30 min |
| حضر العميل Arrived | المحل | Used to measure punctuality; **sends no message** [!] contradicts "every transition" |
| مكتمل Completed | المحل أو تلقائياً (shop or automatic) | Opens the rating window for **7 days** |
| ملغي Cancelled | العميل أو المحل (customer or shop) | Time released instantly; the other party is notified with the reason, so a **cancellation reason field is implied** |
| لم يحضر No-show | المحل بعد ١٥ دقيقة (shop, after 15 min) | Counted in the no-show rate on the admin dashboard |

### 4.4 Script bullet lists at the end (4587–4651)

- **respCards** (demo shop appointments):
  - ٥:٣٠ م عبدالله الشمري · باقة شعر ولحية · فيصل · مؤكد · source التطبيق (app)
  - ٦:٣٠ م خالد الدوسري · تهذيب لحية · فيصل · بانتظار التأكيد · التطبيق
  - ٧:١٥ م وليد العمري · حلاقة أطفال · راكان · مؤكد · حضوري (walk-in)
  - A booking **source** field (app / walk-in) is implied.
- **respRules**, **stateCards**, **contrastRows** and **a11yRules** are covered in §5.
- **handoff**, "قائمة تسليم للمطورين" (developer handoff checklist), 4644–4651:
  1. "رموز التصميم كمتغيرات CSS منطقية (margin-inline-start) لا يمين/يسار" (Design tokens as logical CSS variables such as margin-inline-start, not left/right).
  2. "الأيقونات من عائلة Lucide بسماكة ١.٧٥ وثلاثة مقاسات فقط" (Lucide icons, stroke 1.75, three sizes only).
  3. "كل حالة موعد لها لون ونص ثابتان عبر الأدوار الثلاثة" (Each appointment status has a fixed colour and label across all three roles).
  4. "محرك الإتاحة عقد واحد: إدخال (خدمة، حلاق، تاريخ) وإخراج قائمة أوقات قابلة للحجز" (The availability engine is one contract: input service, barber and date; output a list of bookable times).
  5. "رقم العميل لا يصل إلى واجهة المحل من الخادم أصلاً — المنع على مستوى العقد لا الواجهة" (The customer number never reaches the shop UI from the server; enforcement is at the contract level, not the UI). ✓ Matches spec §7.
  6. "قوالب رسائل واتساب مركزية وقابلة للتعديل من إعدادات النظام" (WhatsApp templates are central and editable from system settings).
- The admin activity-log demo (4579–4585, just before scope) includes [!] "نقل حلاق" (barber transfer) and [!] "تعديل سعر خدمة … ٤٨ محلاً متأثراً" (service price change affecting 48 shops). It also includes "اطلاع على رقم عميل" (support viewed customer number CU-10482 to handle a complaint), which is an audited admin view and is fine. See §7.

---

## 5. Review screens

### 5.1 Responsive rules (r-responsive 3114–3178)

Title: "نفس الشاشة عبر ثلاث مقاسات" ("Same screen across three sizes"). The example is the shop appointments list: "بطاقات على الجوال، جدول مضغوط على اللوحي، جدول كامل مع درج تفاصيل على سطح المكتب" ("cards on mobile, compact table on tablet, full table with details drawer on desktop").

**MOBILE · 390** (demo width 230):
- Stacked cards on `#F7F9FC`. Each card has time 12.5/700 + compact badge, customer 12/700, and "service · barber" 11 `#647484`.
- Rule: "الجدول يتحول إلى بطاقات — لا تمرير أفقي إطلاقاً" ("The table becomes cards — never horizontal scrolling").

**TABLET · 768** (demo width 340):
- Grid `.8fr 1.4fr 1fr` with columns الوقت / العميل / الحالة (Time / Customer / Status).
- The service sits under the customer name at 10.5 `#8C9BAA`.
- Rule: "عمود الحلاق يُدمج تحت اسم العميل، والشريط الجانبي يصبح درجاً منزلقاً" ("The barber column merges under the customer name; the sidebar becomes a sliding drawer").

**DESKTOP · 1440:**
- Grid `.8fr 1.4fr 1fr 1fr .8fr` with columns الوقت / العميل والخدمة / الحلاق / الحالة / المصدر (Time / Customer & service / Barber / Status / Source).
- Rule: "جدول كامل + درج تفاصيل ينزلق من اليمين دون مغادرة الصفحة" ("Full table + details drawer sliding in from the right without leaving the page"). "From the right" is RTL-physical; implement it as inline-start.

**Rule cards (4592–4596):**
1. **نقاط الانكسار** (Breakpoints): ≥1200 full dashboard with a fixed sidebar · 768–1199 sliding drawer · <768 bottom bar and cards.
2. **الجداول لا تنزلق أفقياً** (Tables never scroll horizontally): below 768px each row becomes a card with title + status + one primary action.
3. **التقويم على اللمس** (Calendar on touch): one barber at a time on mobile, with horizontal swipe between barbers; slot height ≥44px.
4. **أولوية الجوال للعميل** (Mobile-first for customers): every customer screen was designed at 390 first; desktop is an expansion, not the reverse.

### 5.2 States catalogue (r-states 3180–3234; data 4612–4625)

| Title | Tag | Icon / tone | Heading | Body | CTA (style) |
|---|---|---|---|---|---|
| لا محلات قريبة (No nearby shops) | EMPTY | pin / flat | لا توجد محلات ضمن ٥ كم (No shops within 5 km) | وسّع نطاق البحث أو غيّر الحي لعرض نتائج أكثر (Widen the search radius or change the district for more results) | توسيع النطاق إلى ١٥ كم (Expand to 15 km), solid |
| لا مواعيد قادمة (No upcoming appointments) | EMPTY | calendar / flat | جدولك فارغ حالياً (Your schedule is currently empty) | احجز حلاقتك القادمة من محلاتك المفضلة بنقرتين (Book your next cut from your favourite shops in two taps) | استكشف المحلات (Explore shops), solid |
| لا أوقات في هذا اليوم (No times this day) | EMPTY | clock / warn | اليوم ممتلئ بالكامل (The day is fully booked) | كل أوقات الخميس محجوزة لدى فيصل. أقرب وقت متاح: الجمعة ١٠:٠٠ ص (All of Thursday is booked with Faisal. Next available: Friday 10:00 AM) | انتقل إلى الجمعة (Go to Friday), ghost. Implies a **"next available slot" API** |
| انقطاع الاتصال (Connection lost) | ERROR | alert / bad | تعذّر تحميل الأوقات (Couldn't load times) | لم يُحجز أي وقت. تحقق من الاتصال ثم أعد المحاولة (No time was booked. Check your connection and retry) | إعادة المحاولة (Retry), bad |
| صلاحية مرفوضة (Permission denied) | PERMISSION | eyeOff / flat | رقم العميل غير متاح لحسابك (Customer number isn't available to your account) | حسابات المحلات لا ترى أرقام العملاء. التواصل يتم عبر واتساب المنصة تلقائياً (Shop accounts don't see customer numbers. Contact happens via platform WhatsApp automatically) | طلب تواصل عبر المنصة (Request contact via platform), ghost [!] unspecified feature |
| الاشتراك منتهي (Subscription expired) | BLOCKED | card / warn | محلك مخفي من الاكتشاف (Your shop is hidden from discovery) | المواعيد القائمة سارية، لكن لا تُقبل حجوزات جديدة حتى يُجدَّد الاشتراك (Existing appointments stay valid, but no new bookings are accepted until renewal) | تواصل مع الإدارة (Contact admin), solid |
| تحميل — هيكل عظمي (Loading — skeleton) | SKELETON | — | — | "الهيكل يطابق شكل المحتوى القادم بالضبط، ومدته القصوى ٣ ثوانٍ قبل التحول إلى حالة خطأ" (Skeleton exactly matches the incoming content shape; **max 3 seconds** before switching to an error state) | — |
| تحقق الحقول (Field validation) | VALIDATION | — | Phone error / name valid (see §2.2) | — | — |

- **Not covered by the design** but required by spec §5: offline/network banner, expired session, 404/not-found, optimistic-action rollback, 403 page-level.
- The ds-components set adds the inline times-load error (730–737) and the "no upcoming appointments" empty state (717–722).

### 5.3 Accessibility & RTL review (r-a11y 3237–3299)

**Direction mirroring demo (3241–3258).** The same appointment card is shown in AR·RTL (date block at the start, chevL at the end, "سبتمبر") and in EN·LTR (Inter, "SEP", "Hair & Beard Package", "5:30 PM — 6:20 PM", chevR). Rule: "السهم ينعكس، والتاريخ يبقى في بداية السطر المنطقية، والأرقام اللاتينية تُعرض بخط Inter في الحالتين" ("The arrow mirrors, the date stays at the logical line start, and Latin numerals render in Inter in both").

**Contrast table (4629–4635)**, with the design's claims versus computed WCAG 2.x values:

| Pair | Design claim | Computed | Verdict |
|---|---|---|---|
| `#17212B` on `#F7F9FC` | 13.4:1 AAA (also at 250) | **15.45:1** | AAA ✓ (claim understated) |
| `#647484` on white | 5.2:1 AA | **4.80:1** | AA ✓ (on `#F7F9FC` 4.55:1, just passes) |
| White on `#10283D` | 14.8:1 AAA | **15.08:1** | AAA ✓ |
| `#2C5C8C` on `#EDF3FA` | 5.6:1 AA | **6.23:1** | AA ✓ |
| White on `#2C5C8C` (251) | 6.3:1 | **6.96:1** | AA ✓ |
| `#6D9BCB` as text on white | 2.6:1, "أسطح فقط" (surfaces only) | **2.92:1** | ✗ surfaces only (correct call) |

Additional computed checks (not in the design):

| Pair | Ratio | Use |
|---|---|---|
| `#3D5266` / white | 8.09 | Text OK |
| `#5E6E7E` / white | 5.24 | Text OK |
| `#4A7FB5` / white | **4.20 ✗** | Global link colour (line 20) fails AA for body text. Use `#2C5C8C` for links |
| `#8C9BAA` / white | **2.84 ✗** | Helper text, breadcrumb |
| `#9AA8B6` / white | **2.43 ✗** | Eyebrows, weekday heads |
| `#A9B6C4` / white | **2.06 ✗** | Meta, counts, placeholders |
| `#98A7B5` / white | **2.46 ✗** | Inactive bottom-nav labels |
| `#D89A2E` / white | 2.45 | Stars and break icon only. Below the 3:1 non-text threshold, so pair with the numeric rating (already done) |
| White on `#2E9E6B` | 3.38 | Icon only, OK for graphics ≥3:1 |
| `#C44545` / white | 4.90 | OK; white on `#C44545` 4.90 OK |
| `#8C6A6A` / `#FEF8F8` | 4.56 | Just passes |
| On-navy `#AEC4DA` 8.41, `#7FA8D4` 6.07, `#8FB0D0` 6.67 | — | OK |
| Disabled `#A9B6C4`/`#EEF2F7` 1.84, `#AEBAC6`/`#F1F4F7` 1.79, `#C2CCD6`/white 1.63 | — | Exempt as disabled, but past/closed dates still convey information. Pair them with line-through (already done) and aria |

**Rule cards (4636–4643):**
1. **الحالة بأكثر من إشارة** (Status with more than one cue): each status badge = colour + dot + explicit Arabic text.
2. **أهداف لمس ≥ ٤٤ بكسل** (Touch targets ≥44px): time slots 44px, icon buttons 46×46, spacing between tappables ≥8px.
3. **ترتيب التركيز منطقي** (Logical focus order): keyboard navigation follows right-to-left reading order, with a clear 3px focus ring on every element.
4. **تسميات للقارئ الصوتي** (Screen-reader labels): icon-only buttons carry an Arabic aria-label (e.g. «إضافة للمفضلة», "Add to favourites"); states are announced via aria-live.
5. **الوقت بصيغة ١٢ ساعة عربية** (12-hour Arabic time): "٨:٣٠ م" in Arabic-Indic digits; dates are Gregorian with the day name to avoid ambiguity.
6. **المعطّل مشروح لا صامت** (Disabled is explained, not silent): every disabled element shows its reason on touch instead of ignoring the tap, and uses aria-disabled rather than being hidden.

**RTL/physical-property bugs found in the prototype (fix in the rebuild):**
- Active nav indicator `box-shadow: inset 3px 0 0 #6D9BCB` (3491) is physical and always sits on the left edge regardless of direction (prototype shell only). Pick an edge (start or end) and implement it logically, e.g. a pseudo-element with `inset-inline-start:0`.
- Toast centring `inset-inline-start:50%; transform:translateX(50%)` (3303) works only in RTL. Use `left:50%; translate:-50%`, or a flex-centred container.
- The "drawer slides from the right" copy (3164) must be implemented as inline-start/end logical, not hard-coded right.
- The shell `direction:rtl` is hard-coded on a div (33). Set `dir` on `<html>` per locale instead.

---

## 6. Logo usage

| Where | Height | Treatment | Line |
|---|---|---|---|
| Prototype nav header (not product) | 52px, centred, mb 10 | — | 37 |
| Foundations "LOGO · ON LIGHT" | 82px in a 150px tile on `#F7F9FC` | — | 209 |
| Foundations "LOGO · ON NAVY" | 82px on `#10283D` | `filter: brightness(1.35) saturate(.85)` | 216 |
| Component: app header | 30px | — | 780 |
| Component: dashboard sidebar (navy) | 26px, centred | `brightness(1.35)` | 797 |
| (outside scope) landing header | 38px | — | 827 |
| (outside scope) auth screen | 58px | — | 923 |
| (outside scope) dark footer or hero | 34px | `brightness(1.4)` | 2089 |
| (outside scope) shop and admin sidebars | 36px | `brightness(1.35)` | 2121, 2664 |

**Usage rules:**
- "هامش حماية = ارتفاع حرف T. الحد الأدنى للعرض ٩٦ بكسل في الويب و٢٤ ملم في الطباعة" (211). Translation: "Clear space = height of the letter T. Minimum width 96px on web, 24mm in print."
  - [!] At 26–38px tall, the header and sidebar sizes may fall below 96px width, depending on the logo's aspect ratio. Check `trimme-logo.png` dimensions.
- "على الداكن يُفتَّح الشعار درجة واحدة للحفاظ على تباين ٤.٥:١. لا يُستخدم الشعار فوق صورة مباشرة دون طبقة تعتيم" (218). Translation: "On dark, the logo is lightened one step to keep 4.5:1 contrast. Never place the logo directly on a photo without a dimming layer."
  - **Recommendation:** export a proper light/on-dark SVG variant instead of relying on a CSS filter.
- **App mark (221–231):**
  - A square tile in brand `#6D9BCB` with a white "T" (Inter 800 40px, letter-spacing −.02em).
  - "رمز التطبيق يستخدم حرف T فقط بخلفية الأزرق الأساسي" ("The app icon uses the letter T only, on the primary blue background").
  - The WhatsApp avatar uses a white circle with a navy "T" (outside scope, 1608).
- **Don'ts (ممنوعات):**
  - تغيير النِسب أو المسافات (changing proportions or spacing).
  - ألوان خارج الهوية (off-brand colours).
  - ظل أو تدرّج على الشعار (shadow or gradient on the logo).
  - استخدام الرمز دون الكلمة في الرأسيات (using the symbol without the wordmark in headers).
- Logo asset: `design/source/trimme-logo.png` (raster only; `alt="TRIMME"`).

**Imagery direction (318–327):**
- "إضاءة دافئة، خلفيات خشب ونحاس، لقطات قريبة للحرفة لا لقطات مخزون عامة" ("Warm lighting, wood and copper backdrops, close-ups of the craft, not generic stock").
- Ratios: **16:9 shop cover** (upload spec 1600×900, ≤4MB, JPG/PNG), **1:1 barber**, **4:3 service**.
- Text on an image uses a 40% navy overlay.

---

## 7. Conflicts with the spec's business rules (and internal inconsistencies)

### 7.1 Must NOT be built or displayed

| # | Design (line) | Spec | Action |
|---|---|---|---|
| C1 | Barber **transfer between shops**: GROUPS title "الحلاقون والنقل والخدمات" (3410); sitemap node "نقل حلاق بين المحلات" (3544); activity-log entry "نقل حلاق… من صالون الأصالة إلى باربر هاوس" (4580) | §7: "There is no 'transfer barber between shops' feature. Do not implement, display, document, or scaffold…"; §14: "no transfer action" | Remove from nav title, sitemap, admin screens and audit-log seed. Each professional has a fixed single shop |
| C2 | **Global service price and duration**: GROUPS "الخدمات العامة والإسناد" (3411); sitemap "الخدمات العامة (سعر · مدة)" + "إسناد الخدمات للمحلات" (3545); activity log "تعديل سعر خدمة «عناية بالوجه» من 65 إلى 70 ر.س — ٤٨ محلاً متأثراً" (price change affecting 48 shops) (4581) | §10: services are tenant-owned; "no global price or global duration that silently overwrites a shop's values"; §7: admin manages categories and packages "without imposing a shared service price or duration" | Re-scope the admin screen to categories, package definitions, moderation, audited per-shop override and professional-service assignment. Remove the global price edit and the "N shops affected" pattern |
| C3 | **Shop cannot change prices or services**: doc-intro "لا ينشئ موظفين ولا يغيّر الأسعار" (96); sitemap "الخدمات المتاحة — تفعيل فقط" (activation only) (3537) and "تعديل الأسعار — ممنوع" (3539) | §7/§10/§13: the shop creates, edits, prices, orders, activates and archives its **own** services | Shop dashboard needs full service CRUD (localized name/description, price, duration, active, order, archive). Drop the "prices forbidden" messaging. Keep "cannot create/delete barbers" |

### 7.2 Conflicts to resolve

| # | Design (line) | Spec | Note |
|---|---|---|---|
| C4 | Assumption 1: phone + OTP only, "لا كلمات مرور في النسخة الأولى" (no passwords in v1) (3501) | §9 ASP.NET Identity with password security/lockout; §12 "forgot/reset password" | Decide the auth model; the spec implies passwords exist. The OTP phone verification can stay for WhatsApp |
| C5 | Reminder "تذكير تلقائي قبل ٣ ساعات" (automatic reminder 3h before) (3576) | §16: customer **and professional** reminders exactly **30 minutes** before | Use the configurable reminder offset (§14 platform settings), default 30 min |
| C6 | Lifecycle mentions only customer WhatsApp (3575–3580); handoff item 6 (4650) says "central templates editable from system settings" | §16: separate versioned AR/EN templates per audience (customer vs **professional**), professional alerts and reminders | Add professional notifications and a template editor with versions |
| C7 | Single "ملغي" (Cancelled) status (3638) | §11: side transitions to **customer cancellation** vs **shop cancellation** | Keep one visual chip if desired, but store two enum values (show the actor in details) |
| C8 | Assumption 7: one service per appointment; packages are "one service with a combined duration" (3507) | §10: packages have duration and price and **expand into configured service items** for reporting | Model packages with items; the booking can still be a single line |
| C9 | "تغيير الحلاق" (Change barber) in the appointment context menu (461) | §7: shop may update operational booking states; not explicitly allowed to reassign the professional | Not a transfer, but needs a decision. If kept, it must re-run availability and the exclusion constraint |
| C10 | Permission state CTA "طلب تواصل عبر المنصة" (Request contact via platform) (4617) | Not in spec | Unspecified feature; decide or drop. Never reveal the phone |
| C11 | Unavailable slots shown disabled with a reason (508, 3673, rule 4642) | §11: "The API returns only genuinely bookable slots. Past, conflicting… must not be returned as available" | Compatible only if the API returns blocked slots flagged `available:false` with a reason. Otherwise drop the disabled slots |
| C12 | `#4A7FB5` link colour (20) and `#8C9BAA`/`#9AA8B6`/`#A9B6C4`/`#98A7B5` used for real text (410, 478, 605, 615, 3708, sitemap meta) | §5: meet WCAG AA contrast | Map tertiary and meta text to a ≥4.5:1 value (`#647484` = 4.80:1 or darker), or restrict these colours to decorative or disabled uses |

### 7.3 Design-invented policy values that must become settings (spec: configurable)

| Value | Line |
|---|---|
| Cancellation cutoff **2h** → then "طلب إلغاء" (request cancellation) | 3503 |
| Rating window **7 days**, one per booking, attributed to barber and shop | 3504, 3578 |
| No-show marking after **15 min** | 3580 |
| Booking horizon **14 days** | 3557 |
| Slot step **5 min** base, "5/10/15" | 3502, 493 |
| **Prep time** added to duration | 3565 |
| **Pause online booking hides the shop** | 3569 |
| Pending "holds time temporarily" plus a sound alert | 3575 |
| Subscription expired: hidden from discovery, no new bookings, existing kept | 3505, 4618 |
| Default nearby radius 5 km → expand to 15 km | 4613 |
| Skeleton max 3s before error | 3212 |

Spec anchors: §11 lead time, horizon and slot step; §14 booking policy and reminder offset; §15 enforcement explicit in settings.

### 7.4 Consistent with spec (no action)

- No online payment (92) ✓ §2.
- Customer phone hidden from shops, enforced at the contract level (3506, 3539, 4617, 4649) ✓ §7.
- Shop cannot create or delete barbers (3539) ✓.
- Walk-ins use the same grid (3508) ✓ §11.
- Subscription statuses Active/ExpiringSoon/Expired/Suspended (3640–3643) ✓ §15.
- Booking statuses Pending → Confirmed → Arrived → Completed plus side states ✓ §11.
- No customer export appears anywhere in these ranges ✓ §7/§13.
- Admin viewing a customer number is audited (4583) ✓ §14 "protected contact data".
- Lucide icons ✓ §3.
- Booking order service → barber → date → time → review → confirm ✓ §2.

### 7.5 Internal inconsistencies in the design (decide once)

| Topic | Values | Lines |
|---|---|---|
| Drawer breakpoint | 768 (foundations) · <992 (sidebar note) · 768–1199 (responsive rule) | 357, 813, 4593 |
| Dashboard sidebar width | 264 (spec'd); 168 in the demo; 272 in the prototype shell | 364, 796, 35 |
| Booking step count | "٦ خطوات" (6 steps) in the sitemap vs 7 in the journey | 3528 vs 150, 3552 |
| Time periods | AM/PM (صباحاً/مساءً) vs morning/noon/evening (صباح/ظهر/مساء) | 88, 494–500 vs 3558 |
| WhatsApp on every transition | "كل انتقال يطلق رسالة" (every transition sends a message) vs Arrived sends nothing | 179 vs 3577 |
| Slot height | 42 (component) vs ≥44 (a11y and responsive rules) | 3671 vs 4595, 4638 |
| Distance digits | `٢٫٤ كم` vs `2.4 كم` | 3606 vs 567 |
| Demo weekday | "الخميس 18 سبتمبر" (Thursday), but 18 Sep 2026 is a **Friday** | 273, 706 |
| Focus style | 3px double ring (button) vs border + 3px soft ring (input) | 3624 vs 406 |
| Font weights | Tajawal 600 and Inter 800 used but not loaded; IBM Plex Sans Arabic fallback not loaded | 3516, 223, 16, 19 |
| Unused | `fadeUp` keyframes | 28 |
| Bottom-nav rule | "Active = colour + bold", but all labels are 700 in the demo | 790 vs 787 |
