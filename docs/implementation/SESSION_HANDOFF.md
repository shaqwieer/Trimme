# TRIMME Session Handoff

- Updated at: 2026-09-27 (end of Session 4, Phase 06)
- Branch: `main`, tracking `origin/main` (https://github.com/shaqwieer/Trimme.git). Phase 06 is committed locally and **not pushed** (push only when the user asks).
- HEAD commit: a docs/test follow-up on top of `805313b` (Phase 06: `feat: phase 06 shops, locations and professionals; media stored in PostgreSQL`), which sits on `d0d040a`. Run `git log --oneline -3`.
- Working tree status: clean after the commit. The local Docker stack is **running**, recreated from an empty volume with the Phase 06 images (web 3300, API 8080, DB 5434, Mailpit UI 8325).
- Current phase: 06 is complete. Phase 07 has not started.
- Phase score: 100 / 100 (Phase 06)
- Last fully completed phase: 06, shops, locations and professionals

## Completed this session
- **Phase 06** — shops, locations and professionals (`phases/phase-06-shops-professionals.md`):
  - **User request:** photos and other media are **stored in PostgreSQL** (`media.media_files`, D-064), not on disk:
    - validated by content (JPEG/PNG/WebP only);
    - EXIF/GPS and other metadata stripped;
    - served immutable from `GET /api/v1/media/{id}`.
  - **Shop profile:** description, category, own phone, amenities, verification, logo/cover/gallery, and the admin edit policy enforced server-side (D-065).
  - **Exact location:** `geography(Point,4326)` with a GiST index, set through the MapLibre pin picker with search, device location, drag, resolved address and typed fallback. Geocoding goes through the API: Fake in compose/tests/CI, Nominatim in `dotnet run` (D-068).
  - **Professionals:** one shop fixed at creation; encrypted WhatsApp number with a unique lookup hash and a mask; toggle; audited reveal with a reason; disable/enable; photo. No transfer anywhere (D-067, D-011).
  - **Public read scope** for anonymous pages (D-066): `/public/shops/{slug}` and `/public/shops/{slug}/professionals`.
  - **Web:**
    - admin shop detail tabs (profile & images, location, accounts, professionals) with the edit-policy card;
    - `/admin/professionals` list, new and detail;
    - `/shop/settings` and `/shop/settings/location` with locks.
  - **Seed:** demo shop profiles and Riyadh locations, and 5 professionals with fake numbers.
  - **Docs:** D-064…D-069, TRACEABILITY, `docs/domain-model.md` (new), architecture, permissions matrix, design deviations, README, `.env.example` files.

## Verification evidence
- Command: `dotnet build Trimme.slnx -c Release --no-incremental`
  Result: PASS, 0 warnings
- Command: unit / architecture / integration tests
  Result: PASS, 153 / 62 / 97 at the phase commit; 98 integration after the follow-up public-scope data-layer test
- Command: `dotnet ef migrations has-pending-model-changes`
  Result: PASS, no changes
- Command: web gates (`lint`, `typecheck` + E2E, `format:check`, `openapi:check`, `test`, `build`)
  Result: PASS, 179 web tests
- Command: compose on the Phase 05 volume (upgrade), then `docker compose down -v` + `up --build` + `pnpm e2e` ×2
  Result: PASS, 40/40 both runs, including `flows/shops-professionals.spec.ts` (pin drag, cover upload, masked number, reveal, no transfer, owner locks)
- Command: no-transfer grep gate (D-069 form, test files excluded)
  Result: PASS, no hits
- Command: gitleaks `dir` + `git` (Docker; set `MSYS_NO_PATHCONV=1` in Git Bash)
  Result: PASS, no leaks

## Database and migrations
- Created this session: `20260927101016_ShopProfileLocationProfessionals`, adding:
  - the `media` schema with `media_files`;
  - the `professionals` schema with `professionals` and `professional_contacts`;
  - shop profile, policy and location columns, with the GiST index.
- Applied locally only: the Testcontainers databases and the local compose volume, both upgraded from Phase 05 and recreated from empty.

## Decisions added
- D-064 Images stored in PostgreSQL (user request).
- D-065 Shop profile, edit policy and location on the shop row.
- D-066 Public read scope.
- D-067 Professionals and the WhatsApp contact.
- D-068 Map and geocoding adapters (implements D-007).
- D-069 Test infrastructure and gates (test DB `max_connections=400`, multipart in the endpoint matrix, grep gate excludes tests).

## Known issues or blockers
- **Not pushed:** CI has not run on Phase 06 yet. After the user asks to push, check the GitHub Actions run, in particular the OpenAPI drift check (the contract was regenerated on Windows with LF normalization) and the E2E job. WebGL 2.0 is available by default in the Linux Playwright 1.63 image (probed), so the map should render there.
- **Pre-existing, noticed this session:** Serilog request logging runs inside the exception handler, so a handled `RequestValidationException`/`DbUpdateConcurrencyException` is *logged* as "responded 500" while the client correctly gets 400/409. It is logging only; fix the middleware order in Phase 17 (observability).
- Local compose only: an upload body over the limit sent through the Next.js rewrite gets a bare 500 instead of the API's 413, and the browser pre-check prevents it in the UI. Production Nginx needs `client_max_body_size 6m` (Phase 17, D-069).
- Production map tile and geocoder hosts are still configuration to choose (D-007/D-068). Images in the database grow backups (D-064); add Nginx/CDN caching of `/api/v1/media/*` in Phase 17.
- Port 8025 is taken on this machine: `TRIMME_MAILPIT_PORT=8325`, `E2E_MAILPIT_URL=http://localhost:8325`.

## Exact next action
1. If the user wants it, push `main` and confirm CI is green; record the run in the Phase 06 file and MASTER_PLAN.
2. Start Phase 07 (`phases/phase-07-services-packages.md`). Re-validate first with `dotnet test --project tests/Trimme.IntegrationTests -c Release --filter-namespace "*Professionals" --filter-namespace "*Shops"` and `pnpm e2e` against the stack.
3. For Phase 07, three things carry over from this phase:
   - Shop services are `IShopOwned` with `HasShopScopedKey`.
   - Professional–service assignment references professionals through `HasShopScopedReference<…, Professional>`, which completes R-NEG-06.
   - Public service reads use `IPublicDataScope` in `*.Application.Public`.

## Files intentionally left modified
- None.
