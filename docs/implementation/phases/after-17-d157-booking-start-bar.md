# After Phase 17 — D-157: «احجز الآن» starts a new booking on the service step, and the wizard's bar sits at the bottom of the screen

**Status:** [x] except the real phone check (Session 32, 2026-11-20) · requested by the user (the client's WhatsApp messages and video, 1:30–2:27 PM) · decision: D-157
**Where:** the main checkout, alongside trimme-0c (D-156, uncommitted, other files) and trimme-08 (its own worktree). Only this decision's paths are staged for its commit.

## The request (the client's messages, forwarded by the user)
- **«احجز» on a service tile is right:** it opens «اختر الخدمات» with that service chosen, to add more (images 1–2). No change.
- **1. The wizard's bar is halfway up the screen** (image 3, the service step with one package): «هذي مرتفعه عدل ارتفاعها و خلها تحت بالصفحه نفس ارتفاع القائمه و نفس الخاصيه ماتتغطا بصفحة سفاري». Put it at the bottom, level with the bottom menu and, like it, clear of Safari's bar. Image 6 shows the same on the time step.
- **2. The big «احجز الآن» brings back the last choice** (images 4–7): on «الأطفال» (حلاقة أطفال ٤٥) the big button opened the time step with «باقة شعر ولحية» at ٨٥ from a minute before («يسجل الخدمه و السعر على اخر خيار اخترته من المره الماضيه مهما كانت»).
- **3. The big «احجز الآن» skips the services:** «اول ما اضغط عليها يوديني على اختيار الوقت بدون ما اختار اي خدمه».

## What the code said
- **2 and 3 are one cause.** The shop page's «احجز الآن» was a bare link (`/shops/{slug}/book`). On a bare link the wizard restored the shop's draft: the tab's memory (D-125) or the `gh-booking-draft` cookie (D-148, 4-7). It then went to the first step still missing a choice. A package with no time gives the day-and-time step, at the package's price.
- **1.** `PublicShell`'s `<main>` is `flex-1` but not a flex container. The wizard's root and its step section are `flex-1`, but they never stretch, so the sticky bar sits right under a short step. The customer's menu (`CustomerNav`) is `fixed` with the safe-area padding, and D-152 made it clear of Safari's bar.
- **The client is not in Safari.** The bottom bar with «Ask», home and the tab count, and the ✕ at the top, look like the Google app's in-app browser. Its web view ends above its own toolbar. The fix holds for Safari too (D-152's `viewport-fit=cover`, `min-h-dvh` and safe-area padding).

## Defaults (decided, not asked)
- **Every «احجز» button starts a new booking.** The shop page's «احجز الآن» sends `step=service`, the shop's QR landing and the favourites' «احجز» do the same, a tile's «احجز» names its service, and a barber's link names the barber. None of these restores the draft; each replaces it. Only a link with no query at all (typed, or an old shared link) picks the draft up, and then on the service step with its choices ticked, never further. This narrows D-148's 4-7 and D-125's "back to the shop page and in again keeps the choices", as the client now asks.
- **The tab the shop page was showing opens on the service step** (`section`). It only opens the tab; nothing is chosen, because opening «الأطفال» is not choosing «حلاقة أطفال». A tab the wizard lacks (only walk-in services) opens the first.
- **«احجز الآن» on the shop page is a plain GET form** (`id="shop-book"`, a hidden `step=service`). The catalog's radios join it with `form="shop-book"`, `name="section"` and the tab's key as `value`. It adds no script to the public page (its budget is tight), and the CSP already allows `form-action 'self'`. On the shop page's other tabs (specialists, reviews, about) there are no radios, so it sends `step` alone. It is now a button, not a link.
- **The bar stays `sticky`, in a column that fills the screen** (`PublicShell fill` makes `<main>` a flex column for the wizard only). A short step pushes the bar to the bottom of the screen. A long one keeps it stuck to the bottom. `fixed` was not used: it would need padding for the bar's height plus the safe area, and the times block and the note could slide under it. Other public pages are untouched: in a flex column, a `mx-auto max-w-*` child without `w-full` would shrink.
- **The height stays** (the 46 px button and two lines of text). «نفس ارتفاع القائمه» is read as its level, at the bottom.

