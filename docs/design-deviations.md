# TRIMME — Design Deviations

The Claude Design prototype (`design/source/TRIMME.dc.html`) is the **visual** source of truth. Where it conflicts with the business rules of the specification, the visual language is kept and the workflow is corrected (spec operating rule 9, decision D-008).
Line numbers refer to the canonical `TRIMME.dc.html` (4656 lines). The analysis files in `design/analysis/` cite a working copy that is 2 lines longer (subtract 2 from their citations above line 5).

ID prefixes: **DV-S** spec conflict corrected · **DV-A** absent from design, spec requires (designed in TRIMME language) · **DV-T** tokens/visual/a11y fixes · **DV-C** copy/data inconsistencies not replicated · **DV-D** design features deferred.
Status: `planned` (to be applied in the listed phase) · `applied` (with commit) · `deferred`.

## DV-S — Spec conflicts corrected

| ID | Design (as drawn) | Lines (canonical) | Rule | Corrected implementation | Phase | Status |
|---|---|---|---|---|---|---|
| DV-S01 | **Barber transfer between shops**: admin transfer card (from/to selects, "تنفيذ النقل"), toast, nav label "الحلاقون والنقل والخدمات", permission row, audit example, sitemap node | 2841–2857, 4479, 3408, 4570, 4578, 3542 | §7 no transfer | **Removed entirely.** Nav: "الحلاقون وخدماتهم". Professional's shop chosen at creation and read-only afterwards; freed space holds the professional create/edit form. No workaround flow. | 6 | **applied** (Phase 06): nav "الحلاقون وخدماتهم"; professional create form chooses the shop once, detail shows it read-only; no transfer route, permission, UI or copy (tests + grep gate) |
| DV-S02 | **Global service catalogue** with platform-set price/duration, "assign services to shops", audit "price change affects 48 shops" | 2874–2909, 4490–4508, 4579, 4571, 3543, 3409 | §7, §10 | Admin: service **categories**, platform-wide view of shop-owned services, moderation (deactivate/hide with reason), audited support override, professional-service assignment. No global price/duration. | 7 | **applied** (Phase 07): `/admin/services` (platform-wide list, shop prices), `/admin/services/categories`, `/admin/packages`; hide/unhide with reason; audited support override; no global price (D-071, D-074) |
| DV-S03 | Shop services **toggle-only**, "prices/durations managed by the platform" badge, ✗ edit prices | 2554–2573, 4412, 3535, 3537 | §7, §10, §13 | Shop CRUD for own services (ar/en name + description, category, price, duration in 5-min steps, active, ordering, archive, "used in N bookings" guard). | 7 | **applied** (Phase 07): `/shop/services` (switch, keyboard move up/down, edit, archive, delete-if-unused), `/new`, `/[id]`; packages tab; badge shows admin moderation with its reason |
| DV-S04 | Professional-service assignment shows global prices | 4484–4488, 2859–2868 | §10 | Lists only the professional's own shop's services with that shop's price/duration; admin-only action. | 7 | **applied** (Phase 07): "الخدمات المسندة" card on the admin professional page lists only that shop's services with its prices (D-073) |
| DV-S05 | Hardcoded plan "سنوي — 2,400 ر.س", fixed renewal presets; Operations Manager has subscription rights | 4466, 2992–3001, 4533–4538, 4572 | §7, §15 | Plans/prices/durations from the API (SuperAdmin-managed, versioned); price snapshot on subscriptions; overrides SuperAdmin-only. | 8 | **applied** (Phase 08): plan picker and durations from `/admin/subscription-plans`, read-only price preview of the version in force on the start date, "Plan period (n months)" or a custom number of days; Operations Manager assigns/renews/suspends, SuperAdmin alone manages plans and overrides |
| DV-S06 | Reminder **3 hours** before (and a day-before reminder in notifications) | 1568, 1628, 3574, 4555, 4113 | §16 30 min | Setting `ReminderOffsetMinutes` (default 30) for customer and professional; copy reads the configured value. | 15 | **applied** (Phase 15): reminder jobs at start − `ReminderOffsetMinutes` for both audiences; the `time_remaining` placeholder renders the configured value (D-111) |
| DV-S07 | Unavailable slots rendered disabled with reason toasts | 3928–3937, 4000 | §11 only bookable slots | API returns only bookable slots; UI groups by period and shows an empty-period message. | 9/12 | API **applied** (Phase 09: slots carry a period; D-082); UI in Phase 12 |
| DV-S08 | Shop drawer offers all status actions for every booking; no "Confirm" action; filter chips omit Arrived/No-show | 4299–4305, 2371–2376, 2389, 4262 | §11 state machine | Render only API `allowedTransitions`; add Confirm; cancel with reason dialog; all status chips. | 13 | **applied** (Phase 10 API, Phase 13 UI): the drawer shows only `allowedTransitions` (Confirm included when pending), cancelling asks for a reason, every status has a chip with its count (cancellations together) |
| DV-S09 | Single "ملغي" status | 3636, 4299 | §11 | `CancelledByCustomer` / `CancelledByShop` (shared badge colour, distinct sub-labels). | 10, 13 | applied (shop board chips «ألغاه المحل» / «ألغاه العميل», Phase 13; status corrected in the Phase 17 visual review) |
| DV-S10 | "Cancellation request" after cutoff reviewed by shop (copy only) | 1779, 3501 | §11 | No online cancel after cutoff (setting); show policy + shop contact (D-015). | 12 | API **applied** (Phase 10); UI **applied** (Phase 12): after the cutoff the booking page hides cancel/reschedule, says the online window has closed and shows the shop's phone (web test `BookingPolicy`) |
| DV-S11 | Home "popular services" tiles show a price with no shop | 1040–1048, 3755 | §10 | Category shortcuts with "from X ر.س" aggregated from nearby shops. | 11 | **applied** (Phase 11): `/discover` tiles are platform categories with the lowest price among nearby listed shops and the shop count (`GET /public/categories/popular`, D-091) |
| DV-S12 | Packages modelled as a single service row | 3505, 3844, 1398 | §10 | Packages with items, explicit price/duration, shown in a packages section on the shop page (D-020). | 7/11 | **applied** (Phase 07 dashboards; Phase 11 shop page: a "الباقات" section under the services with the items, price, duration and a booking link) |
| DV-S13 | Success copy "تم تأكيد حجزك" regardless of status | 1584, 857, 1612 | §11 | Copy depends on resulting status (D-006). | 12 | **applied** (Phase 12): the booking page after creation says "تم تأكيد حجزك" for Confirmed and "تم إرسال طلب الحجز" for Pending (web test `CreatedBanner` for both; E1 for Confirmed) |
| DV-S14 | Hardcoded policy values (2 h free cancellation, 15 min lateness, 7-day review window, 14-day horizon, expiry reminders 30/15/7 days) | 1348–1349, 1568, 1779, 2587, 4404, 3555 | §14, §15 | Platform/shop settings drive values and copy. | 8/11/14 | **applied** (Phases 11 and 14): the shop page's cancellation policy comes from `CancellationCutoffMinutes`; the "late more than 15 minutes" row is not shown because no such rule exists; horizon, review window, expiring threshold and reminder offset are platform settings edited on `/admin/settings` (D-107); the 30/15/7-day expiry reminders are Phase 15 notifications and follow the threshold |
| DV-S15 | Reviews show full customer names | 3819–3821, 1406 (vs promise at 1909) | privacy | First name + surname initial (D-017). | 11 | **applied** (Phase 11): reviews store and show "خالد د." (D-092) |
| DV-S16 | Shop profile lock state hardcoded | 4387–4399 | §13 | `editableFields` from admin policy per shop, enforced server-side. | 6 | **applied** (Phase 06): admin "Shop edit permissions" card; shop settings disable locked fields with a shield note; API 403 `shop.profile_field_locked` (D-065) |
| DV-S17 | Admin customer phone reveal described but not controlled | 2954–2957 | §7, §14 | Masked phone + "إظهار الرقم" with reason → audited, permission-gated. | 14 | **applied** (Phase 14): `/admin/customers/[id]` shows `+966 5•• ••• •03`; «إظهار الرقم» needs `Admin.Customers.ViewContact` and a reason, is audited without the number and never cached (D-105) |
| DV-S18 | Shop search "by customer name or booking number" | 2141 | §7 | Backend search matches name/booking reference only, never phone. | 13 | **applied** (Phase 10 API, Phase 13 UI): the appointments search says "by customer name or booking number" and sends only that |
| DV-S19 | Calendar in fixed 30-minute rows | 4229–4240, 4191, 4345 | §11 5-min step | Minute-accurate positioning. | 13 | **applied** (Phase 13): the day and week views place bookings by the minute; 30-minute lines are visual guides only |
| DV-S20 | Payment-method row in profile settings ("طريقة الدفع") | 4139 | §2 no payment | Read-only info row "الدفع في المحل" (no payment settings screen). | 12 | **applied** (Phase 12): `/account` shows "طريقة الدفع — في المحل" as a read-only row |
| DV-S21 | Discovery hiding on pause/expiry stated as fixed behaviour | 2510, 2522, 2190, 4529, 4616, 3787 | §15 explicit settings | Settings-driven (D-013, D-014). | 8/9/11 | **applied** (Phase 11): discovery lists only shops `IShopBookability` makes visible; the page of a hidden shop stays reachable, says why booking is off and is not indexed |
| DV-S22 | Professional time off auto-notifies customers to reschedule | 4368 | §16 events | No automatic customer messages; affected bookings flagged to shop and admin. | 9 | **applied** (Phase 09): the design's "سيُنبَّه العملاء…" copy is not used; the time-off, break and closure dialogs list overlapping bookings and say nothing is cancelled or sent. Persisting the flag on bookings: Phase 10 |

