import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { customerSignUp } from '../audit/inventory';
import { expect, test } from '../support/fixtures';

/**
 * Dark mode (D-124): Light / Dark / System with System as the default, saved in a cookie the server renders from, so
 * the first paint is already in the right theme (no flash, even without JavaScript), and a change reaches the other
 * open tabs at once. Printing always uses the light palette.
 */
const LIGHT_PAGE = 'rgb(247, 249, 252)'; // --color-bg-page
const DARK_PAGE = 'rgb(12, 22, 32)'; // dark --color-bg-page

const pageBackground = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.documentElement).backgroundColor);
/** Polled: a media emulation or a theme change reaches computed style a frame later. */
const expectBackground = (page: Page, color: string) => expect.poll(() => pageBackground(page)).toBe(color);
const themeAttribute = (page: Page) =>
  page.evaluate(() => document.documentElement.getAttribute('data-theme'));
const themeButton = (page: Page) => page.getByTestId('theme-menu');

async function choose(page: Page, option: 'System' | 'Light' | 'Dark') {
  await themeButton(page).click();
  await page.getByRole('radiogroup', { name: 'Theme' }).getByText(option, { exact: true }).click();
}

test.describe('dark mode (D-124)', () => {
  test('defaults to System and follows the OS live, without a reload', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/en');
    await expect(themeButton(page)).toHaveAccessibleName('Theme: System');
    expect(await themeAttribute(page)).toBeNull();
    await expectBackground(page, LIGHT_PAGE);

    await page.emulateMedia({ colorScheme: 'dark' });
    await expectBackground(page, DARK_PAGE);
    await page.emulateMedia({ colorScheme: 'light' });
    await expectBackground(page, LIGHT_PAGE);
  });

  test('a choice applies at once, survives navigation and reload, and overrides the OS', async ({
    page,
    context,
  }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/en');
    await choose(page, 'Dark');
    expect(await themeAttribute(page)).toBe('dark');
    await expectBackground(page, DARK_PAGE);
    await expect(themeButton(page)).toHaveAccessibleName('Theme: Dark');

    const cookie = (await context.cookies()).find((c) => c.name === 'trimme-theme');
    expect(cookie?.value).toBe('dark');
    // About a year: the choice survives a browser restart.
    expect(cookie!.expires - Date.now() / 1000).toBeGreaterThan(360 * 24 * 3600);

    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('link', { name: 'Shops' })
      .click();
    await page.waitForURL('**/en/shops');
    await expectBackground(page, DARK_PAGE);
    await page.reload();
    expect(await themeAttribute(page)).toBe('dark');

    // Explicit Light beats a dark OS.
    await page.emulateMedia({ colorScheme: 'dark' });
    await choose(page, 'Light');
    await expectBackground(page, LIGHT_PAGE);
    await page.goto('/ar');
    await expectBackground(page, LIGHT_PAGE);
    await expect(themeButton(page)).toHaveAccessibleName('المظهر: فاتح');
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
    await system.close();
  });

  test('a change in one tab reaches the other open tabs at once', async ({ context }) => {
    const first = await context.newPage();
    const second = await context.newPage();
    await first.emulateMedia({ colorScheme: 'light' });
    await second.emulateMedia({ colorScheme: 'light' });
    await first.goto('/en');
    await second.goto('/en/shops');

    await choose(first, 'Dark');
    await expect.poll(() => themeAttribute(second)).toBe('dark');
    await expect(themeButton(second)).toHaveAccessibleName('Theme: Dark');

    await choose(second, 'System');
    await expect.poll(() => themeAttribute(first)).toBeNull();
    await expectBackground(first, LIGHT_PAGE);
  });

  test('printing stays light whatever the screen theme', async ({ page, context, baseURL }) => {
    await context.addCookies([{ name: 'trimme-theme', value: 'dark', url: baseURL! }]);
    await page.goto('/en/terms');
    await expectBackground(page, DARK_PAGE);
    await page.emulateMedia({ media: 'print' });
    await expectBackground(page, LIGHT_PAGE);
  });

  test('works from the keyboard: Enter opens, arrows preview, Escape closes back on the button', async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/en/auth/sign-in');
    await themeButton(page).focus();
    await page.keyboard.press('Enter');
    const group = page.getByRole('radiogroup', { name: 'Theme' });
    await expect(group).toBeVisible();
    await expect(group.getByRole('radio', { name: 'System' })).toBeFocused();
    expect((await new AxeBuilder({ page }).include('[role="radiogroup"]').analyze()).violations).toEqual([]);

    await page.keyboard.press('ArrowDown'); // Light
    await page.keyboard.press('ArrowDown'); // Dark
    expect(await themeAttribute(page)).toBe('dark');
    await page.keyboard.press('Escape');
    await expect(group).toBeHidden();
    await expect(themeButton(page)).toBeFocused();
    await expect(themeButton(page)).toHaveAccessibleName('Theme: Dark');
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
    await expect(themeButton(page)).toHaveAccessibleName('Theme: Dark');
    await page.reload();
    await expect(
      page.getByRole('group', { name: 'Theme' }).getByRole('radio', { name: 'Dark' }),
    ).toBeChecked();
  });
});
