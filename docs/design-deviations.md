# TRIMME — Design Deviations

The Claude Design prototype (`design/source/TRIMME.dc.html`) is the **visual** source of truth. Where it conflicts with the business rules of the specification, the visual language is kept and the workflow is corrected (spec operating rule 9, decision D-008).
Line numbers refer to the canonical `TRIMME.dc.html` (4656 lines). The analysis files in `design/analysis/` cite a working copy that is 2 lines longer (subtract 2 from their citations above line 5).

ID prefixes: **DV-S** spec conflict corrected · **DV-A** absent from design, spec requires (designed in TRIMME language) · **DV-T** tokens/visual/a11y fixes · **DV-C** copy/data inconsistencies not replicated · **DV-D** design features deferred.
Status: `planned` (to be applied in the listed phase) · `applied` (with commit) · `deferred`.

## DV-S — Spec conflicts corrected

| ID | Design (as drawn) | Lines (canonical) | Rule | Corrected implementation | Phase | Status |
|---|---|---|---|---|---|---|
| DV-S01 | **Barber transfer between shops**: admin transfer card (from/to selects, "تنفيذ النقل"), toast, nav label "الحلاقون والنقل والخدمات", permission row, audit example, sitemap node | 2841–2857, 4479, 3408, 4570, 4578, 3542 | §7 no transfer | **Removed entirely.** Nav: "الحلاقون وخدماتهم". Professional's shop chosen at creation and read-only afterwards; freed space holds the professional create/edit form. No workaround flow. | 6 | planned |
| DV-S02 | **Global service catalogue** with platform-set price/duration, "assign services to shops", audit "price change affects 48 shops" | 2874–2909, 4490–4508, 4579, 4571, 3543, 3409 | §7, §10 | Admin: service **categories**, platform-wide view of shop-owned services, moderation (deactivate/hide with reason), audited support override, professional-service assignment. No global price/duration. | 7 | planned |
| DV-S03 | Shop services **toggle-only**, "prices/durations managed by the platform" badge, ✗ edit prices | 2554–2573, 4412, 3535, 3537 | §7, §10, §13 | Shop CRUD for own services (ar/en name + description, category, price, duration in 5-min steps, active, ordering, archive, "used in N bookings" guard). | 7 | planned |
| DV-S04 | Professional-service assignment shows global prices | 4484–4488, 2859–2868 | §10 | Lists only the professional's own shop's services with that shop's price/duration; admin-only action. | 7 | planned |
| DV-S05 | Hardcoded plan "سنوي — 2,400 ر.س", fixed renewal presets; Operations Manager has subscription rights | 4466, 2992–3001, 4533–4538, 4572 | §7, §15 | Plans/prices/durations from the API (SuperAdmin-managed, versioned); price snapshot on subscriptions; overrides SuperAdmin-only. | 8 | planned |
| DV-S06 | Reminder **3 hours** before (and a day-before reminder in notifications) | 1568, 1628, 3574, 4555, 4113 | §16 30 min | Setting `ReminderOffsetMinutes` (default 30) for customer and professional; copy reads the configured value. | 15 | planned |
| DV-S07 | Unavailable slots rendered disabled with reason toasts | 3928–3937, 4000 | §11 only bookable slots | API returns only bookable slots; UI groups by period and shows an empty-period message. | 9/12 | planned |
| DV-S08 | Shop drawer offers all status actions for every booking; no "Confirm" action; filter chips omit Arrived/No-show | 4299–4305, 2371–2376, 2389, 4262 | §11 state machine | Render only API `allowedTransitions`; add Confirm; cancel with reason dialog; all status chips. | 13 | planned |
| DV-S09 | Single "ملغي" status | 3636, 4299 | §11 | `CancelledByCustomer` / `CancelledByShop` (shared badge colour, distinct sub-labels). | 10 | planned |
| DV-S10 | "Cancellation request" after cutoff reviewed by shop (copy only) | 1779, 3501 | §11 | No online cancel after cutoff (setting); show policy + shop contact (D-015). | 12 | planned |
| DV-S11 | Home "popular services" tiles show a price with no shop | 1040–1048, 3755 | §10 | Category shortcuts with "from X ر.س" aggregated from nearby shops. | 11 | planned |
| DV-S12 | Packages modelled as a single service row | 3505, 3844, 1398 | §10 | Packages with items, explicit price/duration, shown in a packages section on the shop page (D-020). | 7/11 | planned |
| DV-S13 | Success copy "تم تأكيد حجزك" regardless of status | 1584, 857, 1612 | §11 | Copy depends on resulting status (D-006). | 12 | planned |
| DV-S14 | Hardcoded policy values (2 h free cancellation, 15 min lateness, 7-day review window, 14-day horizon, expiry reminders 30/15/7 days) | 1348–1349, 1568, 1779, 2587, 4404, 3555 | §14, §15 | Platform/shop settings drive values and copy. | 8/14 | planned |
| DV-S15 | Reviews show full customer names | 3819–3821, 1406 (vs promise at 1909) | privacy | First name + surname initial (D-017). | 12 | planned |
| DV-S16 | Shop profile lock state hardcoded | 4387–4399 | §13 | `editableFields` from admin policy per shop, enforced server-side. | 6 | planned |
| DV-S17 | Admin customer phone reveal described but not controlled | 2954–2957 | §7, §14 | Masked phone + "إظهار الرقم" with reason → audited, permission-gated. | 14 | planned |
| DV-S18 | Shop search "by customer name or booking number" | 2141 | §7 | Backend search matches name/booking reference only, never phone. | 13 | planned |
| DV-S19 | Calendar in fixed 30-minute rows | 4229–4240, 4191, 4345 | §11 5-min step | Minute-accurate positioning. | 13 | planned |
| DV-S20 | Payment-method row in profile settings ("طريقة الدفع") | 4139 | §2 no payment | Read-only info row "الدفع في المحل" (no payment settings screen). | 12 | planned |
| DV-S21 | Discovery hiding on pause/expiry stated as fixed behaviour | 2510, 2522, 2190, 4529, 4616, 3787 | §15 explicit settings | Settings-driven (D-013, D-014). | 8/9/11 | planned |
| DV-S22 | Professional time off auto-notifies customers to reschedule | 4368 | §16 events | No automatic customer messages; affected bookings flagged to shop and admin. | 9 | planned |