## DV-A — Absent from design, spec requires (designed in the TRIMME visual language)

| ID | Missing piece | Spec | Phase |
|---|---|---|---|
| DV-A01 | Shop service create/edit form | §7, §10, §13 | 7 — **applied** (Phase 07): `ServiceForm` (ar required / en optional, category, price in SAR accepting Arabic-Indic digits, 5-minute duration select, online-bookable switch) |
| DV-A02 | **Shop location picker** (search/manual address, use current location, click/drag pin, resolved address + coordinates, confirm & save). The design only has district/address text fields (4389–4390, 4463–4464). Built from customer-side map/pin patterns (970–983, 1131–1143, 1269–1273). | §8, §13, §14 | 6 — **applied** (Phase 06): `LocationPicker` in the TRIMME card style — search pill + results, "استخدم موقعي الحالي", MapLibre map with a draggable navy pin and "اسحب الدبوس…" chip, resolved-address row (1271 style) with LTR coordinates, typed coordinate/address fallback, amber change warning, confirm; admin `/admin/shops/[id]?tab=location`, shop `/shop/settings/location` (read-only when locked) |
| DV-A03 | Professional working-hours editor + professional-scoped breaks | §11, §13 | 9 — **applied** (Phase 09): "دوام الحلاقين" card on `/shop/schedule` (professional select, "يتبع دوام المحل" switch, the same week grid as the shop's hours); breaks for everyone or one professional |
| DV-A04 | Break / time-off create & edit dialogs with conflict preview | §13 | 9 — **applied** (Phase 09): break (weekly weekday chips or one date), time-off (professional, type, dates, whole days or times, note) and closure dialogs; preview lists overlapping bookings and needs "حفظ رغم التعارض" |
| DV-A05 | Shop and admin notification inboxes | §13, §17 | 15 — **applied** (D-112): `/shop/notifications`, `/account/notifications` and the admin bell menu, live over `/hubs/notifications` |
| DV-A06 | Admin shop detail/edit (profile, location, users/invite, subscription, professionals, services, QR, activate/suspend) | §14 | 5/6 — Phase 05 slice **applied**: `/admin/shops` list (search, paging, table → cards), `/admin/shops/new`, `/admin/shops/[id]` with activate/suspend (confirm + reason) and owner/staff invitation; composed from the a-shops table and design-system cards. Profile, location and the rest in Phases 06–16; **Phase 06 slice applied**: tabs (URL `?tab=`) Profile & images (profile form, logo/cover/gallery, edit policy), Location (pin picker), Accounts (invite), Professionals (list + add); **Phase 08 slice applied**: Subscription tab |
| DV-A07 | Admin professional create/edit with E.164 WhatsApp number, masking, audited reveal, notification toggle, disable | §7, §8, §14 | 6 — **applied** (Phase 06): `/admin/professionals` (search, status filter, masked numbers, cards on phones), `/new` (shop chosen once), `/[id]` (profile, photo, disable/enable with reason, WhatsApp card with masked number, toggle, correction, audited reveal with reason) |
| DV-A08 | Admin customers list + profile | §14 | 14 — **applied**: `/admin/customers` (name search, booking figures, no contact data; table → cards) and `/admin/customers/[id]` (the a-appointments side card as a page: stat tiles, masked number with audited reveal, upcoming and previous bookings) |
| DV-A09 | Admin booking detail + intervention | §14 | 14 — **applied**: `/admin/bookings` (a-appointments table with status chips, dates, source, search) and `/admin/bookings/[id]` (snapshot, history with actors and reasons, read-only notes, intervention card: allowed transitions with a reason, reschedule dialog with date, professional and free times) (D-103) |
| DV-A10 | SuperAdmin plan editor + versioned price history | §7, §15 | 8 — **applied** (Phase 08): `/admin/subscription-plans` list with order buttons, `/new`, `/[id]` with availability actions, details editor and the append-only price timeline + add-version form |
| DV-A12 | Pause confirmation with an optional note (the design toggles instantly) | §13 | 9 — **applied** (Phase 09, D-083) |
| DV-A11 | Subscription assign/override/history/suspend | §7, §15 | 8 — **applied** (Phase 08): `/admin/subscriptions` (KPIs over every shop, status filter, search) and the shop page's subscription tab (activation/renewal, suspend/reinstate, SuperAdmin override, full history) |
| DV-A12 | WhatsApp template editor (ar/en, audience, event, placeholders, preview, validate, activate, versions, safe test send) | §16 | 15 — **applied** (D-109): `/admin/whatsapp/templates` and `/[templateId]` |
| DV-A13 | WhatsApp dispatch log with retry, audience, masked recipient, template version, error | §14, §16 | 15 — **applied** (D-110): `/admin/whatsapp/dispatches`, the dispatch detail and the booking page's WhatsApp section |
| DV-A14 | Admin QR generation/management + professional QR landing | §14, §17 | 16 — **applied** (D-114): `/admin/qr` composes the a-reviews QR analytics card (scans, conversion, per-shop bars) with the c-qr print materials (the three notes, PNG / SVG / PDF, «ملصق A5») and a codes table (period tabs, status filter, create dialog, switch off/on); `/[locale]/q/[code]` is the c-qr landing, and the undrawn barber variant reuses it with the barber's avatar and offers; `/shop/qr` gives the owner the same cards read-only. The A5 poster (c-qr 2090–2096) is a browser print page, not a server PDF, so Arabic is shaped correctly; the «PDF» download is the code itself as a 70 mm vector page. The design's decorative QR icon on the shop cover (1252, behaviour undefined) stays out |
| DV-A15 | Role CRUD + user-role assignment | §14 | 14 — **applied**: `/admin/roles` keeps the design's matrix (roles as columns, ✓ / —) read from the API, with a create form; `/admin/roles/[id]` edits one role's permissions by area; `/admin/roles/staff` assigns roles, disables and invites (D-106). The design's eyeOff "restricted" cell is expressed as separate permissions (for example Reviews.Flag vs Reviews.Moderate) |
| DV-A16 | Audit log page with filters | §14 | 14 — **applied**: `/admin/audit`, the design's timeline (colour by family) with filters for action, record, dates, actor and shop, entity links and "older entries" on a keyset cursor (D-104) |
| DV-A17 | Platform settings page | §14, §15 | 8/14 — **applied**: sectioned cards in the s-settings style with descriptions, ranges, a sticky save bar and the latest changes (D-107) |
| DV-A18 | Service categories & packages admin | §7, §10, §14 | 7 |
| DV-A19 | Real week calendar grid | §13 | 13 — **applied** (D-100): seven day columns with every booking (side by side when they overlap) plus the design's density strip |
| DV-A20 | Confirm dialogs and save bars on editable screens | general | 3+ — `ConfirmDialog` built (Phase 03); save bars arrive with the editable screens |
| DV-A21 | Distinct sign-in screen, profile completion (name), manual location selection sheet | §9, §12 | 4/11 — sign-in (same frame as sign-up, no terms), profile completion (name, language, terms) **applied** Phase 04; **location applied** Phase 11: `/onboarding/location` (the c-auth permission frame) and the `/discover` location sheet, with "allow" and a searchable list of districts that have listed shops (D-095) |
| DV-A22 | Security/session settings (session list, revoke all), staff forgot/reset password | §9, §12 | 4 — **applied**: `/account/security`, `/auth/staff/sign-in`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/accept-invite`, all composed from the c-auth card and design-system parts |
| DV-A23 | Shop gallery, shop mini-map, shop description, packages section on shop page | §12 | 11 — **applied**: a gallery strip under the address, the description, hours, policies, amenities and phone in "عن المحل" with a small MapLibre map (loaded when shown), and the packages section |
| DV-A24 | Booking-conflict state at confirm (typed 409) | §11 | 12 — **applied**: a 409 `booking.slot_unavailable` returns the wizard to the time step with "حُجز هذا الوقت للتو" and refreshed slots; reschedule does the same (web test, E6) |
| DV-A25 | Expired-session and permission-denied states per surface | §5 | 3/4 — `ExpiredSession`, `PermissionDenied`, `ErrorState` built (Phase 03); wired to 401/403 in Phase 04 — **applied** (session restore page, PermissionDenied on the wrong surface) |
| DV-A26 | English (LTR) versions of all screens; desktop/tablet layouts for customer screens other than landing | §5, §6 | 2+ |
| DV-A27 | Indexable public shop listing (`/shops`) | §6 | 11 — **applied**: `/shops` with city links, rating order, paging and breadcrumb data |
| DV-A28 | Legal pages (terms, privacy) linked from sign-up | §9 (terms acceptance) | 11 — **applied**: `/terms` and `/privacy` (placeholder text stating the real product rules, marked for legal review); the sign-up checkbox links to them in a new tab; the marketing footer links them |

## DV-T — Tokens, visual and accessibility fixes

| ID | Issue | Fix | Phase |
|---|---|---|---|
| DV-T01 | Link `#4A7FB5` = 4.20:1; tertiary greys used for text 2.1–2.8:1 (fail AA) | Darkened text tokens (D-021, values in D-039); enforced by `tokens.test.ts` | 2 — **applied** |
| DV-T02 | Tajawal 600 and Inter 800 used but not loaded | Inter 400–800 loaded. Tajawal has no 600, so 600 maps to 700 (D-022, D-047) | 2 — **applied** |
| DV-T03 | Mixed Arabic-Indic / Latin digits for the same fields | One rule: clock and date text in Arabic-Indic, quantities in Latin (D-040) | 2 — **applied** |
| DV-T04 | Nav active indicator uses a physical property; toast centring only works in RTL | Logical properties throughout: shells, dialogs (`start-1/2` + direction-aware translate) and a toast centred with `inset-x` + `mx-auto`. E2E checks the sidebar side in RTL and LTR | 2/3 — **applied** |
| DV-T05 | Internal inconsistencies: drawer breakpoint 768/992/1200; slot height 42 vs 44; 6 vs 5 booking steps | Breakpoints 390/768/**1200**/1440 (D-041). Every control ≥44px, slots included (`SlotGrid` `min-h-11`). The stepper takes any step list; the wizard uses 5 steps + confirmation in Phase 12 | 2/3 — **applied** |
| DV-T06 | No WhatsApp brand colour anywhere | Use design neutrals; WhatsApp previews use a neutral bubble style from the design | 15 — **applied** (`BubblePreview`) |

| DV-T07 | Switch off-track `#DDE4EC`, segmented inactive text, bar-chart `#B9CBDD` bars below contrast minimums | Adjusted tokens and relief (D-049) | 3 — **applied** |
| DV-T08 | Toast auto-dismiss after 2.6 s | 5 s, paused on hover/focus; never with an action (D-049) | 3 — **applied** |
| DV-T09 | Design shows unavailable slots with reasons (slot legend) | Only bookable slots rendered (D-009); no disabled/reason slot state in `SlotGrid` | 3 — **applied** |
| DV-T10 | Design dropdown includes "تغيير الحلاق" (change barber) | Not offered; sample menus use Confirm / Reschedule / Cancel only | 3 — **applied** |
| DV-T11 | s-hours closed days drawn at 55% opacity, so their grey text drops below AA (axe colour-contrast) | Closed days keep full opacity; the switch and "مغلق" in secondary text carry the state | 9 — **applied** |
| DV-T12 | Secondary text `#647484` on the grey note boxes (`bg-muted`, `bg-subtle`) is 4.35–4.38:1 | `text-secondary` → `#5F6F80` (D-039); `tokens.test.ts` checks the grey surfaces | 13 — **applied** |
| DV-T13 | Landing "how it works" step numerals `01–03` in `brand-150` on the tile are 1.18:1 (axe checks them even when hidden from assistive tech) | `brand-600` (4.10:1; the numerals are 32 px bold, so 3:1 applies) | 13 CI follow-up — **applied** |
| DV-T14 | Dates and times are drawn as Arabic text («٩:٠٠ ص», «١ أكتوبر») in the filters and schedule fields | The shop and admin date-range filters, the walk-in day and the schedule times are native `<input type="date|time">`. They are accessible and keyboard-operable everywhere, but the browser draws their value in its own UI language: an Arabic browser shows Arabic, an English one `10/01/2026`, `10:00 AM`. Every value the app itself prints follows DV-T03. Custom localized pickers are a later polish item | 17 | accepted (Phase 17 visual review) |
| DV-T15 | The design has a light theme only | A dark theme (D-124) derived from the same tokens: brand navy stays as `chrome` panels, ink inverts, and surfaces step up in lightness. The light theme is unchanged, except that the paused-booking chip now gets the `warning-50` background it was meant to have (`bg-warning-100` was not a token, so it rendered without a background) | 14 | accepted (user request) |

## DV-C — Copy/data inconsistencies not replicated

Weekday/date pairs are from 2025 (e.g. "الخميس ١٨ سبتمبر"; 18 Sep 2026 is a Friday); `addMin` AM/PM bug around noon (server computes end times); professionals shown under two shops in sample data (seed data must respect one shop per professional); headcount mismatches ("٦ حلاقين" vs 3); admin subtitle "١٢٨ محلاً نشطاً" vs KPI 114/128; shop footer "تبدأ من 30" vs cheapest 35; always-on verified shield; favorites header counts; period headers counts; notifications bottom-nav highlight; rating tags auto-selected; stars editable after submit. All demo data is generated from the seed, never copied from the prototype.

DV-C01 — OTP length: design shows 4 digits; D-005/D-037 uses 6 digits for security. The copy says "٦ أرقام" and the field has 6 positions (single LTR input, D-048). **Applied** Phase 04.

DV-C03 — Landing (c-landing): the nav's "للأعمال"/"عن تريمي" links have no pages, so the header links Home, Shops and Nearby; the hero photo placeholder is a brand panel with a decorative, name-free confirmation card; the stats row shows only real, non-zero figures; the partner button appears only when `TRIMME_PARTNER_CONTACT_URL` is configured (there is no partner sign-up flow, shops are admin-created). Phase 11.

DV-C04 — Professional profile: the design's "1,240 موعد مكتمل" and "98% التزام بالموعد" tiles are not shown: punctuality has no definition and invented figures are not allowed (spec §6). The rating, services, next free times and latest reviews are shown. Phase 11.

DV-C05 — Shop page header: the QR button on the cover is not shown yet (QR arrives in Phase 16). The heart is next to the shop name (Phase 12) rather than on the cover image, so it stays visible without a cover. The back button is replaced by the breadcrumb on this page. Phase 11/12.

DV-C06 — Booking wizard (Phase 12):
- The design draws no auth step. Guests confirm through the existing sign-in pages and return to the review step (D-096), instead of inline phone and code sub-steps.
- The date step is the horizontal 14-day strip from the component library, not the prototype's 4-column 12-day grid. Days without free times are disabled, with no reason text (D-009).
- The professional step lists only the professionals assigned to the chosen service or package, with "أي حلاق متاح" preselected (design rule 2). The prototype defaults to a named barber and shows unassigned ones.
- The confirmation is the booking's own page with a status-dependent banner, not a separate success overlay. "عرض موعدي" is therefore the page itself, and "حجز موعد آخر" is the "احجز موعدك القادم" card in My appointments.

DV-C07 — Account and appointments (Phase 12):
- Profile: the stat tiles (completed visits, favourite shops) are not shown. The WhatsApp notification row reads "تُدار قريباً من هنا" until the notification centre (Phase 15). Support shows the terms link; FAQ and contact have no pages yet.
- Review page: the professional is shown by initials; the booking carries no photo.
- Upcoming cards show "التفاصيل" and "إعادة جدولة" as in the design; past cards show "قيّم الزيارة", "تم التقييم ★ n" or "إعادة الحجز", depending on the API's `allowedActions`.

DV-C02 — The OTP help text offers "أو اطلب الرمز عبر رسالة نصية" (request by SMS). v1 has no SMS channel (WhatsApp only, D-005), so the copy asks the user to check WhatsApp and request a new code after the countdown. Phase 04.

DV-C08 — Shop dashboard (Phase 13):
- **Drawer.** The appointment drawer opens from the inline end (left in Arabic) as the app's standard sheet, 420 px or full width on phones. The design draws it from the inline start. It keeps the design's content plus internal notes.
- **Not built.** "إعادة إرسال التأكيد" (resend confirmation) arrives with WhatsApp messaging (Phase 15); "طلب تواصل" stays deferred (D-023, DV-D01).
- **Header CTA.** The top-bar "+ حجز حضوري" button is the walk-in item in the navigation. The header shows the live-updates state instead.
- **Subscription card.** The overview's subscription card became dashboard-wide banners (paused, ending soon, ended, suspended) on every shop page.
- **Overview.** It adds the spec's free-capacity KPI and the day's cancellations. The professional load is minutes-based (booked ÷ available), not "12 / 14" slots (D-100).
- **Calendar.** Free half-hours in the day view link to a walk-in prefilled with that professional and time; the walk-in page offers the exact free times. The week view keeps the design's density strip and adds real columns (D-100).
- **Walk-in time.** "وقت مخصص" (custom time) became a day picker plus that day's free starts on the slot grid, from the same rules as the walk-in command. "ابدأ الآن" is offered only when the professional is free for the whole duration.

DV-C09 — Admin operations (Phase 14):
- **Overview.** The KPI row keeps the design's eight tiles. "Subscriptions expiring soon" counts the configured threshold rather than "within 30 days", and its delta names ended and suspended subscriptions. The period switch (today, 7, 30 days) is an addition. The trend adds a third series ("upcoming or in progress") so each day's total is honest. "Most booked services" became categories (DV-S02). "Top shops" shows name, bookings, cancellation rate and rating, without the district (not on the shop directory contract). The header subtitle shows the platform date instead of "١٢٨ محلاً نشطاً" (DV-C, D19).
- **Bookings.** The design's "today / this week" chips became a date range plus the status chips of the shop board. Rows open a detail page rather than a side card. A shop's page links to its bookings (`?shop=`), and a customer's profile to theirs (`?customer=`).
- **Reviews.** The queue is a tab ("بانتظار المراجعة") among published, hidden and all. «تواصل مع المحل» (contact the shop) sends the shop an in-app message since Phase 15 (D-112).
- **Roles.** The matrix is read-only on the list page and editable per role on its own page, where boxes for permissions you do not hold are disabled with a note.

DV-C10 — WhatsApp and notifications (Phase 15):
- **Message log card.** The design's a-reviews card ("سجل رسائل واتساب", last 24 h, delivery rate) became the full log page with status chips, audience and event filters, and the same 24-hour count and delivery rate. Rows say "reminder" where the design said "تذكير قبل ٣ ساعات" (DV-S06).
- **Template editor.** It is absent from the design; it is built in the two-pane layout proposed in analysis 03 A12: placeholder chips, a neutral bubble preview (DV-T06), and a version history with "restore to draft". The design's "Directions" and "Manage booking" buttons became template buttons whose links the platform builds.
- **Shop drawer.** "إعادة إرسال التأكيد" (resend the confirmation, design 2390) is not offered to shops: a shop could message a customer repeatedly. Admins retry failed messages from the log instead.
- **Default wording.** The customer confirmation follows the design's WhatsApp mock (1600–1628): shop, service with professional, date and time with duration, amount paid at the shop, address, reference.
- **Notification pages.** The design has no shop or admin inbox (DV-A05). The shop page and the customer page use the design's list-card style; the admin bell is a menu of the latest alerts.

## DV-D — Design features deferred

| ID | Feature | Decision |
|---|---|---|
| DV-D01 | "طلب تواصل" contact mediation | D-023 deferred |
| DV-D02 | Shop daily WhatsApp summary | D-024 deferred |
| DV-D03 | Drag-and-drop reordering of shop services and packages | Deferred (D-071): keyboard move up/down buttons with an `aria-live` announcement cover reordering; drag can be added on the same `PUT …/order` endpoint |

DV-C11 — Phase 17 visual review (all 25 product screens against `design/reference`, 390 and 1440):
- **Fixed.**
  - The landing search bar collapsed on a 390 px phone (the text field was 0 px wide), because `sm` is 390 px here (D-041). It now switches to a row at `md`.
  - The booking wizard's back buttons pointed forward.
  - The step bar was empty on step 1; step *n* of *m* now fills *n/m*.
  - The map credits sat under the selected-shop card and the picker hint; they are at the top of the map now.
  - Review counts use Arabic and English plural forms («٤ تقييمات», «268 تقييماً», «1 review»).
  - Admin search buttons used two styles; there is now one outlined style.
  - A suspended shop was red on the shops list but grey for subscriptions; it is grey (neutral) in both, as drawn.
- **Changed: pagination on a phone** (design 758–774). Below `md`, only previous, the current page and next are shown, and the summary line gives the range and total. The design's full row of seven numbered cells plus two arrows needs about 440 px, wider than a 390 px phone; the route audit found the overflow on a six-page list (D-122). Tablet and desktop keep the design's row.
- **Accepted, as built since earlier phases.**
  - **Shop and admin header.** The global header search is not built; search lives on the lists that have it (appointments, customers, shops, bookings). The sidebar keeps short labels («التقويم», «المواعيد»). The sidebar footer shows the shop or role and a sign-out button instead of the design's account card.
  - **Customer screens.**
    - Ratings show five stars with the count, rather than one star.
    - The search map sits inside the page with a list/map switch, rather than full-bleed.
    - The shop page stacks cover, identity and tabs, without the overlapping sheet.
    - The bookings list uses underline tabs.
    - The customer's own number is masked on the profile (privacy by default, the same as everywhere else).
  - **Landing.** It uses a real photograph and its own copy (DV-C03 otherwise holds). The public header has section anchors on the landing page and Shops and Nearby elsewhere.
  - **Shop schedule.** Days take several opening periods (the Phase 09 schedule editor, D-082), so the rows are taller than the design's one-line rows. Time off is drawn in amber on the calendar.
  - **Admin.**
    - The shops list shows name, link, status and creation date, with search, rather than the design's district, barber count and subscription columns; those live on each shop's page.
    - The subscriptions KPI row shows counts per status without the revenue tile (no payments in v1).
    - KPI icon tiles share one neutral tint.
    - The roles matrix orders the role columns by permission scope and has no shop column; shop roles are fixed (D-051).
- Test data left behind by E2E runs (`e2e-…` shops, «اختبار حمل» bookings) appears on the shared development stack only. A fresh `down -v` stack shows the seeded demo data alone.
