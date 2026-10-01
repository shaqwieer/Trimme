# Theming (light and dark)

Decision: D-124. Tokens: `apps/web/src/styles/tokens.css`. Contrast guard: `apps/web/src/styles/tokens.test.ts`.

## How it works
- The user picks **Light**, **Dark** or **System** (the default) from the header theme button on every shell, or from Appearance on `/account`.
- The choice is saved in the `trimme-theme` cookie for a year. The root layout reads it and renders `data-theme="light|dark"` on `<html>` for an explicit choice. System renders no attribute, and the CSS follows `prefers-color-scheme`.
- There is no inline script and nothing to hydrate: the first paint is right even with JavaScript off.
- Open tabs follow a change through a `BroadcastChannel` (`components/theme/ThemeProvider.tsx`).
- The dark palette is one block under `@custom-variant dark` in `tokens.css`. It is screen-only, so printing is always light.

## Rules for new UI
1. **Use tokens, never literals.** The lint rule already rejects hex colours in components. Do not use `bg-white` or `text-black` either; `bg-white` is reserved for the QR panels, which must stay white to scan.
2. **Pick the token for its role, not its light-mode colour.**

   | Need | Use | Dark behaviour |
   |---|---|---|
   | Page, card and raised backgrounds | `bg-bg-page`, `bg-surface`, `bg-bg-subtle` / `bg-bg-muted` | Step up in lightness |
   | Body, strong and secondary text | `text-text-primary`, `text-text-strong`, `text-text-secondary` | Light text |
   | Headings, primary buttons, selected chips (ink) | `text-navy-900`, `bg-navy-900 text-on-navy`, `border-navy-900` | Inverts: a light fill with dark text |
   | A brand navy panel that must stay navy (sidebar, toast, poster, anything on a photo or a map) | `bg-chrome text-on-chrome` (`-muted`, `-accent`, `-subtle`) | Stays navy |
   | Status | `status-*-bg` / `-fg` / `-dot`, `success|warning|danger-*` | Dark tints, light text |
   | Borders | `border-border`, `border-border-input`, `border-border-strong`, `border-border-row` | Dark lines |
   | Shadows | `shadow-e1…e3` and the rest | Deepen automatically (`--elevation-*`) |

3. **Rare one-offs** can use the `dark:` variant, which covers both explicit Dark and System on a dark OS, for example `dark:bg-on-chrome-accent` on the subscription card's progress bar.
4. **Do not invert images.** Photos, map tiles and QR codes keep their colours.
5. **New tokens** need a dark value in the dark block. `tokens.test.ts` fails when a light colour token has none, and checks the contrast of text and status tokens.

## Checks
- Unit: `pnpm --filter @trimme/web test` (tokens, theme module, selector).
- E2E: `flows/theme.spec.ts`. The `a11y-dark` project runs the full route audit (axe colour contrast included) with the OS in dark mode.
