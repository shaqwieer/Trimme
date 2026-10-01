import type { Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';
import { ADMIN, staffSignIn } from './inventory';

/**
 * Phase 17 keyboard pass (17.4, R-WEB-09): the key flows by keyboard alone. Every focus stop shows a visible
 * indicator, focus never gets stuck, the skip link reaches the content, dialogs take focus, keep it, close on Escape and
 * give it back. Reduced motion and touch-target sizes are checked here too. A screen-reader pass with NVDA is a manual
 * check (docs/accessibility.md).
 */

type Stop = { key: string; visible: boolean; size: [number, number] };

/** Where focus is now: a stable key for the element and whether it shows a focus indicator. */
async function focused(page: Page): Promise<Stop | null> {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el || el === document.body) return null;
    const style = getComputedStyle(el);
    const outline = style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
    const ring = style.boxShadow !== 'none';
    const box = el.getBoundingClientRect();
    const index = [...document.querySelectorAll('*')].indexOf(el);
    const key = `${el.tagName.toLowerCase()}#${el.id}|${el.getAttribute('href') ?? ''}|${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 40)}|${index}`;
    return { key, visible: outline || ring, size: [Math.round(box.width), Math.round(box.height)] };
  });
}

/** Tabs through the page: returns the stops in order until focus comes back to the first one (or `max`). */
async function tabThrough(page: Page, max = 120): Promise<Stop[]> {
  const stops: Stop[] = [];
  for (let i = 0; i < max; i++) {
    await page.keyboard.press('Tab');
    const stop = await focused(page);
    if (!stop) continue;
    if (stops.length > 0 && stop.key === stops[0]!.key) break;
    stops.push(stop);
  }
  return stops;
}

test.describe('keyboard pass (Phase 17)', () => {
  test('public pages: the skip link reaches the content, every stop shows focus, and Tab never gets stuck', async ({
    page,
  }) => {
    for (const path of ['/ar', '/en', '/ar/shops/barber-house', '/ar/auth/sign-in']) {
      await page.goto(path);
      // A form that focuses its first field on load (sign-in) starts there; elsewhere the first stop is the skip link.
      const autofocused = await page.evaluate(
        () => document.activeElement?.matches('input, textarea') ?? false,
      );
      if (!autofocused) {
        await page.keyboard.press('Tab');
        const skip = await focused(page);
        expect(skip?.key, `${path}: the first stop is the skip link`).toMatch(/^a#\|#main\|/);
        await page.keyboard.press('Enter');
        await expect
          .poll(() => page.evaluate(() => document.activeElement?.id), { message: path })
          .toBe('main');
      }

      await page.goto(path);
      const stops = await tabThrough(page);
      expect(stops.length, `${path}: focus moves through the page`).toBeGreaterThan(3);
      expect(
        stops.filter((s) => !s.visible).map((s) => s.key),
        `${path}: stops without a focus indicator`,
      ).toEqual([]);
      const repeated = stops.filter((s, i) => stops.findIndex((o) => o.key === s.key) !== i);
      expect(
        repeated.map((s) => s.key),
        `${path}: a stop reached twice before the cycle ends is a trap`,
      ).toEqual([]);
    }
  });

  test('staff sign in with the keyboard alone, and the dashboard navigation is reachable', async ({
    page,
  }) => {
    await page.goto('/en/auth/staff/sign-in');
    await page.getByLabel('Email').focus();
    await page.keyboard.type(ADMIN.email);
    await page.keyboard.press('Tab');
    await page.keyboard.type(ADMIN.password);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/en\/admin$/);

    const stops = await tabThrough(page, 60);
    expect(stops.some((s) => s.key.includes('|/en/admin/shops|'))).toBe(true);
    expect(stops.filter((s) => !s.visible).map((s) => s.key)).toEqual([]);
  });

  test('a dialog takes focus, keeps it, closes on Escape and gives focus back', async ({ page, context }) => {
    await page.goto('/en/terms');
    await staffSignIn(context, page.request, ADMIN.email, ADMIN.password);
    await page.goto('/en/admin/qr');
    const trigger = page.getByRole('button', { name: 'New code' });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect.poll(() => dialog.evaluate((d) => d.contains(document.activeElement))).toBe(true);

    for (let i = 0; i < 15; i++) {
      await page.keyboard.press('Tab');
      expect(
        await dialog.evaluate((d) => d.contains(document.activeElement)),
        `Tab ${i + 1} stays in the dialog`,
      ).toBe(true);
    }
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test('reduced motion turns transitions off; otherwise they run', async ({ browser }) => {
    for (const motion of ['reduce', 'no-preference'] as const) {
      const context = await browser.newContext({ reducedMotion: motion });
      const page = await context.newPage();
      await page.goto('/ar/shops');
      const duration = await page
        .locator('a, button')
        .evaluateAll((els) =>
          Math.max(...els.map((el) => parseFloat(getComputedStyle(el).transitionDuration) || 0)),
        );
      if (motion === 'reduce') expect(duration).toBeLessThan(0.001);
      else expect(duration).toBeGreaterThan(0.05);
      await context.close();
    }
  });

  test('touch targets on a phone: buttons and controls are at least 44 px, links in running text excepted', async ({
    browser,
  }, testInfo) => {
    // A touch screen, so `pointer: coarse` styles apply (compact controls grow to 44 px there).
    const context = await browser.newContext({
      viewport: { width: 390, height: 844 },
      hasTouch: true,
      isMobile: true,
    });
    const page = await context.newPage();
    const small: string[] = [];
    for (const path of [
      '/ar',
      '/ar/shops',
      '/ar/shops/barber-house',
      '/ar/shops/barber-house/book',
      '/ar/auth/sign-in',
    ]) {
      await page.goto(path);
      const found = await page.evaluate(() =>
        [
          ...document.querySelectorAll<HTMLElement>(
            'button, [role="button"], input:not([type="hidden"]), select, a[href]',
          ),
        ]
          .filter((el) => {
            const box = el.getBoundingClientRect();
            if (box.width === 0 || box.height === 0 || getComputedStyle(el).visibility === 'hidden')
              return false;
            // WCAG 2.5.8 exempts links inside a sentence; they take the line height.
            if (el.tagName === 'A' && el.closest('p, li > span, footer')) return false;
            // A stretched link: its ::after covers the card, which is the real target.
            const stretched = getComputedStyle(el, '::after').position === 'absolute' && el.offsetParent;
            const control = stretched
              ? (el.offsetParent as HTMLElement)
              : el.matches('input[type="checkbox"], input[type="radio"]')
                ? (el.closest('label') ?? el)
                : el;
            const target = control.getBoundingClientRect();
            return Math.min(target.width, target.height) < 43.5; // sub-pixel layouts round 44 down
          })
          .map((el) => {
            const box = el.getBoundingClientRect();
            return `${el.tagName.toLowerCase()} "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 30)}" ${Math.round(box.width)}×${Math.round(box.height)}`;
          }),
      );
      small.push(...found.map((f) => `${path}: ${f}`));
    }
    await context.close();
    await testInfo.attach('targets under 44px', {
      body: small.join('\n') || 'none',
      contentType: 'text/plain',
    });
    expect(small).toEqual([]);
  });
});
