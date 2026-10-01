import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const css = readFileSync(join(process.cwd(), 'src/styles/tokens.css'), 'utf8');

function token(name: string): string {
  const match = new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{6})`).exec(css);
  if (!match?.[1]) {
    throw new Error(`Token --${name} not found`);
  }
  return match[1].toLowerCase();
}

/** The dark palette (D-124): the `@variant dark { … }` block of tokens.css. */
const darkBlock = (() => {
  const start = css.indexOf('@variant dark {');
  if (start < 0) throw new Error('Dark palette not found');
  return css.slice(start, css.indexOf('}', start));
})();

function dark(name: string): string {
  const match = new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{6})`).exec(darkBlock);
  if (!match?.[1]) {
    throw new Error(`Dark token --${name} not found`);
  }
  return match[1].toLowerCase();
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const channel = parseInt(hex.slice(i, i + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  }) as [number, number, number];
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

describe('tokens_match_design_snapshot (R-WEB-01)', () => {
  it('keeps the TRIMME identity colours from the design', () => {
    expect(token('color-brand-500')).toBe('#6d9bcb');
    expect(token('color-brand-700')).toBe('#2c5c8c');
    expect(token('color-navy-900')).toBe('#10283d');
    expect(token('color-bg-page')).toBe('#f7f9fc');
    expect(token('color-text-primary')).toBe('#17212b');
  });

  it('resets Tailwind defaults so only TRIMME tokens exist', () => {
    for (const namespace of ['color', 'radius', 'shadow', 'breakpoint', 'font', 'text']) {
      expect(css).toContain(`--${namespace}-*: initial;`);
    }
  });

  it('uses the design breakpoints (390 / 768 / 1200 / 1440)', () => {
    expect(css).toContain('--breakpoint-sm: 24.375rem;');
    expect(css).toContain('--breakpoint-md: 48rem;');
    expect(css).toContain('--breakpoint-lg: 75rem;');
    expect(css).toContain('--breakpoint-xl: 90rem;');
  });

  it.each([
    'color-text-primary',
    'color-text-strong',
    'color-text-secondary',
    'color-text-tertiary',
    'color-text-placeholder',
    'color-text-link',
    'color-brand-700',
    'color-navy-900',
    'color-success-700',
    'color-warning-700',
    'color-danger-700',
  ])('text token %s meets WCAG AA (4.5:1) on white and on the page background', (name) => {
    expect(contrast(token(name), '#ffffff')).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token(name), token('color-bg-page'))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['pending', 'confirmed', 'arrived', 'completed', 'cancelled', 'noshow'])(
    'status %s badge text meets AA on its background',
    (status) => {
      expect(
        contrast(token(`color-status-${status}-fg`), token(`color-status-${status}-bg`)),
      ).toBeGreaterThanOrEqual(4.5);
    },
  );

  it.each(['color-text-secondary', 'color-text-tertiary'])(
    'grey text %s meets AA on the grey surfaces (notes, tracks, hovered links)',
    (name) => {
      for (const surface of ['color-bg-muted', 'color-bg-subtle', 'color-bg-app']) {
        expect(contrast(token(name), token(surface)), surface).toBeGreaterThanOrEqual(4.5);
      }
    },
  );

  it('segmented-control labels meet AA on the grey track (text-tertiary on bg-subtle)', () => {
    expect(contrast(token('color-text-tertiary'), token('color-bg-subtle'))).toBeGreaterThanOrEqual(4.5);
  });

  it('switch off-track meets 3:1 on white and on the page background', () => {
    expect(contrast(token('color-switch-off'), '#ffffff')).toBeGreaterThanOrEqual(3);
    expect(contrast(token('color-switch-off'), token('color-bg-page'))).toBeGreaterThanOrEqual(3);
  });

  it('keeps sidebar text readable on navy chrome', () => {
    expect(contrast(token('color-on-chrome-muted'), token('color-chrome'))).toBeGreaterThanOrEqual(4.5);
    // Chrome is the brand navy itself in the light theme.
    expect(token('color-chrome')).toBe(token('color-navy-900'));
  });
});

