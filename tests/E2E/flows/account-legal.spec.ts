import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * D-144 on the local stack: the admin writes the terms in rich text and the public page shows it (then restores the
 * built-in text, so other specs see the page as seeded); a customer adds an email on personal info, sees why the
 * number is read-only while codes are on, and deletes the account.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';

const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;

async function expectNoSeriousAxe(page: Page, label: string) {
  await expect(page).toHaveTitle(/\S/);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(blocking.map((v) => `${label} ${v.id}: ${v.help}`)).toEqual([]);
}

async function latestOtp(request: APIRequestContext, national: string): Promise<string> {
  const response = await request.get(
    `/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(`+966${national}`)}`,
  );
  expect(response.ok()).toBeTruthy();
  return (await response.json()).code as string;
}

async function signUp(page: Page, name: string) {
  const national = newPhone();
  await page.goto('/ar/auth/sign-up');
  await page.getByLabel('رقم الجوال').fill(national);
  await page.getByLabel(/أوافق على/).check();
  const send = page.getByRole('button', { name: 'إرسال الرمز' });
  if (await send.isVisible()) {
    await send.click();
    await expect(page).toHaveURL(/\/ar\/auth\/verify/);
    await page.getByLabel('رمز التحقق').fill(await latestOtp(page.request, national));
    await page.getByRole('button', { name: 'تحقق' }).click();
  } else {
    await page.getByRole('button', { name: 'إنشاء الحساب' }).click();
  }
  await expect(page).toHaveURL(/\/ar\/auth\/complete-profile/);
  await page.getByLabel('الاسم الكامل').fill(name);
  await page.getByLabel(/أوافق على/).check();
  await page.getByRole('button', { name: 'حفظ ومتابعة' }).click();
  await expect(page).toHaveURL(/\/ar\/discover$/);
}

test.describe('D-144: legal pages, personal info and account deletion', () => {
  test('the admin writes the terms in rich text; the public page shows them until the built-in text is restored', async ({
    page,
    browser,
  }) => {
    await page.goto('/ar/auth/staff/sign-in');
    await page.getByLabel('البريد الإلكتروني').fill(ADMIN_EMAIL);
    await page.getByLabel('كلمة المرور').fill(ADMIN_PASSWORD);
    await page.getByRole('button', { name: 'تسجيل الدخول' }).click();
    await expect(page).toHaveURL(/\/ar\/admin$/);

    await page.getByRole('link', { name: 'الشروط والخصوصية' }).first().click();
    await expect(page).toHaveURL(/\/ar\/admin\/legal/);
    await expect(page.getByTestId('legal-status')).toContainText('النص الافتراضي');
    const editor = page.getByRole('textbox', { name: /نص شروط الاستخدام/ });
    await expect(editor).toContainText('الخدمة');
    await expectNoSeriousAxe(page, 'legal editor');

    try {
      await editor.click();
      await page.keyboard.press('Control+A');
      await page.keyboard.press('Delete');
      await page.getByRole('button', { name: 'عنوان', exact: true }).click();
      await page.keyboard.type('شروط صالونات Groom Here');
      await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'عريض' }).click();
      await page.keyboard.type('تُطبّق هذه الشروط');
      await page.getByRole('button', { name: 'عريض' }).click();
      await page.keyboard.type(' على كل حجز.');
      await page.getByRole('button', { name: 'حفظ ونشر' }).click();
      await expect(page.getByText('حُفظ النص ونُشر.')).toBeVisible();
      await expect(page.getByTestId('legal-status')).toContainText('نص مخصص');

      const visitorContext = await browser.newContext();
      const visitor = await visitorContext.newPage();
      await visitor.goto('/ar/terms');
      const article = visitor.getByTestId('legal-page');
      await expect(article.getByRole('heading', { level: 2, name: 'شروط صالونات Groom Here' })).toBeVisible();
      await expect(article.locator('strong')).toHaveText('تُطبّق هذه الشروط');
      await expect(article).toContainText('آخر تحديث');
      await expectNoSeriousAxe(visitor, 'terms (custom)');
      // The other languages keep their built-in text.
      await visitor.goto('/en/terms');
      await expect(visitor.getByTestId('legal-page')).not.toContainText('Groom Here salons');
      await visitorContext.close();
    } finally {
      await page.goto('/ar/admin/legal');
      const restore = page.getByRole('button', { name: 'استعادة النص الافتراضي' });
      if (await restore.isVisible()) {
        await restore.click();
        await page.getByRole('alertdialog').getByRole('button', { name: 'نعم، استعد الافتراضي' }).click();
        await expect(page.getByTestId('legal-status')).toContainText('النص الافتراضي');
      }
    }
    await page.goto('/ar/terms');
    await expect(page.getByTestId('legal-page')).not.toContainText('شروط صالونات Groom Here');
  });

  test('a customer adds an email, sees the number rules, and deletes the account', async ({ page }) => {
    await signUp(page, 'ريان الشهري');

    // No WhatsApp notifications row any more.
    await page.goto('/ar/account');
    await expect(page.getByText('إشعارات واتساب')).toHaveCount(0);

    await page.goto('/ar/account/profile');
    const phone = page.getByTestId('profile-phone');
    await expect(phone).toContainText('•');
    // The local stack has sign-in codes on (D-143), so the number cannot change here.
    await expect(phone.getByRole('button', { name: 'تغيير الرقم' })).toHaveCount(0);
    await page.getByLabel(/البريد الإلكتروني/).fill('rayan@example.com');
    await expectNoSeriousAxe(page, 'personal info');
    await page.getByRole('button', { name: 'حفظ التغييرات' }).click();
    await expect(page).toHaveURL(/\/ar\/account\?saved=1$/);
    await page.goto('/ar/account/profile');
    await expect(page.getByLabel(/البريد الإلكتروني/)).toHaveValue('rayan@example.com');

    await page.goto('/ar/account');
    const section = page.getByTestId('delete-account');
    await section.getByRole('button', { name: 'حذف حسابي' }).click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText('حذف حسابك نهائياً؟');
    await dialog.getByRole('button', { name: 'نعم، احذف حسابي' }).click();
    await expect(page).toHaveURL(/\/ar$/);
    expect((await page.context().cookies()).some((c) => c.name === 'trimme-signed-in')).toBe(false);
    await page.goto('/ar/account');
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in/);
  });
});
