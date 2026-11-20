import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * D-157, the client's iPhone video (2026-11-20): on the shop page the visitor booked a package, came back, opened
 * «الأطفال» and pressed the big «احجز الآن». The wizard brought the package back and went straight to the time.
 * Now «احجز الآن» opens the service step with nothing chosen, on the tab the shop page showed. The wizard's bar sits at
 * the bottom of the screen, like the customer's menu, however short the step. Guest only, so nothing is booked.
 */
const SHOP = '/ar/shops/barber-house';
const PHONE = { width: 390, height: 844 };

/** The wizard's bottom bar (its note is always there). */
const wizardBar = (page: Page) => page.locator('footer', { has: page.getByTestId('wizard-footer-note') });

async function expectAtScreenBottom(bar: Locator) {
  const box = await bar.boundingBox();
  expect(box).not.toBeNull();
  expect(Math.round(box!.y + box!.height)).toBe(PHONE.height);
}

/** A tab of the shop page's catalog: a pill holding a native radio (D-148). */
const shopTab = (page: Page, name: string) =>
  page.getByRole('group', { name: 'الخدمات' }).locator('label').filter({ hasText: name });

test.use({ viewport: PHONE });

test('«احجز الآن» starts a new booking on the tab the shop page showed, and the bar sits at the bottom (D-157)', async ({
  page,
}) => {
  // The client's first booking: the package's own «احجز», on to the specialist step.
  await page.goto(SHOP);
  await shopTab(page, 'الباقات').click();
  await page.getByRole('link', { name: 'احجز باقة القص واللحية' }).click();
  await expect(page.getByTestId('wizard-title')).toHaveText('اختر الخدمات');
  await expect(page.getByRole('checkbox', { name: /باقة القص واللحية/ })).toBeChecked();
  await page.getByRole('button', { name: 'التالي' }).click();
  await expect(page.getByTestId('wizard-title')).toHaveText('اختر المختص');

  // Back to the shop page inside the app (the tab's memory and the draft cookie both hold the package).
  await page.getByRole('button', { name: 'الخطوة السابقة' }).click();
  await page.getByRole('link', { name: 'العودة إلى صفحة الصالون' }).click();
  await expect(page).toHaveURL(new RegExp(`${SHOP}$`));

  // «الأطفال», then the big «احجز الآن»: the service step on that tab, nothing chosen, not the time.
  await shopTab(page, 'الأطفال').click();
  await page.getByRole('button', { name: 'احجز الآن' }).click();
  await expect(page).toHaveURL(/\/book\?(?=.*section=)(?=.*step=service)/);
  await expect(page.getByTestId('wizard-title')).toHaveText('اختر الخدمات');
  await expect(page.getByRole('tab', { name: 'الأطفال' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByTestId('wizard-footer-price')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'التالي' })).toBeDisabled();
  await page.getByRole('tab', { name: 'الباقات' }).click();
  await expect(page.getByRole('checkbox', { name: /باقة القص واللحية/ })).not.toBeChecked();
  // One tile on a short step: the bar is at the bottom of the screen, not halfway up (the client's image 3).
  await expectAtScreenBottom(wizardBar(page));

  // The kids' haircut alone, at its own price.
  await page.getByRole('tab', { name: 'الأطفال' }).click();
  await page.locator('label').filter({ has: page.getByRole('checkbox', { name: /حلاقة أطفال/ }) }).click();
  await expect(page.getByTestId('wizard-footer-price')).toContainText('٤٠');
  await expect(page.getByTestId('wizard-footer-note')).toHaveText('حلاقة أطفال');
  await expectAtScreenBottom(wizardBar(page));

  await page.getByRole('button', { name: 'التالي' }).click();
  await expect(page.getByTestId('wizard-title')).toHaveText('اختر المختص');
  await expectAtScreenBottom(wizardBar(page));
});
