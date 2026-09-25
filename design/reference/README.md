# Design reference screenshots

Captured on 2026-09-25 (Phase 02, item 2.2) from the Claude Design prototype `TRIMME.dc.html`. The tool was headless Chromium (Playwright 1.63), with a 1440×900 viewport at device scale factor 1. Each image is JPEG q80 of the full scroll height of the prototype's `main` area.

| Folder | What it contains |
|---|---|
| `1440/<screen-id>.jpg` | All 33 prototype screens: `doc-*`, `ds-*`, `c-*`, `s-*`, `a-*`, `r-*`. The IDs match `design/analysis/*` and `MASTER_PLAN.md` §5. |

How to read these images:
- **The prototype is a fixed desktop canvas**, so there are no separate 390px or 768px captures. Its left navigation and header bar belong to the prototype and are not product UI.
- **Mobile (390px) designs** for customer screens are drawn *inside* the canvas as phone frames labelled e.g. "HOME · 390px". Content inside a phone frame scrolls within the frame, so it is clipped at the frame height. `design/analysis/02-customer-screens.md` has the full content.
- **Dashboards (shop/admin)** are drawn at desktop width with the 264px navy sidebar.
- **Tablet (768px) and breakpoint behaviour** appear only as rules and mini-mockups on `r-responsive.jpg`.
- The prototype has an "EN · LTR / AR · RTL" toggle. The captures are in the default **AR · RTL**.
- The design has deviations that are deliberately *not* implemented, such as the barber-transfer card on `a-pros.jpg` and global service prices on `a-services.jpg`. See `docs/design-deviations.md`.

To re-capture, use the `claude_design` MCP `render_preview` for a short-lived preview URL, then open it with Playwright. **Never commit or log the preview URL**, because it embeds an access token.
