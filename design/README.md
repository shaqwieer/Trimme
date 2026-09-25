# TRIMME design source

Imported from Claude Design project **واجهة تريمي التفاعلية**
(`https://claude.ai/design/p/af2d08aa-185e-42e6-be25-43d52be321f3?file=TRIMME.dc.html`) on 2026-09-25.

| Path | What it is |
|---|---|
| `source/TRIMME.dc.html` | The design prototype (single "dc" component: CSS + template + logic). **Visual source of truth — not production code.** 4656 lines, 441,593 bytes. Downloaded via the project preview server with the injected preview harness stripped; the byte count matches the project listing and the head matches `read_file`. |
| `source/support.js` | Generic Claude Design runtime (`dc-runtime`, parses `<x-dc>` and renders via React). Contains **no TRIMME product logic**; nothing from it is shipped. Prototype interactions live in the `data-dc-script` block of `TRIMME.dc.html`. |
| `source/trimme-logo.png` | Official logo (677×369 RGBA PNG). Copied from repo-root `logo.png`, which has the same byte size as the project file (54,177 B). The preview server re-encodes images, so its copy was not used. Never redraw, recolour, stretch or distort. |
| `analysis/01-design-system-and-docs.md` | Tokens, component library, icons, design docs screens, responsive/states/a11y review, conflicts. |
| `analysis/02-customer-screens.md` | Every customer screen → fields, interactions, proposed routes, API/DTOs, deviations. |
| `analysis/03-shop-admin-screens.md` | Every shop & admin screen → fields, KPIs, routes, API/DTOs, permissions, deviations. |

To re-sync from Claude Design: use the `claude_design` MCP (`list_files`, `read_file`) on the project above and diff against `source/`.
Design deviations adopted for implementation are recorded in `docs/design-deviations.md`.