## DV-A — Absent from design, spec requires (designed in the TRIMME visual language)

| ID | Missing piece | Spec | Phase |
|---|---|---|---|
| DV-A01 | Shop service create/edit form | §7, §10, §13 | 7 |
| DV-A02 | **Shop location picker** (search/manual address, use current location, click/drag pin, resolved address + coordinates, confirm & save). The design only has district/address text fields (4389–4390, 4463–4464). Built from customer-side map/pin patterns (970–983, 1131–1143, 1269–1273). | §8, §13, §14 | 6 |
| DV-A03 | Professional working-hours editor + professional-scoped breaks | §11, §13 | 9 |
| DV-A04 | Break / time-off create & edit dialogs with conflict preview | §13 | 9 |
| DV-A05 | Shop and admin notification inboxes | §13, §17 | 15 |
| DV-A06 | Admin shop detail/edit (profile, location, users/invite, subscription, professionals, services, QR, activate/suspend) | §14 | 5/6 |
| DV-A07 | Admin professional create/edit with E.164 WhatsApp number, masking, audited reveal, notification toggle, disable | §7, §8, §14 | 6 |
| DV-A08 | Admin customers list + profile | §14 | 14 |
| DV-A09 | Admin booking detail + intervention | §14 | 14 |
| DV-A10 | SuperAdmin plan editor + versioned price history | §7, §15 | 8 |
| DV-A11 | Subscription assign/override/history/suspend | §7, §15 | 8 |
| DV-A12 | WhatsApp template editor (ar/en, audience, event, placeholders, preview, validate, activate, versions, safe test send) | §16 | 15 |
| DV-A13 | WhatsApp dispatch log with retry, audience, masked recipient, template version, error | §14, §16 | 15 |
| DV-A14 | Admin QR generation/management + professional QR landing | §14, §17 | 16 |
| DV-A15 | Role CRUD + user-role assignment | §14 | 14 |
| DV-A16 | Audit log page with filters | §14 | 14 |
| DV-A17 | Platform settings page | §14, §15 | 8/14 |
| DV-A18 | Service categories & packages admin | §7, §10, §14 | 7 |
| DV-A19 | Real week calendar grid | §13 | 13 |
| DV-A20 | Confirm dialogs and save bars on editable screens | general | 3+ |
| DV-A21 | Distinct sign-in screen, profile completion (name), manual location selection sheet | §9, §12 | 4/11 |
| DV-A22 | Security/session settings (session list, revoke all), staff forgot/reset password | §9, §12 | 4 |
| DV-A23 | Shop gallery, shop mini-map, shop description, packages section on shop page | §12 | 11 |
| DV-A24 | Booking-conflict state at confirm (typed 409) | §11 | 12 |
| DV-A25 | Expired-session and permission-denied states per surface | §5 | 3/4 |
| DV-A26 | English (LTR) versions of all screens; desktop/tablet layouts for customer screens other than landing | §5, §6 | 2+ |
| DV-A27 | Indexable public shop listing (`/shops`) | §6 | 11 |
| DV-A28 | Legal pages (terms, privacy) linked from sign-up | §9 (terms acceptance) | 11 |

