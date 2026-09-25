# 03 — Shop dashboard & Platform Admin screens (design analysis)

> **Line-number note:** citations in this file refer to a 4658-line working copy of `TRIMME.dc.html`. The committed canonical copy (`design/source/TRIMME.dc.html`, 4656 lines, identical to the Claude Design project) is **2 lines shorter**: subtract 2 from any cited line number greater than 5.

Source: `design/source/TRIMME.dc.html` (Claude Design "dc" component). Line numbers refer to that file.
Spec: `TRIMME_Claude_Code_Full_Stack_Implementation_Prompt.md` (§7, 8, 10, 11, 13–17).

Conventions used in this document:

- **As designed** = what the prototype literally shows (verbatim Arabic + English translation, sample values).
- **Proposed** = our recommendation for the Next.js route / ASP.NET Core endpoint / DTO / permission. None of the routes, endpoints or permission names exist in the design; they are ours.
- All endpoints are under `/api/v1`. Shop endpoints **never** take a `shopId` from the client — the tenant is resolved from claims (spec §7 "Technical enforcement"). Admin endpoints take explicit ids.
- Prompt-injection check: no text in the analysed ranges reads as instructions to an AI agent. The "developer handoff" list (4644–4651) and responsive/a11y rules are ordinary design notes and are treated as design data.

---

## 0. Shared building blocks

### 0.1 Status vocabulary (script 3633–3648) — single source for badge colours

Badge = pill `bStyle(bg, fg)` + 6px dot `bDot(c)` + explicit Arabic text (a11y rule 4637: status is never colour-only).

| Arabic (as designed) | English | bg / fg / dot | Proposed enum |
|---|---|---|---|
| بانتظار التأكيد | Pending confirmation | #FDF3E2 / #8A5A12 / #D89A2E | `BookingStatus.Pending` |
| مؤكد | Confirmed | #E8F1FB / #2C5C8C / #4A7FB5 | `Confirmed` |
| حضر العميل | Customer arrived | #EDF3FA / #3D5266 / #6D9BCB | `Arrived` |
| مكتمل | Completed | #E6F4EC / #1F6B48 / #2E9E6B | `Completed` |
| ملغي | Cancelled | #FBEAEA / #9B2C2C / #C44545 | split into `CancelledByCustomer` / `CancelledByShop` (+ `CancelledByAdmin` if intervention is added) — design has one label only |
| لم يحضر | No-show | #F1F4F7 / #5E6E7E / #98A7B5 | `NoShow` |
| نشط | Active (subscription / professional) | #E6F4EC / #1F6B48 / #2E9E6B | `SubscriptionStatus.Active` |
| قرب الانتهاء | Expiring soon | #FDF3E2 / #8A5A12 / #D89A2E | `ExpiringSoon` |
| منتهي | Expired | #FBEAEA / #9B2C2C / #C44545 | `Expired` |
| موقوف | Suspended | #F1F4F7 / #5E6E7E / #98A7B5 | `Suspended` |

