# TRIMME Session Handoff

- **Updated:** 2026-09-27 (end of Session 4: Phases 06, 07 and 08)
- **Branch:** `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). Phases 06 and 07 are pushed and CI-green (runs 36316322648 and 36323449600). Phase 08 and its follow-ups are **pushed** at the user's request; the CI run is recorded in the Phase 08 file.
- **HEAD commit:** `feat: phase 08 custom-period override (D-081) and map pin race fix`, on top of `7054b4e` (review follow-up) and `2f2b42c` (Phase 08). Run `git log --oneline -4`.
- **Working tree:** clean after the commit.
- **Local Docker stack: running.** It was recreated from an empty volume with the Phase 08 images: web 3300, API 8080, DB 5434, Mailpit UI 8325.
- **Current phase:** 08 is complete. Phase 09 has not started.
- **Phase score:** 100 / 100 (Phase 08). Phases 06 and 07 were also 100 / 100 this session.
- **Last fully completed phase:** 08, subscriptions foundation and platform settings.

## Completed this session
- **Phase 06:** shops, locations and professionals. Images are stored in PostgreSQL (D-064).
- **Phase 07:** services, categories and packages.
- **Phase 08** (`phases/phase-08-subscriptions-settings.md`):
  - **Plans (SuperAdmin only).** Localized plans with bilingual features and a billing interval. Prices are append-only versions from a date (D-079).
  - **Shop subscriptions.** Activation and renewal are recorded manually (no payment). Each period keeps the plan name and price version it was recorded with. Overrides are SuperAdmin-only, need a reason, and keep the previous values; suspend and reinstate are also available. There is no future gap in coverage, and dates follow the platform calendar (D-077).
  - **Status.** Active / ExpiringSoon / Expired / Suspended / None, with the threshold as a setting.
  - **Bookability.** `IShopBookability` reads a coverage row that is not tenant-scoped (D-078).
  - **Platform settings.** One audited, version-checked row; `migrate` inserts the defaults and never overwrites edits (D-076).
  - **Races.** A unique-index race now answers 409 (D-080).
  - **Web:**
    - `/admin/subscription-plans` (list/new/detail with the price timeline);
    - `/admin/subscriptions` (KPIs, filter, search);
    - the shop page's Subscription tab;
    - `/shop/subscription` (warning and history);
    - `/admin/settings`.
  - **Seed:** 3 plans (the annual one with 2 price versions) and one subscription per status. Two extra demo shops without users: Lamsat Al Rajul (Expired) and Al Madina (Suspended).

## Verification evidence (Phase 08)
| Command | Result |
|---|---|
| `dotnet build Trimme.slnx -c Release --no-incremental` | PASS, 0 warnings |
| Unit / architecture / integration tests | PASS, 206 / 63 / 113 at the commit (114 after the review follow-up, which added a features round-trip test; Subscriptions namespace 8/8). Integration was 113/113 on 5 of 6 full runs; one early run had 3 unreproduced timeouts at about 37 s (watch item) |
| `dotnet ef migrations has-pending-model-changes` | PASS, no changes |
| Web gates (`lint`, `typecheck`, `format:check`, `openapi:check`, `test`, `build`) | PASS, 212 web tests |
| Fresh `down -v` + `up --build` + `pnpm e2e` ×3, after the D-081 change and the map-pin race fix | **45/45 ×3** (the cold first run included). Web 217 tests |
| No-transfer grep gate; R-NEG-08 grep gate; gitleaks `dir` + `git` | PASS, no hits, no leaks |

## Database and migrations
- Created this session: `ShopProfileLocationProfessionals` (Phase 06), `ServicesPackages` (Phase 07) and `SubscriptionsSettings` (Phase 08).
- Applied locally only: Testcontainers databases and the compose volume.

## Decisions added
- D-064…D-075 (Phases 06–07).
- **Phase 08:** D-076 platform settings, D-077 subscription periods and dates, D-078 coverage read model and bookability gate (confirmed by the user), D-079 plans and versioned prices, D-080 unique races → 409, D-081 custom periods need a SuperAdmin override (user decision).
- D-013 and D-014 are now **Accepted**.

## Known issues or blockers
- **Phase 08 not pushed.** When the user asks, push and confirm CI is green, then record the run in the Phase 08 file and MASTER_PLAN.
- **User decisions (D-078 confirmed, D-081 implemented).**
  - A shop without a subscription in force is hidden and takes no online bookings, so setup for Phases 10–12 tests must assign a standard subscription. `None` stays available only as a temporary warning-only setting.
  - Custom durations and back-dated starts need a SuperAdmin override with an explicit total and a reason.
- **Watch item: timing.**
  - Intermittent integration timeouts at about 31–37 s (Phases 07 and 08), not reproduced.
  - The earlier "slow map pin" E2E failures were a real race, now fixed.
  - One cold-stack OTP-redirect timeout remains unexplained and did not recur in the final 3 runs.
- **Not enforced yet:** grace/trial days and plan limits are stored only (D-077). Expiry notifications come in Phase 15.
- **Pre-existing, logging only:** Serilog logs handled 400/409 as "responded 500". Fix in Phase 17.
- **Local compose only:** an upload over the limit through the Next.js rewrite gets a bare 500. Production Nginx needs `client_max_body_size 6m` (Phase 17).
- **Deferred:** drag-and-drop reordering; keyboard move buttons exist.
- **Ports:** 8025 is taken on this machine. Use `TRIMME_MAILPIT_PORT=8325` and `E2E_MAILPIT_URL=http://localhost:8325`.

## Exact next action
1. Confirm the recorded CI run is green (Phase 08 file).
2. Start Phase 09 (`phases/phase-09-*.md`). Re-validate first:
   - `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Subscriptions"`
   - `pnpm e2e`
3. Phase 09 should read `IPlatformSettings` for the minimum lead time, horizon and slot step, and `IShopBookability` where availability must be hidden.

## Files intentionally left modified
- None.
