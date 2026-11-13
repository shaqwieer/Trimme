import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../support/fixtures';

/**
 * D-143: customer sign-up and sign-in with the mobile number alone, the default when sign-in codes are off.
 * The local compose stack turns codes on for the other flows (`TRIMME_OTP_ENABLED`, default true), so this spec runs
 * only on a stack started with `TRIMME_OTP_ENABLED=false`, and skips itself otherwise.
 */
const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;

test.describe('customer sign-up without a code (D-143)', () => {
  test.beforeEach(async ({ request }) => {
    const options = await request.get('/api/v1/auth/sign-in-options');
    expect(options.ok()).toBeTruthy();
    test.skip((await options.json()).otpRequired === true, 'Sign-in codes are on for this stack.');
  });

  test('signs up and back in with the number alone; codes are refused', async ({ page }) => {
    const national = newPhone();
    await page.goto('/ar/auth/sign-up');
    await expect(page.getByText('أدخل رقم جوالك لإنشاء حسابك')).toBeVisible();
    await expect(page.getByText(/واتساب/)).toHaveCount(0);
    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(axe.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')).toEqual([]);

    await page.getByLabel('رقم الجوال').fill(national);
    await page.getByRole('button', { name: 'إنشاء الحساب' }).click();
    await expect(page.getByText('يجب الموافقة على الشروط وسياسة الخصوصية')).toBeVisible();
    await page.getByLabel(/أوافق على/).check();
    await page.getByRole('button', { name: 'إنشاء الحساب' }).click();

    // No verify step: straight to the profile, then the customer home.
    await expect(page).toHaveURL(/\/ar\/auth\/complete-profile/);
    await page.getByLabel('الاسم الكامل').fill('سلمان الدوسري');
    await page.getByLabel(/أوافق على/).check();
    await page.getByRole('button', { name: 'حفظ ومتابعة' }).click();
    await expect(page).toHaveURL(/\/ar\/discover$/);

    await page.goto('/ar/account');
    await expect(page.getByText('سلمان الدوسري')).toBeVisible();
    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in$/);

    // Back in, from the sign-in page, to the same account.
    await expect(page.getByText('أدخل رقم جوالك لتسجيل الدخول')).toBeVisible();
    await page.getByLabel('رقم الجوال').fill(national);
    await page.getByRole('button', { name: 'تسجيل الدخول', exact: true }).click();
    await expect(page).toHaveURL(/\/ar\/discover$/);
    await page.goto('/ar/account');
    await expect(page.getByText('سلمان الدوسري')).toBeVisible();

    // The code endpoints are off.
    const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json()).token as string;
    const otp = await page.request.post('/api/v1/auth/otp/request', {
      headers: { 'X-CSRF-Token': csrf },
      data: { phone: `+966${national}`, termsAccepted: true, locale: 'ar' },
    });
    expect(otp.status()).toBe(403);
    expect((await otp.json()).errorCode).toBe('otp.disabled');
  });
});
