import { describe, expect, it } from 'vitest';
import {
  colorScheme,
  DEFAULT_THEME,
  parseTheme,
  THEME_COOKIE_MAX_AGE,
  themeAttribute,
  themeCookie,
  themeFromCookieString,
} from './theme';

describe('theme preference (D-124)', () => {
  it('defaults to System when nothing (or something unknown) is saved', () => {
    expect(DEFAULT_THEME).toBe('system');
    expect(parseTheme(undefined)).toBe('system');
    expect(parseTheme(null)).toBe('system');
    expect(parseTheme('')).toBe('system');
    expect(parseTheme('sepia')).toBe('system');
    expect(parseTheme('DARK')).toBe('system');
  });

  it('accepts the three choices', () => {
    expect(parseTheme('light')).toBe('light');
    expect(parseTheme('dark')).toBe('dark');
    expect(parseTheme('system')).toBe('system');
  });

  it('puts an attribute on <html> only for an explicit choice, so System follows the OS through CSS', () => {
    expect(themeAttribute('system')).toBeUndefined();
    expect(themeAttribute('light')).toBe('light');
    expect(themeAttribute('dark')).toBe('dark');
  });

  it('declares the matching color-scheme', () => {
    expect(colorScheme('system')).toBe('light dark');
    expect(colorScheme('light')).toBe('light');
    expect(colorScheme('dark')).toBe('dark');
  });

  it('persists for a year on the whole site, Secure only over HTTPS', () => {
    expect(themeCookie('dark', false)).toBe(
      `trimme-theme=dark; Path=/; Max-Age=${THEME_COOKIE_MAX_AGE}; SameSite=Lax`,
    );
    expect(themeCookie('light', true)).toMatch(/; Secure$/);
    expect(THEME_COOKIE_MAX_AGE).toBe(31_536_000);
  });

  it('reads the preference back from document.cookie among other cookies', () => {
    expect(themeFromCookieString('a=1; trimme-theme=dark; b=2')).toBe('dark');
    expect(themeFromCookieString('trimme-theme=light')).toBe('light');
    expect(themeFromCookieString('x-trimme-theme=dark')).toBe('system');
    expect(themeFromCookieString('')).toBe('system');
  });
});