## Not changed (to raise with the user)
- The shop page's own «احجز الآن» bar sits above the site footer at the end of the page (images 1, 4 and 5). That is a sticky bar reaching its place in the flow; the client did not mark it.
- The customer's «تغيير الموعد» page (`RescheduleForm`) has the same sticky bar inside `CustomerShell`, so a short step may leave it above the menu rather than on it. Not reported yet.
- The real phone check: Playwright draws no browser toolbar, so the bar against the Google app's or Safari's toolbar waits on the client's phone, like D-152's 1-1.

## Checklist
- [x] `wizard.ts`: `section` in the query
- [x] `BookingWizard`: only a bare link restores the draft, on the service step; every other link replaces it; `section` opens its tab
- [x] `ServiceCatalog`: `opened` when nothing is chosen
- [x] Shop page: «احجز الآن» as a GET form with `step=service`; the catalog's radios carry `section`
- [x] QR landing and favourites: `step=service`
- [x] `PublicShell fill` on the booking page; the bar at the bottom of the screen
- [x] Unit tests: the bare-link draft on the service step; «احجز الآن» on «الأطفال» with an old package (nothing chosen, the tab open, the draft gone, then ٤٥ alone); an unknown tab; a tile's «احجز» with another service in the draft
- [x] E2E `qr.spec.ts`: the landing's link has `?step=service`
- [x] E2E `booking-start.spec.ts` (new): the client's video at a phone's size, the bar's bottom at the screen's bottom on three steps
- [x] E2E `customer-booking.spec.ts`: the favourites' «احجز» has `?step=service`
- [x] Gates: format, lint, typecheck, unit, build and bundle budget, E2E
- [ ] The client's phone after a deploy (the bar against the browser's toolbar)

## Evidence (Session 32, main checkout, on top of D-156's `a5137d7`)
- **Web unit:** `vitest run`: 64 files, **603 passed**. D-157's three tests:
  - a bare link's draft on the service step;
  - the client's video (an old package with a time, then «احجز الآن» on «الأطفال»: the service step, «الأطفال» open, nothing chosen, no price, «التالي» disabled, the draft cookie gone; then ٤٥ alone), and an unknown tab opening the first;
  - a tile's «احجز» with another service in the draft.
- **Static:** `tsc --noEmit` exit 0; `pnpm lint` (eslint, 0 warnings) exit 0; `pnpm format:check` clean.
- **Build and bundle budget:** `.next` built at 14:27 from the shared tree with these changes; its server chunks contain the `shop-book` form. `node scripts/bundle-budget.mjs` exit 0: shop page 219.2/220 KB, booking 237.7/260 KB. trimme-0c ran that build; it was not rerun here, to keep out of the shared `.next`.
- **E2E** on the local web container rebuilt from the tree at 14:29 (`curl` showed the `shop-book` form and the `section` radios), `--project=flows --workers=1`:
  - `booking-start.spec` **1/1**. The first run failed only on `.check()` of the tile's hidden checkbox; the tile's label is now clicked, as the other specs do;
  - `qr.spec` **3/3**;
  - `customer-booking.spec` **5/5**. The favourites test failed on the old `book$` link; the assertion was updated to `?step=service`;
  - `public-discovery.spec` **8/8**, `signed-in-gate.spec` **2/2**, `whatsapp-notifications.spec` **1/1**.
- **Audits:** `audit/routes.spec.ts` (anonymous, ar and en, `a11y` and `a11y-dark`) and `audit/keyboard.spec.ts`: **9/9**.
- **Screens** (Chromium 390×844, dark): the service step after «احجز الآن» on «الأطفال», with the tab open and the bar at the bottom; after choosing, «حلاقة أطفال ٤٠» in the bar.
