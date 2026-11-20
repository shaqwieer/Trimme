import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { customerSignUp } from '../audit/inventory';
import { expect, test } from '../support/fixtures';

/**
 * Dark mode (D-124): Light / Dark / System with System as the default, saved in a cookie the server renders from, so
 * the first paint is already in the right theme (no flash, even without JavaScript), and a change reaches the other
 * open tabs at once. Printing always uses the light palette. The header has a day/night switch (D-156): a sun or a
 * moon, one press flips what is shown; System is chosen in the account settings.
 */
const LIGHT_PAGE = 'rgb(247, 249, 252)'; // --color-bg-page
const DARK_PAGE = 'rgb(12, 22, 32)'; // dark --color-bg-page

const pageBackground = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.documentElement).backgroundColor);
/** Polled: a media emulation or a theme change reaches computed style a frame later. */
const expectBackground = (page: Page, color: string) => expect.poll(() => pageBackground(page)).toBe(color);
const themeAttribute = (page: Page) =>
  page.evaluate(() => document.documentElement.getAttribute('data-theme'));
const themeButton = (page: Page) => page.getByTestId('theme-toggle');
/** The header switch draws a sun by day and a moon by night (D-156); CSS shows one of the two. */
const expectIcon = async (page: Page, icon: 'sun' | 'moon') => {
  await expect(themeButton(page).locator(`[data-icon="${icon}"]`)).toBeVisible();
  await expect(themeButton(page).locator(`[data-icon="${icon === 'sun' ? 'moon' : 'sun'}"]`)).toBeHidden();
};

test.describe('dark mode (D-124) and the day/night switch (D-156)', () => {
  test('defaults to System and follows the OS live, without a reload, the sun and moon included', async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/en');
    await expect(themeButton(page)).toHaveAccessibleName('Switch between light and dark mode');
    expect(await themeAttribute(page)).toBeNull();
    await expectBackground(page, LIGHT_PAGE);
    await expectIcon(page, 'sun');

    await page.emulateMedia({ colorScheme: 'dark' });
    await expectBackground(page, DARK_PAGE);
    await expectIcon(page, 'moon');
    await page.emulateMedia({ colorScheme: 'light' });
    await expectBackground(page, LIGHT_PAGE);
  });

  test('a press flips what is shown at once, survives navigation and reload, and overrides the OS', async ({
    page,
    context,
  }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/en');
    await themeButton(page).click();
    expect(await themeAttribute(page)).toBe('dark');
    await expectBackground(page, DARK_PAGE);
    await expectIcon(page, 'moon');

    const cookie = (await context.cookies()).find((c) => c.name === 'trimme-theme');
    expect(cookie?.value).toBe('dark');
    // About a year: the choice survives a browser restart.
    expect(cookie!.expires - Date.now() / 1000).toBeGreaterThan(360 * 24 * 3600);

    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('link', { name: 'Salons' })
      .click();
    await page.waitForURL('**/en/shops');
    await expectBackground(page, DARK_PAGE);
    await page.reload();
    expect(await themeAttribute(page)).toBe('dark');

    // Explicit Light beats a dark OS.
    await page.emulateMedia({ colorScheme: 'dark' });
    await themeButton(page).click();
    await expectBackground(page, LIGHT_PAGE);
    await page.goto('/ar');
    await expectBackground(page, LIGHT_PAGE);
    await expectIcon(page, 'sun');
    await expect(themeButton(page)).toHaveAccessibleName('تبديل الوضع النهاري والليلي');
  });

  test('under System on a dark device, a press goes to Light', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto('/ar/shops');
    await expectIcon(page, 'moon');
    await themeButton(page).click();
    expect(await themeAttribute(page)).toBe('light');
    await expectBackground(page, LIGHT_PAGE);
    await expectIcon(page, 'sun');
  });

  test('the first paint is already themed: the server renders the saved theme (JavaScript disabled)', async ({
    browser,
    baseURL,
  }) => {
    const context = await browser.newContext({ javaScriptEnabled: false, colorScheme: 'light' });
    await context.addCookies([{ name: 'trimme-theme', value: 'dark', url: baseURL! }]);
    const page = await context.newPage();
    await page.goto('/ar/shops');
    expect(await themeAttribute(page)).toBe('dark');
    await expectBackground(page, DARK_PAGE);
    expect(await page.locator('meta[name="color-scheme"]').getAttribute('content')).toBe('dark');
    await context.close();

    // System on a dark OS needs no script either: the stylesheet follows prefers-color-scheme.
    const system = await browser.newContext({ javaScriptEnabled: false, colorScheme: 'dark' });
    const systemPage = await system.newPage();
    await systemPage.goto('/en/auth/sign-in');
    await expectBackground(systemPage, DARK_PAGE);
    await expectIcon(systemPage, 'moon');
    await system.close();
  });

  test('a change in one tab reaches the other open tabs at once', async ({ context }) => {
    const first = await context.newPage();
    const second = await context.newPage();
    await first.emulateMedia({ colorScheme: 'light' });
    await second.emulateMedia({ colorScheme: 'light' });
    await first.goto('/en');
    await second.goto('/en/shops');

    await themeButton(first).click();
    await expect.poll(() => themeAttribute(second)).toBe('dark');
    await expectIcon(second, 'moon');

    await themeButton(second).click();
    await expect.poll(() => themeAttribute(first)).toBe('light');
    await expectBackground(first, LIGHT_PAGE);
  });

  test('printing stays light whatever the screen theme', async ({ page, context, baseURL }) => {
    await context.addCookies([{ name: 'trimme-theme', value: 'dark', url: baseURL! }]);
    await page.goto('/en/terms');
    await expectBackground(page, DARK_PAGE);
    await page.emulateMedia({ media: 'print' });
    await expectBackground(page, LIGHT_PAGE);
    // Native controls and the browser's own colours follow color-scheme, which must be light on paper too.
    await expect
      .poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme))
      .toBe('light');
  });

  test('works from the keyboard: a native button, Enter and Space flip it', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/en/auth/sign-in');
    await themeButton(page).focus();
    await page.keyboard.press('Enter');
    expect(await themeAttribute(page)).toBe('dark');
    await page.keyboard.press('Space');
    expect(await themeAttribute(page)).toBe('light');
    await expect(themeButton(page)).toBeFocused();
    expect(
      (await new AxeBuilder({ page }).include('[data-testid="theme-toggle"]').analyze()).violations,
    ).toEqual([]);
  });

  test('the customer account has a labelled Appearance setting', async ({ page, context }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/ar/terms');
    await customerSignUp(context, page.request);
    await page.goto('/en/account');
    const group = page.getByRole('group', { name: 'Theme' });
    await expect(group.getByRole('radio', { name: 'System' })).toBeChecked();
    await group.getByText('Dark', { exact: true }).click();
    await expectBackground(page, DARK_PAGE);
    await expectIcon(page, 'moon');
    await page.reload();
    await expect(
      page.getByRole('group', { name: 'Theme' }).getByRole('radio', { name: 'Dark' }),
    ).toBeChecked();
  });
});
