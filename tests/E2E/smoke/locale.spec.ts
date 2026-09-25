import { expect, test } from '@playwright/test';

test.describe('locales (R-WEB-04)', () => {
  test('locale_ar_is_rtl', async ({ page }) => {
    await page.goto('/ar');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('احجز حلاقتك القادمة');
  });

  test('locale_en_is_ltr', async ({ page }) => {
    await page.goto('/en');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Book your next haircut');
  });

  // The browser locale drives the navigation Accept-Language header (extraHTTPHeaders do not override it).
  for (const browserLocale of ['fr-FR', 'ar-SA']) {
    test(`root redirects to Arabic for a ${browserLocale} browser`, async ({ browser }) => {
      const context = await browser.newContext({ locale: browserLocale });
      const page = await context.newPage();
      await page.goto('/');
      await expect(page).toHaveURL(/\/ar$/);
      await context.close();
    });
  }

  test('root honours an English Accept-Language', async ({ browser }) => {
    const context = await browser.newContext({ locale: 'en-US' });
    const page = await context.newPage();
    await page.goto('/');
    await expect(page).toHaveURL(/\/en$/);
    await context.close();
  });

  test('language switch keeps the page and flips direction', async ({ page }) => {
    await page.goto('/ar');
    await page.getByRole('link', { name: /English/ }).click();
    await expect(page).toHaveURL(/\/en$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  });

  test('unknown paths render the localized 404', async ({ page }) => {
    const response = await page.goto('/ar/this-page-does-not-exist');
    expect(response?.status()).toBe(404);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('الصفحة غير موجودة');
  });

  test('no_tokens_in_web_storage (R-NEG-07 baseline)', async ({ page }) => {
    await page.goto('/ar');
    const storage = await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }));
    expect(storage).toEqual({ local: 0, session: 0 });
  });
});
