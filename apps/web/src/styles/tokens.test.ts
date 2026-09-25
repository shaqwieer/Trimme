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

  it('segmented-control labels meet AA on the grey track (text-tertiary on bg-subtle)', () => {
    expect(contrast(token('color-text-tertiary'), token('color-bg-subtle'))).toBeGreaterThanOrEqual(4.5);
  });

  it('switch off-track meets 3:1 on white and on the page background', () => {
    expect(contrast(token('color-switch-off'), '#ffffff')).toBeGreaterThanOrEqual(3);
    expect(contrast(token('color-switch-off'), token('color-bg-page'))).toBeGreaterThanOrEqual(3);
  });

  it('keeps sidebar text readable on navy', () => {
    expect(contrast(token('color-on-navy-muted'), token('color-navy-900'))).toBeGreaterThanOrEqual(4.5);
  });
});
