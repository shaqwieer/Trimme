import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { captureViewports } from '../support/viewports';

/**
 * Design-system gallery (Phase 03, dev-only route). Real-browser checks that jsdom cannot do:
 * colour contrast (axe), RTL keyboard behaviour of Radix primitives, and layout at 3 widths.
 */
test.describe('component gallery', () => {
  for (const locale of ['ar', 'en'] as const) {
    test(`no serious or critical axe violations (incl. contrast) in ${locale}`, async ({ page }) => {
      await page.goto(`/${locale}/dev/components`);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
      expect(
        blocking.map((v) => `${v.id}: ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
      ).toEqual([]);
    });
  }

  for (const locale of ['ar', 'en'] as const) {
    test(`server-safe components render from a Server Component in ${locale} (D-048)`, async ({ page }) => {
      // This route has no 'use client'; a component attaching its own handler would fail to render.
      const response = await page.goto(`/${locale}/dev/components/server`);
      expect(response?.status()).toBe(200);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(
        locale === 'ar' ? 'مكونات الخادم' : 'Server components',
      );
      await expect(page.getByRole('table', { name: locale === 'ar' ? 'المحلات' : 'Shops' })).toBeAttached();
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
      expect(
        blocking.map((v) => `${v.id}: ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
      ).toEqual([]);
    });
  }

  test('tabs follow the reading direction with the keyboard (RTL)', async ({ page }) => {
    await page.goto('/ar/dev/components');
    const tablist = page.getByRole('tablist', { name: 'أقسام المحل' });
    await tablist.getByRole('tab', { name: 'الخدمات' }).focus();
    await page.keyboard.press('ArrowLeft');
    await expect(tablist.getByRole('tab', { name: 'الحلاقون' })).toBeFocused();
  });

  test('dialog traps focus and returns it to the trigger', async ({ page }) => {
    await page.goto('/ar/dev/components');
    const trigger = page.getByRole('button', { name: 'نافذة' });
    await trigger.click();
    const dialog = page.getByRole('dialog', { name: 'تفاصيل الموعد' });
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(dialog.locator(':focus')).toHaveCount(1);
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test('phone field stays left-to-right and emits grouped digits from Arabic-Indic input', async ({
    page,
  }) => {
    await page.goto('/ar/dev/components');
    const phone = page.getByLabel('رقم الجوال');
    await phone.fill('٠٥١٢٣٤٥٦٧٨');
    await expect(phone).toHaveValue('51 234 5678');
    await expect(phone).toHaveCSS('direction', 'ltr');
  });

  test('responsive table becomes cards on phones', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/ar/dev/components');
    await expect(page.getByRole('table', { name: 'المحلات' })).toBeHidden();
    await expect(page.getByRole('list', { name: 'المحلات' })).toBeVisible();
    await page.setViewportSize({ width: 1440, height: 900 });
    await expect(page.getByRole('table', { name: 'المحلات' })).toBeVisible();
  });

  test('no horizontal page overflow at any reference width (spec §5)', async ({ page }) => {
    test.slow(); // 24 page loads (6 paths × 4 widths)
    const paths = [
      '/ar/dev/components/server',
      '/ar',
      '/en',
      '/ar/dev/components',
      '/en/dev/components',
      '/ar/dev/shells/customer',
      '/ar/dev/shells/shop',
    ];
    const failures: string[] = [];
    for (const width of [390, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      for (const path of paths) {
        await page.goto(path, { waitUntil: 'load' });
        // Measure only after web fonts are applied (fallback-font text can be temporarily wider).
        const overflow = await page.evaluate(async () => {
          await document.fonts.ready;
          return document.documentElement.scrollWidth - document.documentElement.clientWidth;
        });
        if (overflow > 0) failures.push(`${path} @${width}px overflows by ${overflow}px`);
      }
    }
    expect(failures).toEqual([]);
  });

  test('captures the gallery at 390 / 768 / 1440 in RTL and LTR', async ({ page }, testInfo) => {
    test.slow(); // six full-page screenshots of a long page
    await captureViewports(page, testInfo, 'gallery-ar', '/ar/dev/components');
    await captureViewports(page, testInfo, 'gallery-en', '/en/dev/components');
    expect(testInfo.attachments.length).toBe(6);
  });
});