describe('dark palette (D-124)', () => {
  const SURFACES = [
    'color-bg-page',
    'color-bg-app',
    'color-surface',
    'color-bg-subtle',
    'color-bg-muted',
    'color-bg-tile',
    'color-brand-50',
    'color-brand-100',
  ];

  it('defines both theme selectors from one source: explicit Dark and System on a dark OS, screen only', () => {
    expect(css).toContain("&:where([data-theme='dark'], [data-theme='dark'] *)");
    expect(css).toContain('@media screen and (prefers-color-scheme: dark)');
    expect(css).toContain("&:where(:not([data-theme='light'], [data-theme='light'] *))");
  });

  it('uses dark surfaces that step up in lightness (page → card → subtle) for separation', () => {
    const [page, surface, subtle] = ['color-bg-page', 'color-surface', 'color-bg-subtle'].map((n) =>
      luminance(dark(n)),
    ) as [number, number, number];
    expect(page).toBeLessThan(surface);
    expect(surface).toBeLessThan(subtle);
    expect(page).toBeLessThan(0.02);
  });

  it.each([
    'color-text-primary',
    'color-text-strong',
    'color-text-secondary',
    'color-text-tertiary',
    'color-text-placeholder',
    'color-text-link',
    'color-brand-700',
    'color-navy-900',
    'color-success-700',
    'color-warning-700',
    'color-danger-700',
  ])('text token %s meets AA (4.5:1) on every dark surface', (name) => {
    for (const surface of SURFACES) {
      expect(contrast(dark(name), dark(surface)), surface).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(['pending', 'confirmed', 'arrived', 'completed', 'cancelled', 'noshow'])(
    'status %s badge text meets AA on its dark background',
    (status) => {
      expect(
        contrast(dark(`color-status-${status}-fg`), dark(`color-status-${status}-bg`)),
      ).toBeGreaterThanOrEqual(4.5);
    },
  );

  it('ink fills (primary button, selected chips, danger) keep AA with on-navy text in every state', () => {
    for (const fill of [
      'color-navy-900',
      'color-navy-800',
      'color-navy-950',
      'color-danger-500',
      'color-danger-700',
      'color-text-primary', // tooltip
    ]) {
      expect(contrast(dark('color-on-navy'), dark(fill)), fill).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('chrome panels stay navy with readable text', () => {
    expect(dark('color-chrome')).toBe(token('color-chrome'));
    expect(contrast(token('color-on-chrome-muted'), dark('color-chrome'))).toBeGreaterThanOrEqual(4.5);
  });

  it('essential non-text UI meets 3:1 (switch track, focus ring, status dots, rating)', () => {
    for (const name of [
      'color-switch-off',
      'color-brand-500',
      'color-success-500',
      'color-warning-500',
      'color-danger-500',
      'color-rating',
    ]) {
      expect(contrast(dark(name), dark('color-surface')), name).toBeGreaterThanOrEqual(3);
      expect(contrast(dark(name), dark('color-bg-page')), name).toBeGreaterThanOrEqual(3);
    }
  });

  it('overrides every light colour token that has a hex value', () => {
    const light = [
      ...css.slice(0, css.indexOf('@variant dark {')).matchAll(/--(color-[a-z0-9-]+):\s*#/g),
    ].map((m) => m[1]!);
    const darkNames = new Set([...darkBlock.matchAll(/--(color-[a-z0-9-]+):/g)].map((m) => m[1]!));
    // Text on chrome is the same in both themes by design (chrome stays navy).
    const same = new Set(['color-on-chrome', 'color-on-chrome-muted', 'color-on-chrome-accent']);
    expect(light.filter((name) => !darkNames.has(name) && !same.has(name))).toEqual([]);
  });
});