Professional state badges (4472): نشط Active (green), إجازة On leave (amber #FDF3E2/#8A5A12/#D89A2E), معطّل Disabled (grey #F1F4F7/#5E6E7E/#98A7B5).

Booking source labels (as designed): التطبيق (App), رمز QR (QR code), حضوري (Walk-in), تطبيق العميل (Customer app — drawer wording). Proposed enum `BookingSource { App, Qr, WalkIn }` (+ `Admin` if admin creates bookings).

### 0.2 Reusable components (to build once)

| Component | Where used | Anatomy |
|---|---|---|
| `DashboardShell` | shop 2118–2148, admin 2661–2688 | 250px navy (#10283D) sidebar, logo + caption (`BUSINESS` / `PLATFORM ADMIN`), nav buttons (`sideStyle` 4160–4165: active = white 12% bg, bold, white text; inactive #8FB0D0), footer identity card; white top bar with H2 title + subtitle, search box, bell, primary CTA; scroll body padding 22px 24px 32px. Design frame is fixed 1320px wide, min-height 820px. |
| `KpiCard` | shop overview 2152–2160, admin overview 2692–2700, subs 2966–2974 | icon chip 28px (`kpiIcon(tone)` 4169–4174: info/ok/warn/bad), label, value (Inter 700 26–28px), delta line (`delta/dStyle` up=green, down=red, flat=grey). |
| `Card` | everywhere | white, 1px #E6ECF3 border, radius 16px, padding 20–24px, soft shadow. |
| `DataTable` (CSS grid rows) | appointments, shops, pros, services, subs | header row #F7F9FC bold 11.5–12px; rows 13px padding, bottom border #F1F5F9; trailing `more` (⋯) or `edit` icon column. Under 768px → cards (responsive rule 4594). |
| `FilterChips` | 2309–2311, 2770–2774, 2916–2919 | 36–38px pills; active = navy filled; label includes count ("الكل · ٣٤"). |
| `StatusBadge` | see 0.1 | |
| `Toggle` (`sw` / `knob`) | hours, services, notif prefs, pause | `sw()` (3689): 38×22, on = green #2E9E6B, off = #DDE4EC; pause switch is 46×27 with amber #D89A2E when paused. |
| `PickCard` (`pickCard` 4315–4319) | walk-in service/pro | selected = 1.5px #6D9BCB border + 3px #EDF3FA ring. |
| `CheckboxRow` (`cbox` 4482–4485) | assign services / assign shops | 22px box, navy filled when checked with check icon. |
| `SideDrawer` | shop appointments 2344–2393 | scrim rgba(16,40,61,.35); 420px (max 92%) panel on inline-start (right in RTL), slide transform .3s; header/body/footer. |
| `InfoNote` (grey privacy note with `eyeOff`/`shield`) | 2366–2372, 2439–2442, 2956–2959 | #F1F4F7 bg, icon + text. |
| `WarnNote` (amber) | 2190–2194, 2853–2856 | #FDF3E2 bg, #8A5A12 text. |
| `ProgressBar` / `LoadBar` | pro load 2198–2206, top services 2726–2731, QR 3045–3050, subscription 2587 | 7–8px track #F1F5F9, fill navy (>60–80%) or #6D9BCB / #9FC0DE. |
| `Timeline` (dot + line) | appointment log 2381–2386, audit log 3092–3103 | |
| `Toast` | 3302–3307, `toast()` 3453 | fixed bottom-centre navy pill with info icon, auto-dismiss 2600 ms. |
| `DashedAddButton` | 2500, 2545 | 1.5px dashed #CBD8E5, "+ label". |
| `LockedField` | settings 2621–2626 / 4387–4401 | grey #F7F9FC bg, muted text, trailing `shield` icon = read-only (admin-controlled). |

### 0.3 Responsive behaviour (as designed: 3114–3178, rules 4592–4597)

- ≥1200px: full dashboard with fixed sidebar. 768–1199px: sidebar becomes a sliding drawer; barber column merges under customer name in tables. <768px: bottom bar + cards; tables never scroll horizontally; each row → card with title + status + one primary action.
- Calendar on touch: one professional at a time with horizontal swipe between professionals; slot height ≥ 44px (4595).
- Desktop appointment list: full table + details drawer that slides in without leaving the page (3164).
- The calendar grids themselves use `min-width:640px` + `overflow-x:auto` (2250–2251, 2276–2277) — acceptable on desktop only; the mobile rule above overrides.
- **Per-screen mobile behaviour:** only `s-appointments` has a designed tablet and mobile variant (3114–3178). Every other shop and admin screen in this document is drawn desktop-only, inside the fixed 1320px frame. For those screens, apply the rules at 4593–4596:
  - below 768px, two-column grids stack;
  - the right-column cards (walk-in summary, forms, side panels, subscription card, audit timeline) move below the main card, and the walk-in "تسجيل الحجز" CTA stays sticky at the bottom;
  - tables become cards;
  - KPI grids go to 2 columns, then 1;
  - between 768 and 1199px, the sidebar becomes a drawer;
  - below 768px, the shell uses a bottom bar;
  - drawers become full-screen sheets.
- States library (4612–4619, 3180–3235) relevant to dashboards: "صلاحية مرفوضة / رقم العميل غير متاح لحسابك" (permission denied: customer phone unavailable), "الاشتراك منتهي / محلك مخفي من الاكتشاف" (subscription expired, shop hidden), skeleton loader (max 3 s then error), field validation (message directly under field).

---

## 1. Shop dashboard

### 1.0 Shop shell (2118–2148)

- Sidebar caption `BUSINESS`. Nav = `GROUPS[3]` (3399–3405), label = screen title:
  1. النظرة التشغيلية — Operational overview (icon chart)
  2. تقويم اليوم والأسبوع — Day & week calendar (calendar)
  3. قائمة المواعيد والتفاصيل — Appointment list & details (list)
  4. حجز عميل حضوري — Walk-in booking (plus)
  5. الدوام والاستراحات والإجازات — Hours, breaks & time off (clock)
  6. الخدمات والاشتراك — Services & subscription (tag)
  7. إعدادات المحل والإشعارات — Shop settings & notifications (settings)
- Sidebar footer card (2128–2134): shop initial avatar "أ", shop name "صالون الأصالة", meta "الملقا · ٦ حلاقين" (district · professional count), subscription line with green dot "الاشتراك نشط حتى ٣٠ نوفمبر" (subscription active until 30 Nov).
- Top bar (2138–2146): H2 `{{title}}`; subtitle current date/time "الخميس ١٨ سبتمبر ٢٠٢٦ · ٥:١٢ م"; search box placeholder "ابحث باسم العميل أو رقم الحجز" (search by customer name or booking number) — **must never search by phone**; bell with red unread dot; primary CTA "+ حجز حضوري" (walk-in booking → `/shop/walk-in`).
- Proposed shell endpoint: `GET /api/v1/shop/me` → `ShopContextDto { shopId, name, initial, district, professionalCount, subscription: { status, endsAt, daysRemaining }, onlineBookingPaused, unreadNotifications, currentUser { name, shopRole } }`.
- Proposed global search: `GET /api/v1/shop/bookings/search?q=` (matches customer display name or booking reference `TR-xxxxx` only).
- Proposed live updates: SignalR hub group per shop (server-assigned from claims), events `booking.created|updated|statusChanged` with the same phone-free DTO.

### 1.1 `s-overview` — النظرة التشغيلية (Operational overview) — 2150–2227; script 4176–4203

**Layout (desktop):** KPI row (auto-fit, min 190px) → two-column grid 1.5fr/1fr (left: today's upcoming schedule; right: subscription warning + professional load) → full-width hourly distribution bar chart.

**Sections & fields**

1. KPI cards `shopKpis` (4176–4181):

| Label | Sample | Delta text | Tone | Proposed computation (shop tenant, today in shop TZ) |
|---|---|---|---|---|
| مواعيد اليوم — Today's appointments | 34 | "+12% عن أمس" (+12% vs yesterday) | info / up | count bookings with start date = today, status ≠ cancelled; delta vs yesterday same definition |
| بانتظار التأكيد — Awaiting confirmation | 4 | "تحتاج إجراءً الآن" (needs action now) | warn | count status = Pending (today + future? design implies today) |
| مكتملة اليوم — Completed today | 19 | "من أصل ٣٤" (out of 34) | ok | count status = Completed today / today total |
| لم يحضروا — No-shows | 2 | "5.9% من اليوم" (5.9% of today) | bad / down | count NoShow today; % of today total |

2. "الجدول القادم اليوم" (Today's upcoming schedule) + link "عرض التقويم" (View calendar → `/shop/calendar`). Rows `todayList` (4189–4198): `time` "٥:٣٠ م", `dur` "٥٠ د", coloured status bar, `customer` "عبدالله الشمري", status badge, `svc` "باقة شعر ولحية" · `pro` "فيصل" (first name), `price` "85". The current/next row (`now:1`) is highlighted (#F8FBFE bg, #D6E5F5 border).
   Sample rows: 5:30 PM Abdullah Al-Shamri / Hair & beard package / Faisal / Confirmed / 85; 6:00 Nasser / Haircut / Sultan / Confirmed / 60; 6:30 Khalid / Beard trim / Faisal / Pending / 35; 7:15 Waleed / Kids haircut / Rakan / Confirmed / 45; 8:00 Majed / Facial care / Sultan / Pending / 70.
3. Subscription warning card (amber, 2190–2194): title "الاشتراك ينتهي بعد ٧٣ يوماً" (Subscription ends in 73 days); body "عند الانتهاء يختفي المحل من الاكتشاف وتبقى المواعيد القائمة سارية." (On expiry the shop disappears from discovery; existing appointments remain valid); button-like label "تواصل مع الإدارة للتجديد" (Contact admin to renew) — no action wired. Show only when status ∈ {ExpiringSoon, Expired, Suspended} (design shows it at 73 days, which is above the 30-day threshold used elsewhere — treat threshold as configurable).
4. "حِمل الحلاقين اليوم" (Professionals' load today) `proLoad` (4200–4203): `name` full name, `v` "12 / 14" (booked slots / capacity) or "إجازة" (on leave), bar fill % (navy >80%, blue otherwise, grey 3% for leave). Samples: فيصل القحطاني 12/14 (86%), سلطان الحربي 9/14, راكان المطيري 7/12, ماجد العتيبي إجازة. Proposed: `booked = count active bookings today`, `capacity = floor(working minutes today ÷ standard slot)` or minutes-based %; define in backend.
5. "التوزيع حسب الساعة — آخر ٧ أيام" (Distribution by hour — last 7 days), caption "الذروة ٦:٠٠ — ٨:٠٠ م" (peak 6–8 PM); 12 vertical bars `bars` (script 3685–3687) with hour labels 9,10,11,12,1…8 and relative heights 22,34,41,28,18,24,46,62,88,96,74,52 (% of max; >80% navy, else #B9CBDD). Treat as `[{hour, count}]` and normalise on the client.

**Interactions:** none wired except nav. "عرض التقويم" link, renew CTA (inert).

**Proposed**
- Route: `/[locale]/shop` (alias `/[locale]/shop/overview`).
- `GET /api/v1/shop/dashboard/overview?date=` → `ShopOverviewDto { kpis: { todayTotal, todayTotalPrevDay, pending, completed, noShow }, upcoming: BookingListItemDto[], professionalLoad: [{ professionalId, name, booked, capacity, onLeave }], subscription: ShopSubscriptionSummaryDto }`
- `GET /api/v1/shop/dashboard/hourly-distribution?days=7` → `[{ hour, count }]`, `peakFrom`, `peakTo`.
- `BookingListItemDto` (shop): `id, reference, startsAt, endsAt, durationMinutes, customerDisplayName, serviceName (snapshot), professionalId, professionalName, status, source, priceSnapshot, currency` — **no phone, no customer id beyond an opaque one if needed.**
- Permission: shop role `Shop.Bookings.Read` (any shop user).

### 1.2 `s-calendar` — تقويم اليوم والأسبوع (Day & week calendar) — 2229–2303; script 4205–4261

**Layout:** single card. Toolbar: segmented control Day/Week (`calViews` 4206–4214: "يوم", "أسبوع"; state `sCalView`, default `day`), date navigator (chevR / "الخميس ١٨ سبتمبر" / chevL — inert), legend on inline-end: مؤكد Confirmed (#4A7FB5), بانتظار Pending (#D89A2E), استراحة Break (hatched).

**Day view** (`calDay`, 2249–2274): grid `76px + 3 columns` (one per professional, min-width 640px):
- Column headers `calCols` (4217): professional name + meta count ("فيصل القحطاني · ١٢ موعداً", "سلطان الحربي · ٩ مواعيد", "راكان المطيري · ٧ مواعيد").
- Rows `calRows` (4231–4242): 30-minute buckets 3:00 PM → 7:30 PM; each cell kind (`cellStyles` 4218–4225):
  - `conf` Confirmed — #E8F1FB, 3px inline-start #4A7FB5; title = customer short name ("عبدالعزيز ا."), sub = service ("حلاقة شعر") or "يكمل" (continues — multi-slot booking).
  - `pend` Pending — #FDF3E2, 3px #D89A2E.
  - `break` Break — hatched, title "استراحة العصر" (Asr break) across all columns at 3:30.
  - `off` Off-shift — #F4F6F9, title "انتهى دوامه" (shift ended) for Rakan from 5:30.
  - `empty` — dashed outline (free).
- Customer names abbreviated with last-name initial ("سعود ر.") in the grid; full names elsewhere.

**Week view** (`calWeek`, 2275–2301): it is **a day-part density heatmap, not a week of appointments**. Columns = 7 days `weekDays` (أحد 14 … سبت 20; today 18 highlighted #EDF3FA). Rows = day-parts صباحاً (morning), ظهراً (noon), مساءً (evening), ليلاً (night). Cell value = booking count; colour `heat` (4247–4255): null → hatched "—" (closed), >8 navy (full), >4 #9FC0DE (medium), else #EDF3FA (low). Legend "كثافة الحجوزات:" منخفضة/متوسطة/ممتلئة/مغلق (low/medium/full/closed). Sample Friday morning null (closed), Saturday all null (closed day).

**Interactions:** Day/Week toggle (state). Date prev/next (inert). Cells not clickable in the prototype — proposed: clicking a booking opens the same appointment drawer as 1.3; clicking an empty cell opens walk-in prefilled (only if the availability API confirms the slot).

**Spec gaps to handle in the build:**
- Row granularity is 30 min but spec §11 requires a configurable slot step (5-min capable). The 7:15 booking in the list renders in the 7:30 row. Build the grid with absolute positioning by minutes (booking block height = duration), keep 30-min gridlines as visual guides only.
- Only 3 professionals shown; must scale to N columns (horizontal scroll on desktop, one-pro swipe on mobile per 4595).
- Spec §13 "day/week calendar": we should offer a real week view (per professional or aggregated) in addition to the heatmap, or explicitly accept the heatmap as the week view — record as decision.

**Proposed**
- Route: `/[locale]/shop/calendar?view=day|week&date=YYYY-MM-DD&professionalId=`.
- `GET /api/v1/shop/calendar/day?date=` → `{ date, professionals: [{ id, name, bookingCount, workingIntervals[], breaks[], timeOff[] , bookings: CalendarBookingDto[] }] }`; `CalendarBookingDto { id, startsAt, endsAt, customerShortName, serviceName, status }`.
- `GET /api/v1/shop/calendar/week-density?weekStart=` → `{ days: [{ date, closed, parts: { morning, noon, evening, night } }] }` (null = closed).
- Permission: `Shop.Bookings.Read`, `Shop.Schedule.Read`.

### 1.3 `s-appointments` — قائمة المواعيد والتفاصيل (Appointment list & details) — 2305–2395; script 4263–4313

**Layout:** table card + overlay drawer (position absolute inside content area).

**Toolbar:** status filter chips `statusFilters` (4264–4272), state `sTab` default `all`:
- الكل · ٣٤ (All 34), بانتظار · ٤ (Pending 4), مؤكد · ٢١ (Confirmed 21), مكتمل · ٧ (Completed 7), ملغي · ٢ (Cancelled 2).
- **Missing chips:** حضر العميل (Arrived) and لم يحضر (No-show) although rows with those statuses exist (4278, 4280). Add them.
- Date-range pill (inline-end) "١٨ — ٢٥ سبتمبر" with calendar icon (inert) → date range picker.

**Columns** (grid `1.1fr 1.5fr 1.2fr 1fr 1fr 44px`): الوقت (time + date "الخميس ١٨"), العميل والخدمة (customer full name + "service · duration"), الحلاق (professional full name), الحالة (badge), المصدر (source), ⋯ (opens drawer).
Sample rows (4273–4280): 5:30 PM Abdullah Al-Shamri, Hair & beard package, 50 min, Faisal Al-Qahtani, Confirmed, App; 6:00 Nasser Al-Zahrani, Haircut 30, Sultan Al-Harbi, Confirmed, QR; 6:30 Khalid Al-Dosari, Beard trim 20, Faisal, Pending, App; 7:15 Waleed Al-Omari, Kids haircut 25, Rakan Al-Mutairi, Confirmed, Walk-in; 2:00 Mishal Al-Otaibi, Haircut, Sultan, Arrived, App; 12:30 Turki Al-Shehri, Facial care 40, Faisal, Completed, App; 11:00 AM Badr Al-Saleh, Haircut, Rakan, No-show, App.

**Pagination footer:** "عرض 1–7 من 34 موعداً" (showing 1–7 of 34), prev/next + page numbers (1 active, 2).

**Drawer** `sDrawerOpen` (2344–2393), open via ⋯ (`openDrawer` 4285), close via ✕ (`closeDrawer`); scrim does not close in prototype (proposed: scrim click + Esc close, focus trap).
- Header: "تفاصيل الموعد" (Appointment details), reference "TR-48219".
- Time block: "الموعد" / "٥:٣٠ م — ٦:٢٠ م" / "الخميس ١٨ سبتمبر · ٥٠ دقيقة".
- Key/value rows `drawerRows` (4294–4300): العميل (customer) عبدالله الشمري; الخدمة (service) باقة شعر ولحية; الحلاق (professional) فيصل القحطاني; المصدر (source) تطبيق العميل; المبلغ (amount) "85 ر.س — يُدفع في المحل" (85 SAR — paid at the shop). **This is informational price, not payment UI — keep it.**
- Privacy note (2366–2372): "رقم العميل غير متاح" (Customer number not available) — "تتم مراسلة العميل تلقائياً عبر واتساب المنصة. للتواصل الضروري استخدم زر «طلب تواصل» ويتولى فريق تريمي الوساطة." (Customer is messaged automatically via platform WhatsApp; for necessary contact use the «Request contact» button and the TRIMME team mediates.) — the «طلب تواصل» button is **not drawn** anywhere in the drawer (see Deviations).
- "تغيير الحالة" (Change status): buttons `statusActions` (4301–4307) = حضر العميل (Arrived), مكتمل (Completed), لم يحضر (No-show), ملغي (Cancelled); styled with their status colours; click → toast "تم تحديث حالة الموعد إلى «…» وأُرسل إشعار للعميل" (status updated to «…» and customer notified). **No "تأكيد/Confirm" action; all four shown regardless of current status.**
- "سجل الموعد" (Appointment history) `apptLog` (4308–4312): "حجز العميل الموعد" (customer booked) — Tue 16 Sep 9:41 PM · via app; "أكد المحل الموعد" (shop confirmed) — 9:46 PM · by سارة (استقبال) (Sarah, reception); "أُرسلت رسالة التأكيد" (confirmation message sent) — WhatsApp — delivered.
- Footer: primary "إعادة إرسال التأكيد" (Resend confirmation) → toast "أُعيد إرسال رسالة التأكيد عبر واتساب"; outlined red "إلغاء" (Cancel) — duplicates the ملغي status action.

**Proposed**
- Routes: `/[locale]/shop/appointments?status=&from=&to=&page=` and drawer as parallel/intercepting route `/[locale]/shop/appointments/[bookingId]` (deep-linkable; renders as drawer on desktop, full page on mobile).
- `GET /api/v1/shop/bookings?status=&from=&to=&professionalId=&page=&pageSize=` → `Paged<BookingListItemDto>` + `counts: { all, pending, confirmed, arrived, completed, cancelled, noShow }`.
- `GET /api/v1/shop/bookings/{id}` → `ShopBookingDetailDto { id, reference, startsAt, endsAt, durationMinutes, customerDisplayName, service { nameSnapshot, priceSnapshot, currency, durationSnapshot }, professional { id, name }, source, status, allowedTransitions[], history: [{ type, at, actorDisplay, channel, deliveryStatus }], notes[] }` — no phone, no email.
- `POST /api/v1/shop/bookings/{id}/transitions` body `{ targetStatus, reason?, rowVersion }` → 200 / 409 (invalid transition or concurrency) / typed problem details. Server derives `CancelledByShop`.
- `POST /api/v1/shop/bookings/{id}/notifications/resend-confirmation` (idempotency key, rate-limited; creates outbox message to customer with the active template; no phone in response).
- `POST /api/v1/shop/bookings/{id}/contact-requests` only if the mediation feature is approved (see Deviations).
- Permissions: `Shop.Bookings.Read`, `Shop.Bookings.UpdateStatus`, `Shop.Bookings.ResendNotification`.

### 1.4 `s-walkin` — حجز عميل حضوري (Walk-in booking) — 2397–2459; script 4315–4330

**Layout:** 1.6fr form card + 1fr sticky summary card.

Header "تسجيل عميل حضوري" (Register a walk-in customer); sub "يحجز الوقت فوراً ويختفي من الأوقات المتاحة أمام العملاء في التطبيق." (Reserves the time immediately and removes it from the app's available times.)

Steps (all rendered at once; `walkStep` state exists at 3438 but is unused):
1. "١. الخدمة" (Service) — pick-cards from `SRV` (3843–3848): `name`, `meta` "60 ر.س · ٣٠ دقيقة". First selected. Samples: حلاقة شعر 60/30, تهذيب لحية 35/20, باقة شعر ولحية 85/50, حلاقة أطفال 45/25, عناية بالوجه 70/40.
2. "٢. الحلاق" (Professional) — `walkPros` (4321–4329): avatar initial, name, live availability meta: "مشغول حتى ٦:٢٠ م" (busy until 6:20 PM), "متاح الآن" (available now — selected), "متاح ٧:٠٠ م" (available 7:00 PM). Only professionals assigned to the chosen service should appear.
3. "٣. الوقت" (Time) — chips: primary "▶ ابدأ الآن ٥:١٢ م" (Start now 5:12 PM), "٥:٣٠ م", "٦:٠٠ م", dashed "⏱ وقت مخصص" (custom time). Note "now" 5:12 is not on a 30-min grid → supports free-minute starts subject to collision check.
4. "٤. بيانات العميل" (Customer details) — الاسم (Name) "سعود الرشيد"; ملاحظة (اختياري) (Note, optional) placeholder "تدريج قصير" (short fade). Privacy note: "لا يُطلب رقم جوال العميل في الحجز الحضوري. إن رغب العميل بتذكير واتساب، يُدعى لتحميل التطبيق ومسح رمز المحل." (Customer mobile is not requested for walk-ins; if they want WhatsApp reminders they are invited to install the app and scan the shop QR.) → **walk-in DTO has no phone field.**

Summary "ملخص الحجز": الخدمة حلاقة شعر; الحلاق سلطان الحربي; يبدأ ٥:١٢ م; ينتهي ٥:٤٢ م (start + duration); الإجمالي 60 ر.س (informational total, not a charge). CTA "تسجيل الحجز" (Register booking) → toast "سُجل الحجز الحضوري وحُجب الوقت عن التطبيق" (walk-in registered and time blocked from app). Footnote "سيُحجب هذا الوقت فوراً عن الحجز الإلكتروني".

**Proposed**
- Route: `/[locale]/shop/walk-in` (also opened from top-bar CTA; optional `?professionalId=&startsAt=` prefill from calendar).
- `GET /api/v1/shop/services?active=true` (shop's own services), `GET /api/v1/shop/professionals?serviceId=` → `[{ id, name, initial, availability: { state: AvailableNow|BusyUntil|NextAt, at } }]`.
- `GET /api/v1/shop/availability?serviceId=&professionalId=&date=` → bookable slots from the **same availability engine** (spec §11).
- `POST /api/v1/shop/bookings/walk-in` body `{ serviceId, professionalId, startsAt | startNow:true, customerName, note?, idempotencyKey }` → 201 `ShopBookingDetailDto` / 409 `SlotConflict`. Status on create: proposed `Arrived` when "start now", else `Confirmed`. Source = `WalkIn`. Snapshot price/duration.
- Permission: `Shop.Bookings.CreateWalkIn`.

### 1.5 `s-hours` — الدوام والاستراحات والإجازات (Hours, breaks & time off) — 2461–2550; script 4332–4371

**Layout:** 1.4fr left column (weekly hours, recurring breaks) + 1fr right column (pause online booking, time off).

1. "دوام المحل الأسبوعي" (Shop weekly hours), side note "يحدد الحد الأقصى لدوام الحلاقين" (sets the maximum for professionals' hours); sub "أي تعديل يطبّق على الأيام القادمة فقط ولا يلغي المواعيد المؤكدة." (Changes apply to upcoming days only and do not cancel confirmed appointments.) Rows `weekHours` (4333–4343): toggle (open/closed), day, from, "إلى", to, note.
   - الأحد–الأربعاء 9:00 AM – 11:00 PM; الخميس 9:00 AM – 12:00 AM note "ذروة" (peak); الجمعة 2:00 PM – 12:00 AM note "بعد الصلاة" (after prayer); السبت closed "—" note "مغلق" (row at 55% opacity).
   - Fields: `dayOfWeek, isOpen, opensAt, closesAt, note?` (note appears to be a free label — confirm; it could be derived). Closing "12:00 ص" = midnight → support close time ≥ 24:00 / overnight.
2. "الاستراحات المتكررة" (Recurring breaks), sub "تُحذف تلقائياً من الأوقات المتاحة كل يوم مختار." (Removed automatically from availability on each selected day). Rows `breaks` (4344–4348): name, days, time range, edit & delete icons (inert): استراحة صلاة العصر / كل الأيام / 3:30–4:00 PM; استراحة الغداء / الأحد إلى الخميس / 1:00–1:45 PM; صلاة المغرب / كل الأيام / 6:05–6:25 PM (note 5-minute precision). Dashed "+ إضافة استراحة" (Add break). **Breaks are shop-wide; no professional scoping shown** (calendar shows the Asr break in all columns).
3. Pause online bookings (2506–2529), state `bookingPaused` (toggle `togglePause` 4362 → toasts "أُوقف الحجز الإلكتروني مؤقتاً" / "عاد الحجز الإلكتروني للعمل"):
   - Live: white card, title "إيقاف الحجز الإلكتروني مؤقتاً" (Temporarily pause online booking); body "استخدمه عند الازدحام أو الظروف الطارئة. لن يُلغي أي موعد مؤكد، وسيختفي المحل من نتائج البحث حتى تعيد التفعيل." (Use when crowded/emergency; won't cancel confirmed appointments; the shop disappears from search results until re-enabled.)
   - Paused: amber card, "الحجز الإلكتروني موقوف مؤقتاً" (Online booking is paused); "المحل مخفي الآن من الاكتشاف ولا تُقبل حجوزات جديدة. المواعيد المؤكدة سارية ولم تُلغَ." (Shop hidden from discovery; no new bookings; confirmed appointments remain.)
   - No confirm dialog, no reason, no auto-resume time in design (proposed: optional `resumeAt`, confirm dialog).
4. "الإجازات والتعطيل" (Time off & closures), sub "إجازة الحلاق تحذف أيامه فقط؛ إجازة المحل تغلق اليوم بالكامل." (Professional leave removes only their days; shop closure closes the whole day.) Rows `timeOff` (4367–4371): icon (plane = professional, store = whole shop), who, range, state badge (سارية الآن Active now — amber; مجدولة Scheduled — blue), effect text:
   - ماجد العتيبي · ١٦ — ٢٤ سبتمبر · سارية الآن · "أوقاته محذوفة من التطبيق، وبقية الحلاقين يعملون كالمعتاد."
   - المحل بالكامل · الجمعة ٢٥ سبتمبر — اليوم الوطني (National Day) · مجدولة · "اليوم مغلق كلياً ولا تُقبل أي حجوزات فيه."
   - راكان المطيري · ٢ — ٥ أكتوبر · مجدولة · "سيُنبَّه العملاء أصحاب المواعيد القائمة لإعادة الجدولة." (Customers with existing appointments will be notified to reschedule.)
   - Dashed "+ تسجيل إجازة" (Register leave).

**Absent here but required (spec §13):** per-professional working hours editor ("Professional schedule visibility"); break/time-off create/edit forms (only add buttons drawn); conflict preview when time off overlaps existing bookings.

**Proposed**
- Route: `/[locale]/shop/schedule` (tabs or sections: hours, breaks, time-off, pause); optional sub-route `/[locale]/shop/schedule/professionals/[professionalId]`.
- `GET/PUT /api/v1/shop/opening-hours` → `[{ dayOfWeek, isOpen, opensAt, closesAt, note? }]` + `rowVersion`. Server validates future-only effect and returns count of affected future bookings (warning, never auto-cancel).
- `GET/POST /api/v1/shop/breaks`, `PUT/DELETE /api/v1/shop/breaks/{id}` → `{ id, name, daysOfWeek[], startTime, endTime, professionalIds[] | appliesToAll }`.
- `GET/POST /api/v1/shop/time-off`, `PUT/DELETE /api/v1/shop/time-off/{id}` → `{ id, scope: Shop|Professional, professionalId?, professionalName?, startDate, endDate, reason?, state (derived: Active|Scheduled|Past), affectedBookingCount }`.
- `GET/PUT /api/v1/shop/professionals/{id}/working-hours` (shop may edit working hours per spec §7 "manage working hours"; cannot edit profile).
- `POST /api/v1/shop/online-booking/pause` `{ reason?, resumeAt? }`, `POST /api/v1/shop/online-booking/resume`.
- Permissions: `Shop.Schedule.Manage`, `Shop.OnlineBooking.Pause`.

### 1.6 `s-services` — الخدمات والاشتراك (Services & subscription) — 2552–2605; script 4373–4385

**Layout:** 1.5fr services card + 1fr column (subscription card, renewal history).

**As designed (conflicts with spec — see Deviations D3):**
- Header "الخدمات المتاحة في محلك" (Services available in your shop); copy "الأسعار والمدد تُدار من إدارة المنصة. دورك هو تفعيل ما تقدمه فعلياً — والتفعيل ينعكس فوراً على صفحة محلك." (Prices and durations are managed by platform admin. Your role is to activate what you actually offer — activation reflects instantly on your shop page.)
- Rows `shopServices` (4373–4380): toggle, name, desc, price "60 ر.س", duration "٣٠ دقيقة", grey badge "🛡 تُدار من المنصة" (Managed by platform). Samples: حلاقة شعر (قص وتصفيف مع غسيل) 60/30 on; تهذيب لحية 35/20 on; باقة شعر ولحية (الأكثر حجزاً في محلك) 85/50 on; حلاقة أطفال (للأعمار حتى ١٢ سنة) 45/25 on; عناية بالوجه 70/40 off; صبغة شعر (غير مفعّلة — لا يقدمها محلك) 120/60 off.
- Footer info: "تحتاج خدمة غير موجودة أو سعراً مختلفاً؟ أرسل طلباً لإدارة المنصة ويُراجع خلال يوم عمل." (Need a missing service or a different price? Send a request to platform admin; reviewed within one business day.)

**Subscription card** (navy, 2580–2590): eyebrow `SUBSCRIPTION`; status badge نشط (Active); plan name "باقة المحل — سنوي" (Shop plan — annual); "تنتهي في ٣٠ نوفمبر ٢٠٢٦ · يتبقى ٧٣ يوماً" (ends 30 Nov 2026 · 73 days left); progress bar (80% elapsed) with start "١ ديسمبر ٢٠٢٥" and end "٣٠ نوفمبر ٢٠٢٦"; note "التجديد يتم عبر إدارة المنصة. سيصلك تنبيه قبل ٣٠ و١٥ و٧ أيام من الانتهاء." (Renewal is done via platform admin; you'll be alerted 30, 15 and 7 days before expiry — hardcoded thresholds, make configurable.)

**Renewal history** "سجل التجديد" `renewals` (4381–4385): period + amount: 1 Dec 2025 – 30 Nov 2026 · 2,400 SAR; 2024–2025 · 2,400 SAR; 2023–2024 · 1,900 SAR (price change across renewals → versioned plan prices, spec §15).

**Proposed (corrected per spec §10/§13):**
- Routes: `/[locale]/shop/services` (list, reorder, active toggle, archive), `/[locale]/shop/services/new`, `/[locale]/shop/services/[serviceId]` (edit), `/[locale]/shop/subscription` (subscription card + renewal history; may stay on the same page as a side column to match design).
- `GET /api/v1/shop/services?includeArchived=` → `[{ id, nameAr, nameEn, descriptionAr, descriptionEn, categoryId, categoryName, price, currency, durationMinutes, isActive, isArchived, displayOrder, assignedProfessionalCount, moderationState, rowVersion }]`.
- `POST /api/v1/shop/services`, `PUT /api/v1/shop/services/{id}`, `PATCH /api/v1/shop/services/{id}/active` `{ isActive }`, `POST /api/v1/shop/services/{id}/archive`, `PUT /api/v1/shop/services/order` `{ orderedIds[] }`. No hard delete when referenced by bookings.
- `GET /api/v1/shop/subscription` → `{ planName (localized), status, startsAt, endsAt, daysRemaining, elapsedPercent, expiringSoonThresholdDays, renewals: [{ periodStart, periodEnd, amount, currency }] }` (read-only).
- Permissions: `Shop.Services.Manage`, `Shop.Subscription.Read`.

### 1.7 `s-settings` — إعدادات المحل والإشعارات (Shop settings & notifications) — 2607–2654; script 4387–4416

**Layout:** 1.4fr profile card + 1fr column (notification prefs, account permissions).

1. "ملف المحل" (Shop profile):
   - Logo tile 120×120 with camera button (upload); cover dropzone "صورة الغلاف — 1600×900" + "أعلى جودة تعني ظهوراً أفضل في الاكتشاف" (higher quality = better discovery visibility).
   - Fields `shopFields` (4389–4401), 2-col grid; locked fields show grey bg + shield icon:
     | Label | Sample | Locked |
     |---|---|---|
     | اسم المحل — Shop name | صالون الأصالة للحلاقة | no |
     | الحي — District | الملقا | no |
     | العنوان — Address (full width) | طريق أنس بن مالك، الرياض | no |
     | رقم المحل — Shop phone | +966 11 456 7890 | no (shop's own business number — not customer data) |
     | عدد الحلاقين — Number of professionals | ٦ حلاقين | **yes** (derived; admin-owned) |
     | حالة التوثيق — Verification status | موثّق (Verified) | **yes** |
   - No save button drawn; no map/location picker (see §3 Location picker).
   - Spec §13 says profile editing only for fields permitted by admin policy → lock state must come from the API (`editableFields[]`), not be hard-coded.
2. "الإشعارات" (Notifications) "من يُخطر، ومتى." (Who is notified, and when.) Toggles `shopNotifPrefs` (4402–4407), all on:
   - حجز جديد — New booking — "تنبيه فوري داخل اللوحة + صوت" (instant in-dashboard alert + sound)
   - إلغاء من العميل — Customer cancellation — "تنبيه فوري ليُعاد فتح الوقت" (instant alert so the time reopens)
   - ملخص يومي — Daily summary — "واتساب ٩:٠٠ م بمواعيد الغد" (WhatsApp at 9 PM with tomorrow's appointments) — not in spec §16 event list; needs a shop WhatsApp recipient + template (decision).
   - تنبيه انتهاء الاشتراك — Subscription expiry alert — "قبل ٣٠ و١٥ و٧ أيام" (hardcoded thresholds).
3. "صلاحيات حسابك" (Your account permissions) read-only list `shopPerms` (4409–4416): ✓ إدارة مواعيد محلك وتغيير حالتها (manage your appointments & status); ✓ تعديل الدوام والاستراحات والإجازات (edit hours, breaks, leave); ✓ تفعيل الخدمات التي يقدمها محلك (activate services your shop offers); ✗ إنشاء أو حذف الحلاقين (create/delete professionals); ✗ تعديل الأسعار والمدد (edit prices & durations) — **conflicts with spec**; ✗ عرض أرقام العملاء أو تصدير بياناتهم (view customer numbers or export their data).

**Proposed**
- Routes: `/[locale]/shop/settings` (profile), `/[locale]/shop/settings/location` (map picker — absent from design), `/[locale]/shop/settings/notifications`, `/[locale]/shop/notifications` (in-app inbox — absent from design, bell only).
- `GET /api/v1/shop/profile` → `{ nameAr, nameEn, district, addressLine, businessPhone, logoUrl, coverUrl, professionalCount (ro), verificationStatus (ro), editableFields[], location { lat, lng, formattedAddress } , rowVersion }`; `PUT /api/v1/shop/profile` (only editable fields accepted server-side).
- `POST /api/v1/shop/profile/logo`, `POST /api/v1/shop/profile/cover` (multipart, validated type/size, cover 1600×900 recommended).
- `PUT /api/v1/shop/profile/location` `{ lat, lng, formattedAddress, placeId? }` (only if policy allows).
- `GET/PUT /api/v1/shop/notification-preferences` → `{ newBooking, customerCancellation, dailySummary, subscriptionExpiry }`.
- `GET /api/v1/shop/me/permissions` → list rendered in the "your permissions" card (derive from real policy, fix the wording per spec).
- `GET /api/v1/shop/notifications?unread=`, `POST /api/v1/shop/notifications/{id}/read`, `POST /api/v1/shop/notifications/read-all`.
- Permissions: `Shop.Profile.Edit`, `Shop.Location.Edit` (policy-gated), `Shop.Notifications.Manage`.

---

## 2. Platform Admin dashboard

### 2.0 Admin shell (2661–2688)

- Sidebar caption `PLATFORM ADMIN`. Nav = `GROUPS[4]` (3408–3415):
  1. نظرة عامة على المنصة — Platform overview (chart)
  2. المحلات وتفاصيل المحل — Shops & shop details (store)
  3. الحلاقون والنقل والخدمات — Professionals, **transfer** & services (users) → **rename** to "الحلاقون وخدماتهم" (Professionals & their services); see Deviation D1
  4. الخدمات العامة والإسناد — **Global services** & assignment (tag) → rename to "الخدمات والتصنيفات" (Services & categories); see D2
  5. المواعيد والعملاء — Appointments & customers (list)
  6. الاشتراكات والتجديد — Subscriptions & renewal (card)
  7. التقييمات وQR وواتساب — Reviews, QR & WhatsApp (star)
  8. الأدوار والصلاحيات والسجل — Roles, permissions & log (shield)
- Sidebar footer: avatar "ن ع", "نورة العتيبي" (Noura Al-Otaibi), role "مدير عمليات" (Operations manager).
- Top bar: H2 title; subtitle "منصة تريمي · الرياض · ١٢٨ محلاً نشطاً" (TRIMME platform · Riyadh · 128 active shops — conflicts with the KPI "114 active of 128"; read total/active from the API); search "ابحث في المحلات والحلاقين" (search shops and professionals); bell; global CTA "+ إضافة محل" (Add shop).
- Proposed: `GET /api/v1/admin/me` → `{ name, initials, roles[], permissions[] }` (nav items hidden when a permission is missing; the API still enforces). `GET /api/v1/admin/search?q=` → shops + professionals (masked numbers; never matches on customer phone).
- Routes root: `/[locale]/admin/...`.

### 2.1 `a-overview` — نظرة عامة على المنصة (Platform overview) — 2690–2764; script 4418–4451

**Layout:** 4-col KPI grid (8 cards) → grid 1.5fr/1fr (14-day stacked bars | top services) → grid 1.3fr/1fr (top shops table | expiring subscriptions).

1. KPIs `adminKpis` (4419–4428) — matches the spec §17 KPI list:

| Label | Sample | Delta | Proposed computation |
|---|---|---|---|
| مواعيد اليوم — Appointments today | 412 | +8% عن أمس (vs yesterday) | count bookings starting today (shop-local date), excl. cancelled — define |
| مكتملة — Completed | 318 | 77% من اليوم | completed today / today total |
| معدل الإلغاء — Cancellation rate | 7.4% | −0.6 نقطة (green = good) | cancelled / total over a defined window |
| معدل عدم الحضور — No-show rate | 5.1% | +0.9 نقطة (red) | no-show / total (define denominator) |
| محلات نشطة — Active shops | 114 | من ١٢٨ محلاً (of 128) | shops not suspended with Active/ExpiringSoon subscription / all shops |
| اشتراكات قرب الانتهاء — Subscriptions expiring soon | 9 | خلال ٣٠ يوماً | status ExpiringSoon (configurable threshold) |
| حلاقون نشطون — Active professionals | 540 | +12 هذا الشهر | professionals with status Active; delta = created this month |
| عملاء جدد — New customers | 1,284 | +18% عن الشهر الماضي | customers registered this month vs last month |

2. "الحجوزات خلال ١٤ يوماً" (Bookings over 14 days): stacked bars `trendBars` (4429–4434), days ٥…١٨; bottom = مكتملة (completed; navy when ≥92% of max else #6D9BCB), top = ملغاة ولم يحضر (cancelled & no-show, #C9DAEB). Data `[{ date, completed, cancelledOrNoShow }]`.
3. "الخدمات الأكثر حجزاً" (Most booked services) `topServices` (4435–4438): name + count + bar — حلاقة شعر 4,120; باقة شعر ولحية 2,860; تهذيب لحية 1,940; حلاقة أطفال 1,120; عناية بالوجه 640. **Services are shop-owned, so aggregate by category (or normalized name), not by a global service id.**
4. "المحلات الأعلى أداءً هذا الشهر" (Top-performing shops this month) `topShops` (4439–4445): columns المحل (name + district), مواعيد (appointments), إلغاء (cancel %; red if >10%), تقييم (rating ★). Samples: صالون الرواد/العليا 612 4.1% 4.9; صالون الأصالة/الملقا 548 5.2% 4.8; باربر هاوس/حطين 431 6.8% 4.7; لمسة الرجل/النرجس 389 11.4% (red) 4.5; صالون النخبة/الياسمين 354 7.0% 4.6.
5. "اشتراكات تحتاج متابعة" (Subscriptions needing follow-up), sub "تنتهي خلال ٣٠ يوماً" `expiring` (4446–4451): name, date text, status badge, link "تجديد" (Renew): باربر هاوس "ينتهي ٢٧ سبتمبر" قرب الانتهاء; لمسة الرجل "انتهى ١٢ سبتمبر" منتهي; صالون النخبة "ينتهي ٣ أكتوبر" قرب الانتهاء; حلاقة المدينة "موقوف بطلب الإدارة" (suspended by admin) موقوف.

**Proposed**
- Route: `/[locale]/admin` (alias `/[locale]/admin/overview`).
- `GET /api/v1/admin/dashboard/kpis?date=` → `{ appointmentsToday, appointmentsYesterday, completedToday, cancellationRate, cancellationRatePrev, noShowRate, noShowRatePrev, activeShops, totalShops, expiringSubscriptions, activeProfessionals, professionalsAddedThisMonth, newCustomersThisMonth, newCustomersPrevMonth }`.
- `GET /api/v1/admin/dashboard/booking-trend?days=14`; `GET /api/v1/admin/dashboard/top-services?period=month&limit=5`; `GET /api/v1/admin/dashboard/top-shops?period=month&limit=5` → `[{ shopId, name, district, appointments, cancellationRate, rating }]`; `GET /api/v1/admin/subscriptions?status=ExpiringSoon,Expired,Suspended&limit=4`.
- Permission: `Admin.Dashboard.View`.

### 2.2 `a-shops` — المحلات وتفاصيل المحل (Shops & shop details) — 2766–2816; script 4453–4470

**Layout:** 1.6fr shops table + 1fr "Add new shop" form card. Despite the title "وتفاصيل المحل" (and shop details), **no shop detail screen is drawn**.

- Filter chips (static): الكل · ١٢٨ (All 128, active), نشط · ١١٤ (Active 114), قرب الانتهاء · ٩ (Expiring 9), موقوف · ٥ (Suspended 5); inline-end filter "الحي" (District). There is no "Expired" chip although rows show منتهي — add one.
- Columns: المحل (initials avatar + name + id "SH-1024"), الحي (district), الحلاقون (professional count), الاشتراك (subscription status badge), التقييم (rating), ⋯ (row actions, inert).
- Rows `adminShops` (4453–4461): أص صالون الأصالة للحلاقة SH-1024 الملقا 6 نشط 4.8; بھ باربر هاوس SH-1031 حطين 4 قرب الانتهاء 4.7; لر لمسة الرجل SH-1047 النرجس 3 منتهي 4.5; رو صالون الرواد SH-1002 العليا 8 نشط 4.9; نخ صالون النخبة SH-1058 الياسمين 5 قرب الانتهاء 4.6; حم حلاقة المدينة SH-1066 السويدي 2 موقوف 4.2; كل كلاسيك بربر SH-1071 قرطبة 4 نشط 4.7.
- "إضافة محل جديد" (Add new shop) form `newShopFields` (4463–4469):

  | Label | Sample | Type |
  |---|---|---|
  | اسم المحل — Shop name | صالون الياسمين للحلاقة | text (needs ar + en) |
  | الحي — District | الياسمين | text/select |
  | العنوان — Address | طريق الملك عبدالعزيز، الرياض | free text — **no map/pin** |
  | جوال المسؤول — Manager mobile | +966 55 302 1174 | E.164, LTR, Inter |
  | الباقة — Plan | سنوي — 2,400 ر.س | select of SuperAdmin plans (label must be data-driven; see D5) |

  - Info box "حساب دخول المحل" (Shop login account): "يُنشأ حساب برقم جوال المسؤول، ويصله رابط تفعيل عبر واتساب. لا يرى هذا الحساب سوى بيانات محله." (An account is created with the manager's mobile and an activation link is sent by WhatsApp; this account sees only its own shop's data.)
  - Buttons "إنشاء المحل والحساب" (Create shop & account) → toast "أُنشئ المحل وأُرسل رابط التفعيل عبر واتساب"; "إلغاء" (Cancel).

**Proposed**
- Routes: `/[locale]/admin/shops?status=&district=&q=&page=`; `/[locale]/admin/shops/new` (full form incl. location picker; the side card can stay on desktop as a quick-create); `/[locale]/admin/shops/[shopId]` (detail — absent from design: profile, location, account/users, subscription, professionals, services, QR, status); `/[locale]/admin/shops/[shopId]/edit`.
- `GET /api/v1/admin/shops?status=&district=&q=&page=` → `Paged<AdminShopListItemDto { id, code, initials, nameAr, nameEn, district, professionalCount, subscriptionStatus, rating, isSuspended }>` + `counts { all, active, expiringSoon, expired, suspended }`.
- `POST /api/v1/admin/shops` `{ nameAr, nameEn, district, addressLine, location { lat, lng, formattedAddress }, managerMobile (E.164), planId, subscriptionStartDate, durationDays?, sendInvite }` → 201 `{ shopId, code, inviteStatus }` (invite via WhatsApp outbox; mobile never logged in clear).
- `GET/PUT /api/v1/admin/shops/{id}`; `PUT /api/v1/admin/shops/{id}/location`; `PUT /api/v1/admin/shops/{id}/profile-policy` `{ editableFields[] , locationEditableByShop }`; `POST /api/v1/admin/shops/{id}/suspend` `{ reason }`; `POST /api/v1/admin/shops/{id}/activate`; `GET /api/v1/admin/shops/{id}/users`; `POST /api/v1/admin/shops/{id}/account/resend-invite`.
- Permissions: `Admin.Shops.View`, `Admin.Shops.Create`, `Admin.Shops.Edit`, `Admin.Shops.Suspend`, `Admin.Shops.ManageAccount`. All writes audited.

### 2.3 `a-pros` — الحلاقون والنقل والخدمات (Professionals, transfer & services) — 2818–2874; script 4472–4490

**Layout:** 1.5fr professionals table + 1fr column (transfer card — **must be removed**; assigned-services card).

- Header "الحلاقون — ٥٤٠ حلاقاً" (Professionals — 540), button "+ إضافة حلاق" (Add professional — **no form drawn**).
- Columns: الحلاق (round avatar placeholder + name + specialty `skill`), المحل (shop — exactly one), الخدمات (assigned service count), الحالة (state badge نشط / إجازة / معطّل), ⋯.
- Rows `adminPros` (4473–4480): فيصل القحطاني "تدريج وفيد" (fades) صالون الأصالة 5 نشط; سلطان الحربي "لحية وعناية" صالون الأصالة 4 نشط; راكان المطيري "حلاقة كلاسيك" صالون الأصالة 3 نشط; ماجد العتيبي "حلاقة كلاسيك" صالون الرواد 4 إجازة; عمر السالم "أطفال" باربر هاوس 2 نشط; زياد الغامدي "عناية بالوجه" لمسة الرجل 3 معطّل.
- **Transfer card (2843–2859) — forbidden by spec §7, do not implement:** title "نقل حلاق بين المحلات" (Transfer a professional between shops); copy "إجراء إداري حصري. مواعيد الحلاق القائمة في المحل القديم تبقى وتُغلق بعد تنفيذها."; fields الحلاق (select: سلطان الحربي), من (from, read-only: صالون الأصالة) → إلى (to: باربر هاوس); amber warning "٣ مواعيد قادمة في المحل القديم — ستبقى سارية، ولن يظهر الحلاق للحجز الجديد إلا في المحل الجديد."; button "تنفيذ النقل" (Execute transfer) → toast "نُقل سلطان الحربي إلى باربر هاوس" (4481).
- "الخدمات المسندة — سلطان الحربي" (Assigned services — Sultan Al-Harbi) `assignServices` (4486–4490): checkbox rows, name + meta "price · duration": حلاقة شعر 60 ر.س · ٣٠ د ✓; تهذيب لحية 35 · ٢٠ ✓; باقة شعر ولحية 85 · ٥٠ ✓; حلاقة أطفال 45 · ٢٥ ☐; عناية بالوجه 70 · ٤٠ ✓. No save button drawn. **Keep this panel** (spec: the admin assigns professional-services), but it must list only the professional's own shop's services, with that shop's price and duration.

**Absent but required (spec §7, §8, §14):** professional create/edit form — name (ar/en), photo, specialty/bio, shop (select at creation, **read-only afterwards**), status (active/disabled; "إجازة" should be derived from time off rather than a manual status), WhatsApp number (E.164, validated, **masked** in lists, e.g. `+966 5• ••• •174`), WhatsApp notifications toggle, audited "reveal/correct number" action, schedule view; disable action.

**Proposed (corrected)**
- Routes: `/[locale]/admin/professionals?shopId=&status=&q=`; `/[locale]/admin/professionals/new`; `/[locale]/admin/professionals/[professionalId]` (profile, services, schedule, WhatsApp settings).
- `GET /api/v1/admin/professionals?shopId=&status=&q=&page=` → `Paged<{ id, name, specialty, photoUrl, shopId, shopName, assignedServiceCount, status, whatsAppMasked, whatsAppEnabled }>`.
- `POST /api/v1/admin/professionals` `{ shopId, nameAr, nameEn, specialtyAr?, specialtyEn?, photo?, whatsAppNumber?, whatsAppEnabled }`; `PUT /api/v1/admin/professionals/{id}` (**`shopId` is not accepted** — immutable in v1); `POST /api/v1/admin/professionals/{id}/disable`, `/enable`.
- `PUT /api/v1/admin/professionals/{id}/whatsapp` `{ number, enabled }`; `POST /api/v1/admin/professionals/{id}/whatsapp/reveal` (returns the full number once; audited).
- `GET /api/v1/admin/professionals/{id}/services` → `[{ shopServiceId, name, price, currency, durationMinutes, assigned }]` (from the professional's shop only); `PUT /api/v1/admin/professionals/{id}/services` `{ shopServiceIds[] }` (server rejects ids that belong to another shop — composite FK).
- Permissions: `Admin.Professionals.View`, `.Create`, `.Edit`, `.Disable`, `Admin.Professionals.ManageWhatsApp`, `Admin.Professionals.RevealWhatsApp`, `Admin.Professionals.AssignServices`. **No transfer permission.**

### 2.4 `a-services` — الخدمات العامة والإسناد (Global services & assignment) — 2876–2911; script 4492–4510

**As designed (conflicts with spec — Deviation D2):**
- "كتالوج الخدمات العام" (Global service catalogue); copy "مصدر الحقيقة الوحيد للأسماء والأسعار والمدد. أي تعديل هنا ينعكس على كل المحلات المفعِّلة للخدمة." (The single source of truth for names, prices and durations. Any edit here reflects on every shop that activated the service.)
- Columns: الخدمة (name + category), السعر (price, SAR), المدة (duration), المحلات ("N محلاً", shops using it), ✎ edit.
- Rows `globalServices` (4492–4499): حلاقة شعر / شعر / 60 / ٣٠ دقيقة / 126; تهذيب لحية / لحية / 35 / 20 / 121; باقة شعر ولحية / باقات / 85 / 50 / 118; حلاقة أطفال / شعر / 45 / 25 / 92; عناية بالوجه / عناية / 70 / 40 / 48; صبغة شعر / شعر / 120 / 60 / 21. Categories seen: شعر (Hair), لحية (Beard), باقات (Packages), عناية (Care).
- "إسناد خدمة للمحلات" (Assign a service to shops): "«عناية بالوجه» — اختر المحلات المخوّلة بتقديمها، ثم يقرر كل محل تفعيلها." (choose the shops authorized to offer it; each shop then decides whether to activate). `assignShops` (4500–4509): checkbox, name, district, state pill — مفعّلة لدى المحل (activated by the shop, green) / غير مفعّلة بعد (not activated yet) / غير مسندة (not assigned). Button "حفظ الإسناد" (Save assignment) → toast "حُفظ إسناد الخدمة للمحلات المختارة".

**Proposed (corrected per spec §7 Platform Admin, §10, §14)** — replace with three admin areas in the same visual language:
1. **Service categories** (the category column survives): `/[locale]/admin/services/categories` — table: name ar/en, display order, active, "used by N shop services". `GET/POST /api/v1/admin/service-categories`, `PUT /api/v1/admin/service-categories/{id}`, `POST .../{id}/deactivate` (no delete while in use).
2. **Platform-wide shop services (visibility + moderation)**: `/[locale]/admin/services?shopId=&categoryId=&state=&q=` — columns: الخدمة (name + category), **المحل (shop)**, السعر (shop's own), المدة (shop's own), الحالة (active / archived / hidden by moderation), ⋯ (moderate / support override). `GET /api/v1/admin/shop-services?...`; `POST /api/v1/admin/shop-services/{id}/moderation` `{ action: Hide|Restore|Deactivate, reason }`; `PUT /api/v1/admin/shop-services/{id}/support-override` `{ changes, reason }` (explicit, one service at a time, permission-gated, audited; never a bulk cross-shop edit).
3. **Packages** (platform-controlled package definitions, only if kept) — must not impose a shared price or duration on shops.
- The "assign service to shops" panel is removed; professional-service assignment lives in 2.3.
- Permissions: `Admin.ServiceCategories.Manage`, `Admin.ShopServices.View`, `Admin.ShopServices.Moderate`, `Admin.ShopServices.SupportOverride`, `Admin.Packages.Manage`.

### 2.5 `a-appointments` — المواعيد والعملاء (Appointments & customers) — 2913–2962; script 4512–4526

**Layout:** 1.6fr global appointments table + 1fr customer profile side card.

- Chips: اليوم · ٤١٢ (Today 412, active), هذا الأسبوع (This week), filter "المحل · الحالة · المصدر" (shop · status · source).
- Columns: الوقت (time + date "اليوم"), العميل والخدمة (customer + service), المحل والحلاق (shop + professional), الحالة, المصدر. **No row action, detail view or intervention is drawn.**
- Rows `adminAppts` (4512–4520): 5:30 PM عبدالله الشمري باقة شعر ولحية / صالون الأصالة فيصل القحطاني / مؤكد / التطبيق; 5:45 تركي الشهري حلاقة شعر / صالون الرواد ماجد العتيبي / بانتظار التأكيد / رمز QR; 6:00 ناصر الزهراني تهذيب لحية / باربر هاوس عمر السالم / مؤكد / التطبيق; 4:15 مشعل العتيبي حلاقة أطفال / كلاسيك بربر زياد الغامدي / حضر العميل / حضوري; 3:00 بدر الصالح حلاقة شعر / صالون الأصالة سلطان الحربي / مكتمل / التطبيق; 2:30 يزيد القرني عناية بالوجه / لمسة الرجل زياد الغامدي / ملغي / التطبيق; 12:00 وليد العمري حلاقة شعر / صالون النخبة عمر السالم / لم يحضر / رمز QR.
  - Sample-data inconsistency: زياد الغامدي appears under two shops (كلاسيك بربر, لمسة الرجل) and عمر السالم under two (باربر هاوس, صالون النخبة). Seed data must respect one-shop-per-professional.
- Customer card: avatar "ع ش", name عبدالله الشمري, "CU-10482 · عميل منذ يناير ٢٠٢٥" (customer since Jan 2025); stat tiles 24 حجز (bookings), 2 إلغاء (cancellations), 0 عدم حضور (no-shows); "آخر الحجوزات" (Recent bookings) `customerHistory` (4521–4526): service, date · shop, status (باقة شعر ولحية · ١٨ سبتمبر · صالون الأصالة · مؤكد; حلاقة شعر · ٤ سبتمبر · صالون الأصالة · مكتمل; تهذيب لحية · ٢١ أغسطس · صالون الرواد · مكتمل; عناية بالوجه · ٩ أغسطس · لمسة الرجل · ملغي). Privacy note: "رقم العميل مرئي للإدارة فقط ولأغراض الدعم، ويُسجَّل كل اطلاع في سجل النشاط." (The customer's number is visible to admins only, for support; every view is logged.) **No reveal control is drawn and the phone is not displayed.**

**Proposed**
- Routes: `/[locale]/admin/bookings?range=today|week|custom&shopId=&status=&source=&q=`; `/[locale]/admin/bookings/[bookingId]` (detail + history + intervention — absent); `/[locale]/admin/customers?q=`; `/[locale]/admin/customers/[customerId]` (absent as a screen; sitemap node 3546 "العملاء — الملف والسجل").
- `GET /api/v1/admin/bookings?...` → `Paged<{ id, reference, startsAt, customerId, customerName, serviceName, shopId, shopName, professionalId, professionalName, status, source }>` + counts.
- `GET /api/v1/admin/bookings/{id}` (full status history + WhatsApp dispatches); `POST /api/v1/admin/bookings/{id}/transitions` `{ targetStatus, reason }` (authorized intervention, audited); `POST /api/v1/admin/bookings/{id}/reschedule` `{ startsAt, professionalId, reason, idempotencyKey }` (same availability + exclusion-constraint path).
- `GET /api/v1/admin/customers?q=&page=` → `{ id, code, name, registeredAt, bookingCount, lastBookingAt }`; `GET /api/v1/admin/customers/{id}` → `{ code, name, registeredAt, stats { bookings, cancellations, noShows }, upcoming[], previous[], phoneMasked }`; `POST /api/v1/admin/customers/{id}/contact/reveal` `{ reason }` → `{ phone }` + AuditEntry "اطلاع على رقم عميل".
- Permissions: `Admin.Bookings.View`, `Admin.Bookings.Intervene`, `Admin.Customers.View`, `Admin.Customers.ViewContact` (audited). **No export endpoint.**

### 2.6 `a-subs` — الاشتراكات والتجديد (Subscriptions & renewal) — 2964–3010; script 4528–4542

**Layout:** 4 KPIs → grid 1.6fr table | 1fr renew form.

- KPIs `subKpis` (4528–4533): اشتراكات نشطة (Active) 114 "89% من المحلات"; تنتهي خلال ٣٠ يوماً (Expiring within 30 days) 9 "تحتاج تواصلاً" (needs contact); منتهية (Expired) 5 "مخفية من الاكتشاف" (hidden from discovery); إيراد الاشتراكات (Subscription revenue) 284K "ر.س سنوياً" (SAR/year — recorded amounts only, not collected online).
- Table columns: المحل (name + district), الباقة (plan), تنتهي في (ends on), الحالة (status), إجراء (action link). Rows `subsRows` (4534–4541): صالون الأصالة/الملقا سنوي ٣٠ نوفمبر ٢٠٢٦ نشط "عرض السجل" (view history); باربر هاوس/حطين سنوي ٢٧ سبتمبر ٢٠٢٦ قرب الانتهاء "تجديد" (renew); صالون النخبة/الياسمين نصف سنوي (semi-annual) ٣ أكتوبر ٢٠٢٦ قرب الانتهاء "تجديد"; لمسة الرجل/النرجس سنوي ١٢ سبتمبر ٢٠٢٦ منتهي "تفعيل" (activate); حلاقة المدينة/السويدي سنوي — موقوف "مراجعة" (review); صالون الرواد/العليا سنوي ١٤ يناير ٢٠٢٧ نشط "عرض السجل".
- Renew form "تجديد اشتراك" (Renew subscription), context "باربر هاوس — ينتهي بعد ٩ أيام" (ends in 9 days): المدة (Duration) segmented ٣ أشهر / ٦ أشهر / سنة (selected) — hardcoded; تاريخ البداية (Start date) "١ أكتوبر ٢٠٢٦"; الإجمالي (Total) "2,400 ر.س" — hardcoded; button "تأكيد التجديد" (Confirm renewal) → toast "جُدد اشتراك باربر هاوس حتى ٣٠ سبتمبر ٢٠٢٧" (renewed until 30 Sep 2027 → implies `end = start + duration − 1 day`; define and test).
- **Not drawn:** plan picker, shop-specific override, reason/notes, full history view (link only), suspend/unsuspend, plan management (SuperAdmin).

See §5 for the full proposed model.

### 2.7 `a-reviews` — التقييمات وQR وواتساب (Reviews, QR & WhatsApp) — 3012–3068; script 4544–4561

**Layout:** 1.4fr review moderation list | 1fr column (QR analytics card, WhatsApp log card). Details and proposals in §4.

### 2.8 `a-roles` — الأدوار والصلاحيات والسجل (Roles, permissions & log) — 3070–3106; script 4563–4585

**Layout:** 1.5fr permission matrix | 1fr audit timeline. Details and proposals in §4.

---

## 3. Location picker (shop location capture)

### 3.1 Finding: **there is no shop location picker in the design**

Spec §8 (prompt line 407) says shop location capture "must follow the interaction shown in the imported design: location search/manual address, use-current-location…, click or drag the map pin…, display the resolved address and coordinates…". I checked the whole template for map, pin, coordinate, address and location strings. **None of that interaction exists for shops or admins.** The spec's claim does not match the file. This is a headline conflict for the product owner.

What the design actually has for shop location:
- Admin "Add new shop" (`newShopFields` 4465–4466): two **plain text fields**, الحي (District) "الياسمين" and العنوان (Address) "طريق الملك عبدالعزيز، الرياض". There is no map, no coordinates and no search.
- Shop settings profile (`shopFields` 4391–4392): the same two text fields, الحي "الملقا" and العنوان "طريق أنس بن مالك، الرياض" (wide), both editable (unlocked). There is no map and no save button.
- The admin shop detail screen, which would normally hold location, is not drawn.

Closest location/map patterns in the design (all customer-side). Build the picker from these:

| Pattern | Lines | What it shows |
|---|---|---|
| Location-permission bottom sheet | 972–985 | Hatched map placeholder "map preview · Riyadh" (977); white sheet radius 22px; pin icon chip 52px (#EDF3FA / #2C5C8C); title "فعّل الموقع لعرض الأقرب إليك"; primary navy 52px button "السماح بالوصول للموقع" (Allow location access); secondary text button "أدخل الحي يدوياً" (Enter district manually). |
| Location header | 1002–1003 | Caption "موقعك الحالي" (Your current location) + pin + "حي الملقا، الرياض" + chevron-down (opens a selector). |
| Map screen controls | 1133–1145 | Floating white 44px search pill with shadow "ابحث في هذه المنطقة" (Search this area); floating square 44px control button; price-marker pills; **blue current-location dot** (16px #6D9BCB, 3px white border, 6px halo rgba(109,155,203,.25)). |
| Map bottom sheet | 1147–1162 | Grabber bar 40×4 #E1E8F0; white sheet radius 20px; content card. |
| Address row with pin | 1271–1275 | #F7F9FC row, pin icon #6D9BCB, address "طريق أنس بن مالك، حي الملقا، الرياض", trailing link "الاتجاهات" (Directions). |
| Pin glyph | 3326 | `IC.pin` (Lucide-style map-pin path + circle). |
| LTR numeric field | 4467 (manager mobile), 928–929 | Inter font, `direction:ltr`. Use it for coordinates. |

### 3.2 Proposed picker (absent from design, spec requires). Design it in the same visual language.

Used in: Admin → Shop create/edit (`/[locale]/admin/shops/new`, `/[locale]/admin/shops/[shopId]/edit#location`), and Shop → Settings → Location (`/[locale]/shop/settings/location`, only when `locationEditableByShop` policy is true; otherwise read-only map + "تواصل مع الإدارة").

Layout (desktop card inside the form; full-screen sheet on mobile):
1. **Search / manual address**: floating search pill (1133 style) "ابحث عن العنوان أو الحي" with autocomplete results list, plus a text link "أدخل العنوان يدوياً" (984 style) that shows district + street + building fields.
2. **Use current location**: floating square button (1134 style) with a crosshair/pin icon, label "استخدم موقعي الحالي". Enabled only when the browser grants geolocation. When denied, show the "صلاحية مرفوضة" state pattern (4612–4619 style) with a manual fallback.
3. **Map with draggable pin**: map fills the card at 16:9 on desktop and 60vh on mobile. A navy pin marker sits at the centre. The user can click the map or drag the pin. Helper chip: "اسحب الدبوس إلى مدخل المحل بالضبط" (Drag the pin to the exact shop entrance). The current-location dot uses the 1145 style.
4. **Resolved address + coordinates confirmation**: #F7F9FC row (1271 style) with the pin icon and reverse-geocoded address, plus a second line of coordinates in Inter LTR, e.g. `24.8123, 46.6011`. The district is pre-filled from the geocoder and editable.
5. **Confirm / save**: primary navy button "تأكيد الموقع" (Confirm location). Changing an existing location shows an amber WarnNote: "تغيير الموقع يؤثر على المسافة في نتائج البحث".

DTO `ShopLocationDto { latitude (decimal, 6 dp), longitude, formattedAddress, district, city, street?, buildingNumber?, postalCode?, placeId? (provider), source: Search|CurrentLocation|PinDrag|Manual, rowVersion }` stored as PostGIS `geography(Point,4326)` + address columns. Map provider sits behind `IMapProvider` (geocode, reverse-geocode, autocomplete) and a front-end `MapAdapter` component.

Endpoints: `PUT /api/v1/admin/shops/{id}/location`, `PUT /api/v1/shop/profile/location` (policy-gated), `GET /api/v1/geo/autocomplete?q=&near=`, `GET /api/v1/geo/reverse?lat=&lng=` (server-side proxy so provider keys are not exposed; rate-limited).

Permissions: `Admin.Shops.Edit` (admin); `Shop.Location.Edit` (shop, granted per-shop by admin policy). Every change is audited.

---

## 4. WhatsApp, QR, reviews moderation, roles/permissions, audit log (as designed + proposed)

### 4.1 WhatsApp

**As designed**
- Admin log card (3053–3065, `waLog` 4555–4561). Header "سجل رسائل واتساب" with sub "آخر ٢٤ ساعة · ١٬٢٤٠ رسالة · نسبة التسليم ٩٨٪" (last 24 h · 1,240 messages · 98% delivered). Row fields: type icon, **message type** (تأكيد حجز Booking confirmation / تذكير قبل ٣ ساعات Reminder 3 h before / إشعار إلغاء Cancellation notice / تعديل موعد Appointment change), **booking reference + relative time**, and **state** (تم التسليم Delivered / فشل Failed / قيد الإرسال Sending).
- Not drawn: recipient audience (customer vs professional), masked recipient, template version, retry button, error reason, filters, pagination.
- Shop drawer: the "إعادة إرسال التأكيد" button (2390) and the history line "أُرسلت رسالة التأكيد … واتساب — تم التسليم" (4311).
- Customer WhatsApp confirmation mock (1600–1628) shows the rendered customer confirmation content: shop, service, professional, date/time, duration, amount and address, with "الاتجاهات" and "إدارة الموعد" buttons. It is useful as the default Arabic customer-confirmation template.
- Shop notification preference "ملخص يومي — واتساب ٩:٠٠ م بمواعيد الغد" (4405). This is not an event in spec §16.
- Admin shop creation sends the activation link by WhatsApp (2807, 4470).
- Handoff note: "قوالب رسائل واتساب مركزية وقابلة للتعديل من إعدادات النظام" (WhatsApp templates are central and editable from system settings, 4650). The editor itself is **absent**.
- Sitemap nodes: "رسائل واتساب — سجل الحالة" and "إعدادات النظام" (3548).

**Proposed**
- Routes: `/[locale]/admin/whatsapp/templates`, `/[locale]/admin/whatsapp/templates/[templateId]` (editor + versions), `/[locale]/admin/whatsapp/dispatches?status=&audience=&event=&bookingRef=`.
- Template model: `WhatsAppTemplate { id, event (BookingConfirmed|BookingUpdated|BookingCancelled|Reminder|…), audience (Customer|Professional), locale (ar|en), activeVersionId }`, `WhatsAppTemplateVersion { id, version, body, placeholders[], providerTemplateName?, status (Draft|Active|Archived), createdBy, createdAt }`.
- Placeholder whitelist: `{customer_name} {professional_name} {shop_name} {service_name} {booking_date} {booking_time} {time_remaining}`. Professional templates never get a customer phone placeholder.
- Endpoints: `GET /api/v1/admin/whatsapp/templates`; `POST /api/v1/admin/whatsapp/templates/{id}/versions` (draft); `POST .../versions/{vid}/validate`; `POST .../versions/{vid}/preview` `{ sampleData }`; `POST .../versions/{vid}/activate`; `POST .../versions/{vid}/test-send` `{ testRecipientId }` (configured safe test recipients only); `GET /api/v1/admin/whatsapp/dispatches?...` → `{ id, bookingReference, event, audience, recipientMasked, templateVersion, status (Queued|Sending|Delivered|Failed|Cancelled), attempts, lastError, createdAt, sentAt }`; `POST /api/v1/admin/whatsapp/dispatches/{id}/retry`; `GET /api/v1/admin/whatsapp/stats?window=24h` → `{ total, deliveryRate }`.
- Reminder offset: **30 minutes** for customer and professional (spec §16). The design's "٣ ساعات" label is wrong.
- Permissions: `Admin.WhatsApp.Templates.View`, `.Edit`, `.Activate`, `.TestSend`, `Admin.WhatsApp.Dispatches.View`, `.Retry`.

### 4.2 QR

**As designed**
- Admin QR analytics card (3038–3052, `qrRows` 4550–4553): KPIs مسح هذا الشهر (scans this month) 3,842 and تحويل إلى حجز (scan→booking conversion) 38%, plus a per-shop scans bar list.
- QR generation / print material is only in the customer-group `c-qr` screen (2084–2110): card "مواد الطباعة — بطاقة QR للمحل" (print materials — shop QR card) with a navy card, logo, QR, shop name and "امسح الرمز واحجز دورك". Bullets: "رمز لكل محل" (one per shop → shop page), "رمز لكل حلاق" (one per professional → professional page, "suitable for the chair mirror"), "كل مسح يُحتسب" (every scan counted; conversion measured in admin). Buttons "PNG / SVG / PDF" download and "ملصق A5" (A5 poster).
- QR landing (2041–2080): badge "دخلت عبر رمز المحل" (entered via shop QR).
- Booking source "رمز QR" appears in shop and admin lists.
- Shop dashboard has no QR screen.

**Proposed**
- Routes: `/[locale]/admin/qr?shopId=` (generate/list codes, download, analytics); optional read-only `/[locale]/shop/qr` (download own shop/professional codes — decision).
- Endpoints: `GET /api/v1/admin/qr-codes?shopId=&professionalId=`; `POST /api/v1/admin/qr-codes` `{ targetType: Shop|Professional, targetId }` → `{ id, slug, url }`; `GET /api/v1/admin/qr-codes/{id}/download?format=png|svg|pdf|a5`; `POST /api/v1/admin/qr-codes/{id}/deactivate`; `GET /api/v1/admin/qr-analytics?period=month` → `{ scans, bookings, conversionRate, byShop: [{ shopId, name, scans, bookings }] }`; public `GET /q/{slug}` → records `QrVisit` then redirects (attribution cookie/session, non-invasive).
- Permissions: `Admin.Qr.Manage`, `Admin.Qr.Analytics.View`.

### 4.3 Reviews moderation

**As designed** (3014–3035, `modReviews` 4545–4549)
- Queue title "التقييمات — بانتظار المراجعة" (Reviews — pending review) with badge "٣ بلاغات" (3 reports).
- Item fields: reviewer initials + name, shop · professional · date, rating (x.0), flag reason, text.
- Flag reasons seen: مُبلَّغ عنه (reported), تقييم منخفض (low rating, grey/soft), يحتوي رقم جوال (contains a phone number — implies automatic phone-pattern detection).
- Actions: نشر (Publish), إخفاء (Hide), تواصل مع المحل (Contact the shop).
- Samples:
  - محمد العنزي · صالون الأصالة · فيصل · ١٧ سبتمبر · 5.0 · مُبلَّغ عنه: "التزام دقيق بالموعد والتدريج نظيف. المكان مرتب والاستقبال محترم."
  - يزيد القرني · لمسة الرجل · زياد · ١٦ سبتمبر · 2.0 · تقييم منخفض: "تأخر الموعد أكثر من نصف ساعة دون اعتذار، والنتيجة لم تكن كما اتفقنا."
  - بدر الصالح · باربر هاوس · عمر · ١٥ سبتمبر · 4.0 · يحتوي رقم جوال: "خدمة ممتازة، تواصلوا معي على الرقم ٠٥xxxxxxx للاستفسار."
- Flag pill colours: red (#FBEAEA/#9B2C2C) for مُبلَّغ عنه and يحتوي رقم جوال; grey for تقييم منخفض (`flagStyle` 4544).
- The audit log confirms that a hide is logged (4584).
- The matrix gives Support "restricted" (eyeOff) rights for hiding reviews.

**Proposed**
- Route `/[locale]/admin/reviews?status=Pending|Published|Hidden&flag=&shopId=`.
- `ReviewModerationDto { id, bookingReference, customerDisplayName, shopId, shopName, professionalName, rating, text, createdAt, flags[] (Reported|LowRating|ContainsPhone|Profanity), status }`.
- Endpoints: `GET /api/v1/admin/reviews?...`; `POST /api/v1/admin/reviews/{id}/publish`; `POST /api/v1/admin/reviews/{id}/hide` `{ reason }`; `POST /api/v1/admin/reviews/{id}/shop-contact` `{ message }` (creates an in-app notification to the shop; the channel needs a decision).
- Rating aggregates are recalculated on hide/publish (spec §17).
- Permissions: `Admin.Reviews.View`, `Admin.Reviews.Moderate`. The matrix's "eyeOff" for Support = propose `Admin.Reviews.Flag` (can flag/queue but not hide), which needs confirmation.
- Decision needed: are reviews published immediately and only flagged ones queued, or are all reviews pre-moderated? The title "بانتظار المراجعة" and the flags suggest the queue holds flagged or low-rated reviews only.

### 4.4 Roles & permissions matrix — exact transcription (3074–3087, `permMatrix` 4569–4578)

Header: "مصفوفة الصلاحيات" / "الصلاحية الأدق تتغلب على العامة. أي تغيير يُسجَّل في سجل النشاط." (the more specific permission overrides the general one; every change is logged).

Columns (roles), exactly as designed: **مدير عام** (General Manager), **مدير عمليات** (Operations Manager), **دعم** (Support), **المحل** (Shop).

Cell legend (`pc` 4563–4568): `1` = ✓ check (green #2E9E6B) = allowed; `2` = eyeOff (amber #D89A2E) = restricted / masked / partial; `0` = ✗ (grey #C9D4E0) = denied.

| # | Permission (Arabic, exact) | English | مدير عام | مدير عمليات | دعم | المحل | Seed guidance |
|---|---|---|---|---|---|---|---|
| 1 | إنشاء وتعديل المحلات | Create & edit shops | ✓ | ✓ | ✗ | ✗ | Seed as `Admin.Shops.Create`, `Admin.Shops.Edit` |
| 2 | إنشاء وتعديل الحلاقين | Create & edit professionals | ✓ | ✓ | ✗ | ✗ | Seed as `Admin.Professionals.Create`, `.Edit` (+ `AssignServices`, `ManageWhatsApp`) |
| 3 | نقل حلاق بين المحلات | Transfer professional between shops | ✓ | ✓ | ✗ | ✗ | **DO NOT SEED. Forbidden by spec §7.** |
| 4 | إدارة الخدمات والأسعار | Manage services & prices | ✓ | ✓ | ✗ | ✗ | **Rename/split**: `Admin.ServiceCategories.Manage`, `Admin.ShopServices.Moderate`, `Admin.ShopServices.SupportOverride`. Shop gets `Shop.Services.Manage` (✓ for المحل, own shop only). |
| 5 | إدارة الاشتراكات | Manage subscriptions | ✓ | ✓ | ✗ | ✗ | Split: `Admin.Subscriptions.Assign/Renew/Suspend` (ops ✓), `Admin.Subscriptions.Override` and `Admin.SubscriptionPlans.Manage` = **SuperAdmin only**. The design gives مدير عمليات full subscription management, which conflicts with spec §7 for plan pricing. |
| 6 | عرض أرقام العملاء | View customer numbers | ✓ | eyeOff | eyeOff | ✗ | `Admin.Customers.ViewContact`. eyeOff = masked by default, reveal-on-demand with reason + audit. Shop is always ✗ (enforced at DTO level). |
| 7 | إدارة مواعيد محل واحد | Manage a single shop's appointments | ✓ | ✓ | ✓ | ✓ | Admin: `Admin.Bookings.Intervene`. Shop: `Shop.Bookings.UpdateStatus` (tenant-scoped). |
| 8 | إخفاء التقييمات | Hide reviews | ✓ | ✓ | eyeOff | ✗ | `Admin.Reviews.Moderate`; Support eyeOff → `Admin.Reviews.Flag` (confirm). |

Assumptions to confirm:
- مدير عام = SuperAdmin. This is an assumption. The spec's SuperAdmin-only powers (plan CRUD/pricing, overrides) are not represented as separate rows.
- The matrix is display-only. There is no role CRUD, no user-role assignment and no per-permission toggle. Spec §14 "Roles and permissions" implies management UI, which is absent.
- Shop sub-roles: the appointment log shows "بواسطة سارة (استقبال)" (by Sarah, reception) at 4310. That implies multiple shop users with a "Reception" role, which the spec does not define. **Open question.** Proposal: `ShopOwner` and `ShopStaff` (reception) roles with the same tenant scope; staff cannot edit services, hours, the profile or pause bookings, pending confirmation.

Proposed full permission catalogue (seed; names are ours):
- Shop (tenant-scoped): `Shop.Bookings.Read`, `Shop.Bookings.UpdateStatus`, `Shop.Bookings.CreateWalkIn`, `Shop.Bookings.ResendNotification`, `Shop.Schedule.Read`, `Shop.Schedule.Manage`, `Shop.OnlineBooking.Pause`, `Shop.Services.Manage`, `Shop.Profile.Edit`, `Shop.Location.Edit` (policy), `Shop.Subscription.Read`, `Shop.Notifications.Manage`.
- Admin: `Admin.Dashboard.View`; `Admin.Shops.View|Create|Edit|Suspend|ManageAccount`; `Admin.Professionals.View|Create|Edit|Disable|AssignServices|ManageWhatsApp|RevealWhatsApp`; `Admin.ServiceCategories.Manage`; `Admin.ShopServices.View|Moderate|SupportOverride`; `Admin.Packages.Manage`; `Admin.Bookings.View|Intervene`; `Admin.Customers.View|ViewContact`; `Admin.Reviews.View|Moderate|Flag`; `Admin.Subscriptions.View|Assign|Renew|Suspend`; `Admin.Qr.Manage`, `Admin.Qr.Analytics.View`; `Admin.WhatsApp.Templates.View|Edit|Activate|TestSend`, `Admin.WhatsApp.Dispatches.View|Retry`; `Admin.Roles.View|Manage`; `Admin.Audit.View`; `Admin.Settings.View|Edit`.
- SuperAdmin-only: `SuperAdmin.SubscriptionPlans.Manage` (create/edit/price/publish/archive/order), `SuperAdmin.Subscriptions.Override`.
- Seed roles: `SuperAdmin` (all), `OperationsManager` (all Admin.* except SuperAdmin.*; ViewContact reveal-with-reason), `Support` (View permissions + Bookings.Intervene + Customers.ViewContact reveal-with-reason + Reviews.Flag), `ShopOwner`, `ShopStaff` (pending decision).
- Endpoints: `GET /api/v1/admin/roles`, `GET /api/v1/admin/permissions`, `PUT /api/v1/admin/roles/{id}/permissions`, `POST /api/v1/admin/roles`, `GET/PUT /api/v1/admin/users/{id}/roles`. Route `/[locale]/admin/roles`.

### 4.5 Audit log

**As designed** (3089–3105, `auditLog` 4579–4585)
- Vertical timeline. Each entry has a coloured dot by category (blue = professionals, amber = pricing, green = subscriptions, grey = privacy, red = moderation), a title, a sentence with actor and object, and a relative time.
- Entries, exactly as designed:
  - نقل حلاق (Professional transfer): "نورة العتيبي نقلت «سلطان الحربي» من صالون الأصالة إلى باربر هاوس", اليوم ٤:٥٢ م. **Forbidden feature; do not seed.**
  - تعديل سعر خدمة (Service price edit): "تغيير سعر «عناية بالوجه» من 65 إلى 70 ر.س — ٤٨ محلاً متأثراً" (48 shops affected), اليوم ٢:١٠ م. **Global price; replace with a per-shop support-override example.**
  - تجديد اشتراك (Subscription renewal): "تجديد اشتراك «صالون الرواد» لمدة سنة", أمس ١١:٣٠ ص.
  - اطلاع على رقم عميل (Customer number viewed): "فريق الدعم اطلع على رقم العميل CU-10482 لمعالجة شكوى" (support viewed the number to handle a complaint), أمس ٩:٠٥ ص.
  - إخفاء تقييم (Review hidden): "إخفاء تقييم يحتوي رقم تواصل مباشر — باربر هاوس", ١٦ سبتمبر.
- There are no filters, search, entity links or before/after diff.

**Proposed**
- Route `/[locale]/admin/audit?actor=&action=&entityType=&entityId=&from=&to=`.
- `GET /api/v1/admin/audit?...` → `Paged<AuditEntryDto { id, at, actorUserId, actorName, actorRole, action, entityType, entityId, entityLabel, summaryAr, summaryEn, reason?, before?, after? (redacted: no phone numbers) }>`.
- Must record: shop/pro create/edit/suspend, WhatsApp number reveal/change, customer contact reveal, service moderation/support override, category changes, subscription assign/renew/override/suspend, plan and price changes, template activation, role/permission changes, review hide/publish, booking interventions, settings changes.
- Permission `Admin.Audit.View`. The log is append-only, with no edit or delete endpoints.

---

## 5. Subscriptions (as designed + proposed)

### 5.1 As designed

- **Plan fields visible:**
  - Plan name "باقة المحل — سنوي" (shop plan — annual) (2585).
  - Plan labels سنوي (annual) and نصف سنوي (semi-annual) in the admin table (4535–4540).
  - Plan option "سنوي — 2,400 ر.س" in Add Shop (4468).
  - Price amounts in renewal history: 2,400 / 2,400 / 1,900 SAR (4381–4385). This shows the price changed across periods.
- **Subscription fields:**
  - status
  - start date "١ ديسمبر ٢٠٢٥"
  - end date "٣٠ نوفمبر ٢٠٢٦"
  - days remaining "٧٣"
  - elapsed-progress bar
  - renewal history rows (period + amount)
  - action per status: عرض السجل (view history) for Active, تجديد (renew) for ExpiringSoon, تفعيل (activate) for Expired, مراجعة (review) for Suspended
- **Statuses:** نشط Active, قرب الانتهاء ExpiringSoon, منتهي Expired, موقوف Suspended (3640–3643). These match spec §15 exactly.
- **Renewal UI (2990–3007):**
  - Duration presets ٣ أشهر / ٦ أشهر / سنة.
  - Start date.
  - Computed total 2,400 SAR.
  - "تأكيد التجديد" (Confirm renewal).
  - There is no payment capture. The action is a manual record, which fits spec §15 "recorded manually".
- **Override UI:** none.
- **Plan editor (SuperAdmin):** none.
- **Price history:** only the shop-side renewal list.
- **Effects stated in copy:**
  - Expiry hides the shop from discovery while existing appointments stay valid (2192, 4531 "مخفية من الاكتشاف", 4618). The shop state card says "لا تُقبل حجوزات جديدة حتى يُجدَّد".
  - The shop is alerted 30/15/7 days before expiry (2589, 4406).
  - The shop sidebar shows "الاشتراك نشط حتى ٣٠ نوفمبر" (2133).
  - The shop overview shows an amber expiry warning (2190–2194).
- **KPI:** "إيراد الاشتراكات 284K ر.س سنوياً" (4532). This is subscription revenue from recorded renewals, not payment collection.

### 5.2 Proposed (spec §7 SuperAdmin, §15)

- **SuperAdmin plan management** (absent from design; spec requires):
  - Routes: `/[locale]/admin/subscription-plans`, `/[locale]/admin/subscription-plans/new`, `/[locale]/admin/subscription-plans/[planId]`.
  - Table columns: name (ar/en), price + currency, billing interval (e.g. 30/90/180/365 days or months), status (Draft/Published/Inactive/Archived), available to new shops, display order, active subscriptions count.
  - The editor adds descriptions ar/en, features list, limits (e.g. max professionals), trial/grace days and a **price history** tab.
- `SubscriptionPlanDto { id, nameAr, nameEn, descriptionAr, descriptionEn, currentPrice { amount, currency, effectiveFrom, versionId }, billingInterval { unit: Day|Month, count }, features[], limits { maxProfessionals?, … }, trialDays?, graceDays?, status, availableForNewShops, displayOrder, rowVersion }`.
- `SuperAdmin` endpoints:
  - `GET/POST /api/v1/admin/subscription-plans`
  - `PUT /api/v1/admin/subscription-plans/{id}`
  - `POST .../{id}/publish|deactivate|archive`
  - `PUT /api/v1/admin/subscription-plans/order`
  - `POST /api/v1/admin/subscription-plans/{id}/prices` `{ amount, currency, effectiveFrom }` (new version; never rewrites activated subscriptions)
  - `GET .../{id}/prices`
- **Shop subscription operations** (ops admin):
  - Routes: `/[locale]/admin/subscriptions?status=`, `/[locale]/admin/shops/[shopId]/subscription` (current + full history), renew/assign as a side panel.
  - Assign/renew form: plan picker (published plans), start date, duration (from the plan interval or an explicit number of days such as 30), a price snapshot shown read-only, notes, and an optional **override** block (SuperAdmin only) with custom price/duration and a mandatory reason. There is no payment UI; the button text stays "تأكيد التجديد" / "تسجيل التفعيل".
  - `ShopSubscriptionDto { id, shopId, planId, planName, priceSnapshot, currency, startDate, endDate, status (computed: Active|ExpiringSoon|Expired|Suspended), daysRemaining, override?: { price?, durationDays?, reason, by, at } }`.
  - `SubscriptionRenewalDto { id, periodStart, periodEnd, planName, amount, currency, recordedBy, recordedAt, notes, isOverride }`.
  - Endpoints:
    - `GET /api/v1/admin/subscriptions?status=&q=&page=` (+ KPI counts, revenue)
    - `GET /api/v1/admin/shops/{id}/subscription`
    - `POST /api/v1/admin/shops/{id}/subscription/assign` `{ planId, startDate, durationDays? }`
    - `POST /api/v1/admin/shops/{id}/subscription/renew` `{ planId?, startDate, durationDays?, notes, idempotencyKey }`
    - `POST /api/v1/admin/shops/{id}/subscription/override` `{ price?, durationDays?, reason }` (**SuperAdmin**)
    - `POST /api/v1/admin/shops/{id}/subscription/suspend|reinstate` `{ reason }`
    - `GET /api/v1/admin/shops/{id}/subscription/history`
- **Platform settings:**
  - `expiringSoonThresholdDays` (default 30)
  - `expiryReminderOffsetsDays` [30,15,7]
  - `expiredShopEnforcement` (HideFromDiscovery + BlockNewBookings; keep existing bookings), stated explicitly per spec §15
- Shop read-only view: see 1.6 (`GET /api/v1/shop/subscription`).

---

## 6. Deviations / conflicts with the spec (with line numbers and corrected workflow)

| # | Deviation (as designed) | Lines | Spec rule | Corrected workflow |
|---|---|---|---|---|
| D1 | **Barber transfer between shops.** Admin card "نقل حلاق بين المحلات" with from/to selects, upcoming-appointments warning and "تنفيذ النقل" button; toast "نُقل سلطان الحربي إلى باربر هاوس". Also in: nav title "الحلاقون **والنقل** والخدمات"; permission matrix row "نقل حلاق بين المحلات"; audit entry "نقل حلاق … من صالون الأصالة إلى باربر هاوس"; sitemap node "نقل حلاق بين المحلات"; state `toastMove`. | 2843–2859, 4481, 3410, 4572, 4580, 3544 | §7: "There is no transfer barber between shops feature. Do not implement, display, document, or scaffold…" | Remove the card, toast, permission, audit example and sitemap node. Rename the nav to "الحلاقون وخدماتهم". The professional's shop is chosen at creation and shown read-only in edit. Put the professional create/edit form in the freed right column (§2.3). Do not offer any workaround flow. |
| D2 | **Global services with platform-set price/duration.** "كتالوج الخدمات العام — مصدر الحقيقة الوحيد للأسماء والأسعار والمدد. أي تعديل هنا ينعكس على كل المحلات"; editable price/duration column; "assign service to shops" panel; audit "تغيير سعر «عناية بالوجه» من 65 إلى 70 ر.س — ٤٨ محلاً متأثراً"; matrix row "إدارة الخدمات والأسعار"; sitemap "الخدمات العامة — سعر · مدة" + "إسناد الخدمات للمحلات"; nav "الخدمات العامة والإسناد". | 2876–2911, 2881, 4492–4510, 4581, 4573, 3545, 3411 | §7 Platform Admin, §10: services are tenant-owned; no global price or duration; admin manages categories, moderation, audited support override, professional-service assignment. | Replace with Categories + Platform-wide shop services (view/moderate/override per service, audited) + Packages (§2.4). Remove "assign to shops". Aggregate analytics by category. |
| D3 | **Shop services are toggle-only; the shop cannot set price or duration.** Copy "الأسعار والمدد تُدار من إدارة المنصة. دورك هو تفعيل ما تقدمه"; badge "تُدار من المنصة"; "request a different price from admin" note; permission ✗ "تعديل الأسعار والمدد"; ✓ "تفعيل الخدمات التي يقدمها محلك"; sitemap "الخدمات المتاحة — تفعيل فقط" and "تعديل الأسعار — ممنوع". | 2556–2575, 2557, 2570, 2575, 4412, 4414, 3537, 3539 | §7 Shop, §10, §13: the shop creates/edits its own services (localized name, description, price, duration, active, ordering, archive). | Build a shop service CRUD: list with toggle, drag-to-reorder, edit (✎) and archive, plus "+ إضافة خدمة" and a create/edit form (§7 A1). Remove the "تُدار من المنصة" badge (optionally show a "مخفية من الإدارة" moderation badge when applicable). Change the shop permission list to ✓ "إدارة خدمات محلك وأسعارها ومددها". |
| D4 | **Professional-service assignment shows global prices.** `assignServices` meta "60 ر.س · ٣٠ د" comes from the global catalogue. | 4486–4490, 2861–2870 | §10: services are per shop. | List only the professional's shop's services with that shop's price and duration. Admin assigns (the shop cannot, spec §7). |
| D5 | **Hardcoded plan price and durations.** Add-shop plan "سنوي — 2,400 ر.س"; renew presets ٣/٦ أشهر/سنة and total 2,400; plan labels سنوي/نصف سنوي. | 4468, 2994–3003, 4535–4540 | §7 SuperAdmin, §15: plans, prices and durations are data-driven, versioned and SuperAdmin-managed. | Populate the plan picker and durations from the `/subscription-plans` API. Show the price snapshot read-only. Add the SuperAdmin plan editor and a price-history tab. |
| D6 | **Subscription management granted to Operations Manager** in the matrix, with no SuperAdmin distinction. | 4574 | §7: only SuperAdmin alters plans and prices and applies overrides. | Split permissions (§4.4 row 5). |
| D7 | **Reminder timing.** The WhatsApp log shows "تذكير قبل ٣ ساعات" (reminder 3 hours before). | 4557 | §16: customer and professional reminders exactly **30 minutes** before. | Reminder offset = 30 min (a platform setting, default 30). Label: "تذكير قبل ٣٠ دقيقة". |
| D8 | **Missing Confirm transition + unconstrained status actions.** The drawer offers حضر العميل / مكتمل / لم يحضر / ملغي for every booking. There is no "تأكيد" (Confirm) for Pending. The footer "إلغاء" duplicates ملغي. Filter chips omit حضر العميل and لم يحضر. | 4301–4307, 2373–2378, 2391, 4264 | §11 state machine: Pending→Confirmed→Arrived→Completed, plus side transitions; invalid transitions rejected server-side. | Render only `allowedTransitions` from the API: Pending → [Confirm, Cancel]; Confirmed → [Arrived, NoShow, Cancel]; Arrived → [Completed]; terminal → none. Put cancel in the footer with a reason dialog. Add the missing chips. Toast wording stays. |
| D9 | **Single "ملغي" status.** No distinction between customer and shop cancellation. | 3638, 4301 | §11: customer cancellation vs shop cancellation. | Two enum values sharing one badge colour, with sub-labels "ألغاه العميل" / "ألغاه المحل". |
| D10 | **Contact mediation feature not in spec.** Drawer copy references a «طلب تواصل» (request contact) button handled by the TRIMME team. The button is not drawn; the state card CTA "طلب تواصل عبر المنصة" also mentions it. | 2370, 4617 | Not in spec. The phone rule is satisfied. | Decision: either drop the sentence or implement a simple support ticket (`POST /shop/bookings/{id}/contact-requests`) visible to admin support. Never expose the phone. |
| D11 | **Discovery side-effects not stated in the spec.** (a) Pausing online booking hides the shop from search/discovery. (b) An expired subscription hides the shop from discovery and blocks new bookings. (c) Professional time off triggers "customers with existing appointments will be notified to reschedule". (d) The Suspended shop state has no copy. | 2512, 2524; 2192, 4531, 4618; 4370 | §11 (pause = no new online bookings), §15 (enforcement explicit in settings; do not alter future bookings). | Record each as an explicit decision in `DECISIONS.md` with a platform-settings flag. For (c), do not auto-cancel: flag affected bookings to the shop and admin, and send a customer "please reschedule" template only if approved. |
| D12 | **Hardcoded expiry reminder thresholds** "قبل ٣٠ و١٥ و٧ أيام"; overview warning shown at 73 days. | 2589, 4406, 2191 | §15: expiring-soon threshold configurable. | Read from platform settings. Warning shown when status = ExpiringSoon. |
| D13 | **Shop daily-summary WhatsApp** "ملخص يومي — واتساب ٩:٠٠ م بمواعيد الغد". | 4405 | §16 lists only customer/professional booking events. | Optional feature. Needs a shop WhatsApp recipient, a template and a schedule setting. The summary must contain no customer phone numbers. Otherwise, drop it. |
| D14 | **Shared professional in sample data.** زياد الغامدي appears under كلاسيك بربر and لمسة الرجل. عمر السالم appears under باربر هاوس and صالون النخبة. ماجد العتيبي appears in صالون الأصالة's own dashboard (proLoad 4202, timeOff 4368) but at صالون الرواد in admin (4477, 4514). Headcount mismatch: the sidebar says "٦ حلاقين" (2131), but the calendar and admin list show 3 professionals for الأصالة. | 4516/4518, 4515/4519, 4202/4368 vs 4477/4514, 2131 | §7: each professional belongs to exactly one shop. | Fix the seed/demo data. |
| D15 | **Shop profile lock state hardcoded** (name, district, address, phone editable; count and verification locked). | 4389–4401 | §13: editable only for fields permitted by admin policy. | Drive `editableFields[]` from an admin policy per shop and enforce server-side. |
| D16 | **Admin customer phone reveal is copy-only**, with no control. | 2956–2959 | §7, §14: protected contact data; audited access. | Add a masked phone plus a "إظهار الرقم" button with a reason prompt → audit entry (§2.5). |
| D17 | **Shop search** "ابحث باسم العميل أو رقم الحجز". | 2143 | §7: no phone in the shop surface. | The backend search must match only name and booking reference, never phone, including partial matches. |
| D18 | **Calendar granularity** fixed at 30-minute rows; a 7:15 booking sits in the 7:30 row. Breaks at 6:05–6:25 cannot render accurately. | 4231–4242, 4193, 4347 | §11: configurable slot step (5-minute capable). | Minute-accurate positioning, as described in §1.2. |
| D19 | **Admin header subtitle** "١٢٨ محلاً نشطاً" contradicts the KPI "114 active of 128". | 2681, 4424 | — | Bind to the API. |

Checked and **compliant (keep as is)**:
- **Customer phone hidden from the shop.** The drawer shows the note "رقم العميل غير متاح" (2366–2372). The walk-in form asks for no phone (2439–2442). The shop permission list denies viewing or exporting customer numbers (4415). The permission-state card is at 4617. The handoff note says "the phone never reaches the shop UI from the server — enforced at the contract level" (4649). No phone field appears in any shop row data (4189–4198, 4273–4280, 4294–4300).
- **No customer export button** exists anywhere in the shop or admin scope. The "PNG / SVG / PDF" and "ملصق A5" buttons (2108–2109, `c-qr`) download QR print material, not data. Do not flag them as an export.
- **No payment or checkout UI.**
  - Drawer "المبلغ — 85 ر.س — يُدفع في المحل" (paid at the shop, 4299).
  - Walk-in "الإجمالي 60 ر.س" (2453).
  - Overview price column (2183).
  - Subscription renewal total (3003) and revenue KPI (4532).

  These are informational amounts or manual records, **not payment UI. Do not remove them.** The shop "renewal history" shows amounts. That is the shop's own data and is acceptable.
- **The shop cannot create or delete professionals.** No such control is drawn for the shop; the permission ✗ is at 4413, and the sitemap says "ممنوع" at 3539. The shop can register professional time off (4367–4370), which is allowed ("manage … time off").

---

## 7. Spec requirements absent from the design (design these in the same visual language)

Each item is **absent from design, spec requires**. Build it from the shared components in §0.2.

| # | Missing piece | Spec ref | Suggested composition |
|---|---|---|---|
| A1 | **Shop service create/edit form** (name ar/en, description ar/en, category select, price + currency, duration (5-min steps), active, display order, archive; "used in N bookings" guard) | §7, §10, §13 | Right-side drawer (the 2344 SideDrawer pattern) or a full page with 2-col field grid (2620 style); list rows get ✎ edit and ⋮ archive; "+ إضافة خدمة" as DashedAddButton. |
| A2 | **Shop location picker** (search/manual, current location, draggable pin, resolved address + coordinates, confirm) | §8 (line 407), §13, §14 | See §3.2. |
| A3 | **Professional working-hours editor + professional-scoped breaks** | §11, §13 | Reuse the weekly-hours row (2470–2481) per professional with a professional selector; break form with "applies to: all / selected professionals". |
| A4 | **Break / time-off create & edit dialogs** (only "+" buttons drawn) | §13 | Modal: type (professional/shop), professional select, date range, all-day or time range, reason; conflict preview listing affected bookings (count + list, no phones). |
| A5 | **Shop notifications inbox** (bell only, 2144). The admin bell (2684) also has no inbox. | §13, §17 | Popover + `/shop/notifications` list with mark-read; SignalR push. |
| A6 | **Admin shop detail / edit** (profile, location, account users + resend invite, subscription, professionals, services, QR, activate/suspend) | §14 | Tabbed page; header card with status badge + actions. |
| A7 | **Admin professional create/edit** with E.164 WhatsApp number, masking, reveal (audited), notifications toggle, disable/enable; shop read-only after create | §7, §8, §14 | Replace the transfer card position (2843) with this form. |
| A8 | **Admin customers list + profile** (booking count, upcoming/previous, last booking, registration date, protected contact with reveal) | §14 | Promote the side card (2934–2960) into `/admin/customers/[id]`; add list page. |
| A9 | **Admin booking detail + intervention** (status change with reason, reschedule with availability check, history incl. dispatches) | §14 | Reuse the shop drawer (2345–2393) with an admin actions footer. |
| A10 | **SuperAdmin plan editor + versioned price history** | §7, §15 | Table + editor page; price history as timeline (3092 style). |
| A11 | **Subscription assign/override, full history, suspend/reinstate** | §7, §15 | Extend the renew card (2990–3007) with plan picker, override block (SuperAdmin), reason; history timeline. |
| A12 | **WhatsApp template editor** (ar/en, audience, event, placeholders whitelist, preview, validate, activate, version history, safe test send) | §16 | Two-pane: template list | editor with placeholder chips + WhatsApp bubble preview (reuse 1611–1628 styling). |
| A13 | **WhatsApp dispatch log with retry, audience, masked recipient, template version, error** | §14, §16 | Extend the `waLog` row (3057–3063) + filters + retry button on failed rows. |
| A14 | **Admin QR generation/management** (currently only in customer group `c-qr` 2084–2110) | §14, §17 | Page with per-shop/per-professional codes, downloads (PNG/SVG/PDF/A5), analytics (3038–3052). |
| A15 | **Role CRUD + user-role assignment** (matrix is display-only) | §14 | Make matrix cells toggles for editable roles; users tab. |
| A16 | **Audit log page** with filters and entity links | §14 | Full-page version of the timeline (3090–3104) + filter chips. |
| A17 | **Platform settings** (booking policy: lead time, horizon, slot step, cancellation window; locale; currency; time zone; reminder offset (30 min); expiring-soon threshold; expiry enforcement; map defaults (centre, zoom, provider)) — sitemap node "إعدادات النظام" only | §14, §15 | Settings page with grouped cards (2631–2640 style) and a save bar. |
| A18 | **Service categories & packages admin** | §7, §10, §14 | See §2.4. |
| A19 | **Real week calendar** (appointments per day/professional); design's week view is a density heatmap | §13 | Keep the heatmap as a summary strip; add a week grid. |
| A20 | **Confirm dialogs** (pause bookings, cancel booking with reason, suspend shop, renew/override, hide review) and **save buttons** on settings/hours/profile/assignments | general | Modal with WarnNote and primary/secondary buttons. |

---

## 8. Open questions / decisions to record

1. Should shop sub-roles (reception "سارة (استقبال)" 4310) exist in v1, and with which rights?
2. Pause behaviour: does pausing only block new online bookings, or also hide the shop from discovery as the design says?
3. Expired or suspended enforcement: hide from discovery and block new bookings (design). Confirm, and decide whether walk-ins are still allowed.
4. Should professional time off that overlaps existing bookings notify customers automatically?
5. Is the shop daily WhatsApp summary in scope?
6. Is the "طلب تواصل" mediation (support ticket) in scope?
7. Review publishing: post-moderation (flagged-only queue) or pre-moderation?
8. Should the shop be able to download its own QR codes (shop dashboard), or only admins?
9. Should the week view be a heatmap, a grid, or both?
10. Is مدير عام = SuperAdmin? Confirm the seed roles.
11. Status on walk-in "start now": `Arrived` or `Confirmed`?

---

## 9. Route & permission index (proposed)

| Route | Screen | Permission |
|---|---|---|
| `/[locale]/shop` | s-overview | Shop.Bookings.Read |
| `/[locale]/shop/calendar` | s-calendar | Shop.Bookings.Read, Shop.Schedule.Read |
| `/[locale]/shop/appointments` (+ `/[bookingId]` drawer) | s-appointments | Shop.Bookings.Read / UpdateStatus / ResendNotification |
| `/[locale]/shop/walk-in` | s-walkin | Shop.Bookings.CreateWalkIn |
| `/[locale]/shop/schedule` | s-hours | Shop.Schedule.Manage, Shop.OnlineBooking.Pause |
| `/[locale]/shop/services` (+ `/new`, `/[serviceId]`) | s-services (corrected) | Shop.Services.Manage |
| `/[locale]/shop/subscription` | s-services (right column) | Shop.Subscription.Read |
| `/[locale]/shop/settings` (+ `/location`, `/notifications`) | s-settings | Shop.Profile.Edit, Shop.Location.Edit, Shop.Notifications.Manage |
| `/[locale]/shop/notifications` | absent | Shop.Bookings.Read |
| `/[locale]/admin` | a-overview | Admin.Dashboard.View |
| `/[locale]/admin/shops` (+ `/new`, `/[shopId]`, `/[shopId]/edit`, `/[shopId]/subscription`) | a-shops | Admin.Shops.* |
| `/[locale]/admin/professionals` (+ `/new`, `/[professionalId]`) | a-pros (corrected) | Admin.Professionals.* |
| `/[locale]/admin/services`, `/[locale]/admin/services/categories`, `/[locale]/admin/packages` | a-services (corrected) | Admin.ShopServices.*, Admin.ServiceCategories.Manage, Admin.Packages.Manage |
| `/[locale]/admin/bookings` (+ `/[bookingId]`) | a-appointments | Admin.Bookings.View / Intervene |
| `/[locale]/admin/customers` (+ `/[customerId]`) | a-appointments side card | Admin.Customers.View / ViewContact |
| `/[locale]/admin/subscriptions` | a-subs | Admin.Subscriptions.* |
| `/[locale]/admin/subscription-plans` (+ `/new`, `/[planId]`) | absent | SuperAdmin.SubscriptionPlans.Manage |
| `/[locale]/admin/reviews` | a-reviews (left) | Admin.Reviews.* |
| `/[locale]/admin/qr` | a-reviews (QR card) + c-qr print | Admin.Qr.* |
| `/[locale]/admin/whatsapp/templates`, `/[locale]/admin/whatsapp/dispatches` | a-reviews (log card) + absent editor | Admin.WhatsApp.* |
| `/[locale]/admin/roles` | a-roles (matrix) | Admin.Roles.View / Manage |
| `/[locale]/admin/audit` | a-roles (timeline) | Admin.Audit.View |
| `/[locale]/admin/settings` | absent | Admin.Settings.View / Edit |
