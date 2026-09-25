import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { captureViewports } from '../support/viewports';

/**
 * Layout shells (Phase 02) through the development-only preview routes. The stack must run the web app
 * with TRIMME_ENABLE_DEV_ROUTES=true (the default for the local docker compose stack).
 */
test.describe('layout shells', () => {
  test('dashboard: fixed sidebar on desktop, drawer below 1200px (RTL)', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/ar/dev/shells/shop');
    const sidebar = page.locator('aside');
    await expect(sidebar).toBeVisible();
    await expect(page.getByRole('button', { name: 'فتح القائمة' })).toBeHidden();

    // In RTL the sidebar sits on the right edge.
    const box = await sidebar.boundingBox();
    expect(box && Math.round(box.x + box.width)).toBe(1440);

    await page.setViewportSize({ width: 768, height: 1024 });
    await expect(sidebar).toBeHidden();
    await page.getByRole('button', { name: 'فتح القائمة' }).click();
    const drawer = page.getByRole('dialog', { name: 'التنقل الرئيسي' });
    await expect(drawer).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
  });

  test('dashboard sidebar sits on the left in English (LTR)', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/en/dev/shells/admin');
    const box = await page.locator('aside').boundingBox();
    expect(box?.x).toBe(0);
  });

  test('customer: bottom bar on phones, header navigation on desktop', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/ar/dev/shells/customer');
    const navs = page.getByRole('navigation', { name: 'التنقل الرئيسي' });
    await expect(navs.filter({ visible: true })).toHaveCount(1);
    await expect(navs.filter({ visible: true }).getByRole('link')).toHaveCount(5);

    await page.setViewportSize({ width: 1440, height: 900 });
    await expect(navs.filter({ visible: true })).toHaveCount(1);
    const box = await navs.filter({ visible: true }).boundingBox();
    expect(box && box.y).toBeLessThan(80); // header, not bottom bar
  });

  test('bidi: phone numbers stay left-to-right inside Arabic text', async ({ page }) => {
    await page.goto('/ar/dev/shells/customer');
    const phone = page.locator('bdi').first();
    await expect(phone).toHaveText('+966 51 234 5678');
    await expect(phone).toHaveCSS('direction', 'ltr');
  });

  for (const variant of ['customer', 'shop', 'admin'] as const) {
    test(`no serious axe violations in the ${variant} shell`, async ({ page }) => {
      await page.goto(`/ar/dev/shells/${variant}`);
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
      expect(blocking.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
    });
  }

  test('captures shells at 390 / 768 / 1440 for design comparison', async ({ page }, testInfo) => {
    for (const [name, path] of [
      ['customer-ar', '/ar/dev/shells/customer'],
      ['shop-ar', '/ar/dev/shells/shop'],
      ['admin-en', '/en/dev/shells/admin'],
    ] as const) {
      await captureViewports(page, testInfo, name, path);
    }
    expect(testInfo.attachments.length).toBe(9);
  });
});
