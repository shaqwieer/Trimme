import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * D-144: a signed-in user cannot open the landing page or the sign-in and sign-up pages again until they sign out —
 * neither by typing the address nor with the Back button — and a stale hint never locks anyone out of sign-in.
 * Runs on the local stack with codes on (the dev OTP inbox), like flows/auth.spec.ts.
 */
const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;

async function latestOtp(request: APIRequestContext, national: string): Promise<string> {
  const response = await request.get(
    `/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(`+966${national}`)}`,
  );
  expect(response.ok()).toBeTruthy();
  return (await response.json()).code as string;
}

/** Signs a new customer up from the sign-up page, through the code when the stack asks for one. */
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

test.describe('signed-in users stay out of the landing and sign-in pages (D-144)', () => {
  test('typed addresses and the Back button lead home until the customer signs out', async ({ page }) => {
    await page.goto('/ar');
    await expect(page).toHaveURL(/\/ar$/);
    await signUp(page, 'هند القحطاني');

    // The Back button restores the sign-up page, then the landing page, without asking the server; each sends the
    // customer home again.
    // (With codes off there is no verify step, so the history is one page shorter.)
    for (let step = 0; step < 2; step++) {
      await page.goBack();
      if (page.url() === 'about:blank') break;
      await expect(page).toHaveURL(/\/ar\/discover$/);
    }

    // Typed addresses: the landing page and every sign-in page lead home; a sign-in's returnTo is honoured.
    for (const path of ['/ar', '/ar/auth/sign-in', '/ar/auth/sign-up', '/ar/auth/staff/sign-in']) {
      await page.goto(path);
      await expect(page).toHaveURL(/\/ar\/discover$/);
    }
    await page.goto('/ar/auth/sign-in?returnTo=%2Faccount%2Fbookings');
    await expect(page).toHaveURL(/\/ar\/account\/bookings$/);

    // Signing out opens them again.
    await page.goto('/ar/account');
    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in$/);
    expect((await page.context().cookies()).some((c) => c.name === 'trimme-signed-in')).toBe(false);
    // A client-side click on the logo must not replay a home page prefetched while signed in (its redirect).
    await page.locator('header a[href="/ar"]').first().click();
    await expect(page).toHaveURL(/\/ar$/);
    await page.goto('/ar');
    await expect(page).toHaveURL(/\/ar$/);
  });

  test('a hint without a session (signed out elsewhere) never locks the user out of sign-in', async ({
    page,
    baseURL,
  }) => {
    await page
      .context()
      .addCookies([
        { name: 'trimme-signed-in', value: 'Customer', url: baseURL!, secure: true, sameSite: 'Lax' },
      ]);
    await page.goto('/ar');
    // Through the session page, whose refresh fails, to sign-in; the hint is gone.
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in\?returnTo=%2Fdiscover$/);
    expect((await page.context().cookies()).some((c) => c.name === 'trimme-signed-in')).toBe(false);
    await page.goto('/ar');
    await expect(page).toHaveURL(/\/ar$/);
  });
});