## DV-T — Tokens, visual and accessibility fixes

| ID | Issue | Fix | Phase |
|---|---|---|---|
| DV-T01 | Link `#4A7FB5` = 4.20:1; tertiary greys used for text 2.1–2.8:1 (fail AA) | Darkened text tokens (D-021) | 2 |
| DV-T02 | Tajawal 600 and Inter 800 used but not loaded | Load all used weights (D-022) | 2 |
| DV-T03 | Mixed Arabic-Indic / Latin digits for the same fields | One rule per locale (D-029) | 2 |
| DV-T04 | Nav active indicator uses a physical property; toast centring only works in RTL | Logical properties throughout | 3 |
| DV-T05 | Internal inconsistencies: drawer breakpoint 768/992/1200; slot height 42 vs 44; 6 vs 5 booking steps | Single breakpoint set (390/768/1024/1440 validated in Phase 2); 44px touch targets; 5 steps + confirmation | 2/3 |
| DV-T06 | No WhatsApp brand colour anywhere | Use design neutrals; WhatsApp previews use a neutral bubble style from the design | 15 |

## DV-C — Copy/data inconsistencies not replicated

Weekday/date pairs are from 2025 (e.g. "الخميس ١٨ سبتمبر"; 18 Sep 2026 is a Friday); `addMin` AM/PM bug around noon (server computes end times); professionals shown under two shops in sample data (seed data must respect one shop per professional); headcount mismatches ("٦ حلاقين" vs 3); admin subtitle "١٢٨ محلاً نشطاً" vs KPI 114/128; shop footer "تبدأ من 30" vs cheapest 35; always-on verified shield; favorites header counts; period headers counts; notifications bottom-nav highlight; rating tags auto-selected; stars editable after submit. All demo data is generated from the seed, never copied from the prototype.

DV-C01 — OTP length: design shows 4 digits; default under D-005 is 6 digits for security (pending the user's decision).

## DV-D — Design features deferred

| ID | Feature | Decision |
|---|---|---|
| DV-D01 | "طلب تواصل" contact mediation | D-023 deferred |
| DV-D02 | Shop daily WhatsApp summary | D-024 deferred |
