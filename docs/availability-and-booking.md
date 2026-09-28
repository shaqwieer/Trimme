# Availability and booking

This document explains how TRIMME decides which appointment times can be booked. The booking part (state machine, overlap constraint, idempotency, payment seam) is added in Phase 10.

Decisions: D-009 (only bookable slots), D-012 (any professional), D-013 / D-083 (pause), D-014 / D-078 (subscription gate), D-030 (time), D-076 (platform settings), D-082 (engine), D-084 (API).

## Availability

### Inputs

| Input | Owner | Where |
|---|---|---|
| Weekly opening hours | Shop | `availability.shop_opening_hours`: one row per shop, the week as a JSON list of intervals |
| Closed days | Shop | `availability.shop_closures`: inclusive local dates |
| Online-booking pause | Shop | `shops.online_booking_pauses`: the row exists while paused (D-083) |
| Professional's weekly hours | Shop | `availability.professional_working_hours`: "follows the shop's hours" by default |
| Breaks (weekly or one date; one professional or everyone) | Shop | `availability.breaks` |
| Time off (vacation, sick, other) | Shop | `availability.professional_time_off`: UTC instants |
| Service or package duration, and who performs it | Shop (duration), admin (assignment) | Services module, through `IBookableOfferCatalog` |
| Existing non-cancelled appointments | Bookings (Phase 10) | `IBookedTimeReader`; empty until Phase 10 |
| Lead time (60 min), horizon (30 days), slot step (5 min) | SuperAdmin | `administration.platform_settings` (D-076) |
| Subscription in force and shop status | Admin | `IShopBookability` (D-078) |

An interval is expressed in minutes from the weekday's local midnight, on 5-minute steps. It may end after midnight: 21:00–02:00 is stored as `1260–1560`.

### Rules

1. **Free time.** A professional is free where the shop is open and they work: their own hours, or the shop's hours when they follow them. Breaks, time off and existing appointments are then removed.
2. **Closures.** A closure closes the *business day*, meaning every window that opens on that date, including its hours after midnight. The previous night's window that runs into a closed date is not affected.
3. **Slot fit.** A slot needs the whole item, `[start, start + duration)`, to be free. A package is one contiguous appointment with one professional who is assigned to every item in it.
4. **Grid.** Starts sit on the slot-step grid counted from local midnight. With a 5-minute step that gives 8:05, 8:10, 8:15 and so on.
5. **Dates and edges.** Slots are dated by their local start time. Bookable dates run from today to today + horizon − 1. A slot must start at or after now + lead time. Past times are never returned.
6. **Gates.** No slot is offered when:
   - the shop is not active (the API answers 404);
   - the shop takes no online bookings (the API answers 200 with `bookable: false` and the reason):
     - `subscription.none`, `subscription.expired` or `subscription.suspended` (D-078);
     - `shop.paused` (D-083);
   - the item is not published (404);
   - the item is not online-bookable (422);
   - the chosen professional is inactive or belongs to another shop (404), or is not assigned to the item (422).
7. **Any professional.** When no professional is chosen, each slot lists every eligible professional who is free for the whole item. Phase 10 picks one inside the booking transaction (D-012).
8. **Time zones.** The engine works on UTC instants in the shop's IANA time zone.
   - A local time skipped by daylight saving resolves to the first instant after the gap.
   - A repeated local time resolves to its first occurrence.
   - Riyadh has no daylight saving; New York is used in the tests.

`AvailabilityEngine` is pure: every input is passed in, including "now". `FindSlots` lists the slots for a date range. `IsBookable` checks one start with the same rules; Phase 10 uses it for the transactional recheck.

### API

| Endpoint | Who | Purpose |
|---|---|---|
| `GET /api/v1/public/shops/{slug}/availability/dates?serviceId\|packageId&professionalId&from&to` | Anyone (rate limit `availability`) | Slot count per date for the date strip. Default 14 days from today, at most 31 |
| `GET /api/v1/public/shops/{slug}/availability/slots?serviceId\|packageId&professionalId&date` | Anyone (rate limit `availability`) | The bookable starts: instant, end, local `HH:mm`, period (Morning / Afternoon / Evening) and candidate professional ids |
| `GET /api/v1/shop/schedule` | Shop owner and staff (`Shop.Schedule.Read`) | Hours, professionals' hours, current and upcoming closures, breaks and time off, pause state |
| `PUT /api/v1/shop/schedule/opening-hours` | Owner (`Shop.Schedule.Manage`) | Replace the week. Version-checked after the first save |
| `PUT /api/v1/shop/professionals/{id}/working-hours` | Owner | Follow the shop's hours, or set the professional's own week |
| `POST\|PUT\|DELETE /api/v1/shop/schedule/closures\|breaks\|time-off`, plus `POST …/preview` | Owner | Manage entries. The preview lists upcoming appointments the change would overlap |
| `GET /api/v1/shop/online-booking`, `POST …/pause`, `POST …/resume` | Owner (`Shop.OnlineBooking.Pause`) | Pause or resume online booking (audited) |

The public responses never contain professional contact data or booking details. The conflict preview shows the shop the customer's name and the booked item, never a phone number.

### Changes never cancel bookings

Editing hours, adding a break, time off or a closure, and pausing all affect *new* availability only.

- Existing appointments stay as they are.
- No customer is messaged automatically (DV-S22). Instead, the preview shows the shop which appointments overlap, and the shop has to confirm before saving.
- Persisting a "flagged" state on those bookings comes with the Bookings module (Phase 10).

### Performance

One request loads the schedule and the busy times for its whole range once, then computes in memory. A 31-day request covers 288 five-minute starts per day for each eligible professional. A cache shared between requests is not needed yet (see the Phase 09 risks).
