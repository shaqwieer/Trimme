import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * Flow E1 (auth portion) and the staff journeys of Phase 04, against the compose stack:
 * - the API runs in Development, so codes go to the dev OTP inbox (never outside Development/Testing);
 * - staff emails go to Mailpit;
 * - the bootstrap SuperAdmin comes from the one-shot `seed` service.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';

const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;
const newEmail = (prefix: string) => `${prefix}.${Date.now()}.${Math.floor(Math.random() * 1e6)}@trimme.test`;

async function latestOtp(request: APIRequestContext, national: string): Promise<string> {
  const response = await request.get(
    `/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(`+966${national}`)}`,
  );
  expect(response.ok()).toBeTruthy();
  return (await response.json()).code as string;
}

async function latestEmailLink(request: APIRequestContext, recipient: string): Promise<URL> {
  let link: string | undefined;
  await expect
    .poll(async () => {
      const search = await request.get(
        `${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(`to:"${recipient}"`)}`,
      );
      const { messages } = (await search.json()) as { messages: Array<{ ID: string }> };
      if (messages.length === 0) return false;
      const message = await (await request.get(`${MAILPIT_URL}/api/v1/message/${messages[0]!.ID}`)).json();
      link = /https?:\/\/\S+/.exec(message.Text as string)?.[0];
      return Boolean(link);
    })
    .toBe(true);
  return new URL(link!);
}

async function expectNoSeriousAxeViolations(page: Page) {
  // A soft refresh replaces the <title> element; let it settle so axe never sees the page between the two.
  await expect(page).toHaveTitle(/\S/);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(blocking.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
}

async function expectNoTokensReachable(page: Page) {
  const exposure = await page.evaluate(() => ({
    local: localStorage.length,
    session: sessionStorage.length,
    cookies: document.cookie,
  }));
  expect(exposure.local).toBe(0);
  expect(exposure.session).toBe(0);
  // The session cookies are HttpOnly: page scripts can see only the CSRF cookie.
  expect(exposure.cookies).not.toContain('trimme-access');
  expect(exposure.cookies).not.toContain('trimme-refresh');
}

async function signUpCustomer(page: Page, national: string, name: string) {
  await page.goto('/ar/auth/sign-up');
  await page.getByLabel('رقم الجوال').fill(national);
  await page.getByLabel(/أوافق على/).check();
  await page.getByRole('button', { name: 'إرسال الرمز' }).click();
  await expect(page).toHaveURL(/\/ar\/auth\/verify/);
  await page.getByLabel('رمز التحقق').fill(await latestOtp(page.request, national));
  await page.getByRole('button', { name: 'تحقق' }).click();
  await expect(page).toHaveURL(/\/ar\/auth\/complete-profile/);
  await page.getByLabel('الاسم الكامل').fill(name);
  await page.getByLabel(/أوافق على/).check();
  await page.getByRole('button', { name: 'حفظ ومتابعة' }).click();
  await expect(page).toHaveURL(/\/ar\/discover$/);
}

test.describe('customer mobile sign-up and sessions (E1 auth, R-AUTH-01/05)', () => {
  test('signs up with a WhatsApp code, completes the profile and manages sessions', async ({ page }) => {
    const national = newPhone();
    await page.goto('/ar/auth/sign-up');
    await expectNoSeriousAxeViolations(page);

    // Terms are required on sign-up.
    await page.getByLabel('رقم الجوال').fill(national);
    await page.getByRole('button', { name: 'إرسال الرمز' }).click();
    await expect(page.getByText('يجب الموافقة على الشروط وسياسة الخصوصية')).toBeVisible();

    await page.getByLabel(/أوافق على/).check();
    await page.getByRole('button', { name: 'إرسال الرمز' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/verify/);
    await expect(page.getByRole('button', { name: 'إعادة إرسال' })).toBeDisabled();
    await expectNoSeriousAxeViolations(page);

    // A wrong code reports the remaining attempts.
    const code = await latestOtp(page.request, national);
    await page.getByLabel('رمز التحقق').fill(code === '000000' ? '111111' : '000000');
    await page.getByRole('button', { name: 'تحقق' }).click();
    await expect(page.getByText(/تبقى محاولتان/)).toBeVisible();

    await page.getByLabel('رمز التحقق').fill(code);
    await page.getByRole('button', { name: 'تحقق' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/complete-profile/);
    await page.getByLabel('الاسم الكامل').fill('ريم العتيبي');
    await page.getByLabel(/أوافق على/).check();
    await page.getByRole('button', { name: 'حفظ ومتابعة' }).click();

    // Signing in lands on the customer home (D-130); the account page shows the new profile.
    await expect(page).toHaveURL(/\/ar\/discover$/);
    await page.goto('/ar/account');
    await expect(page.getByText('ريم العتيبي')).toBeVisible();
    await expect(page.getByText(`+966 5•• ••• •${national.slice(-2)}`)).toBeVisible();
    await expectNoTokensReachable(page);

    await page.getByRole('link', { name: 'الأمان والجلسات' }).click();
    await expect(page.getByTestId('sessions').getByText('هذا الجهاز')).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    await page.goto('/ar/account');
    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in$/);
    await page.goto('/ar/account');
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in\?returnTo=%2Faccount/);
  });

  test('revoking other devices signs them out immediately', async ({ browser }) => {
    const national = newPhone();
    const phone = await browser.newContext();
    const laptop = await browser.newContext();
    const phonePage = await phone.newPage();
    await signUpCustomer(phonePage, national, 'سارة');

    // Same number, second device: sign in (resend cooldown is per number, so wait it out).
    const laptopPage = await laptop.newPage();
    await laptopPage.waitForTimeout(31_000);
    await laptopPage.goto('/ar/auth/sign-in');
    await laptopPage.getByLabel('رقم الجوال').fill(national);
    await laptopPage.getByRole('button', { name: 'إرسال الرمز' }).click();
    await expect(laptopPage).toHaveURL(/\/ar\/auth\/verify/);
    await laptopPage.getByLabel('رمز التحقق').fill(await latestOtp(laptopPage.request, national));
    await laptopPage.getByRole('button', { name: 'تحقق' }).click();
    await expect(laptopPage).toHaveURL(/\/ar\/discover$/);

    await laptopPage.goto('/ar/account/security');
    await expect(laptopPage.getByTestId('sessions').locator('li')).toHaveCount(2);
    await laptopPage.getByRole('button', { name: 'تسجيل الخروج من الأجهزة الأخرى' }).click();
    await laptopPage
      .getByRole('alertdialog')
      .getByRole('button', { name: 'تسجيل الخروج من الأجهزة الأخرى' })
      .click();
    await expect(laptopPage.getByText('سُجّل الخروج من جهاز واحد')).toBeVisible();
    await expect(laptopPage.getByTestId('sessions').locator('li')).toHaveCount(1);

    await phonePage.goto('/ar/account');
    await expect(phonePage).toHaveURL(/\/ar\/auth\/sign-in\?returnTo=%2Faccount/);
    await phone.close();
    await laptop.close();
  });

  test('expired_session_redirects_to_sign_in (R-WEB-08)', async ({ page, context }) => {
    await signUpCustomer(page, newPhone(), 'Lama');

    // Access cookie gone (expired): the guard refreshes silently and returns to the page.
    await context.clearCookies({ name: 'trimme-access' });
    await page.goto('/ar/account/security');
    await expect(page).toHaveURL(/\/ar\/account\/security$/);
    await expect(page.getByTestId('sessions')).toBeVisible();

    // Refresh cookie gone too: sign in again and come back afterwards.
    await context.clearCookies({ name: 'trimme-access' });
    await context.clearCookies({ name: 'trimme-refresh' });
    await page.goto('/ar/account/security');
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in\?returnTo=%2Faccount%2Fsecurity/);
  });
});

test.describe('staff sign-in, invitations and password reset (R-AUTH-02/06, R-WEB-13)', () => {
  test('the bootstrap SuperAdmin signs in and sees the full admin navigation', async ({ page }) => {
    await page.goto('/en/auth/staff/sign-in');
    await expectNoSeriousAxeViolations(page);
    await page.getByLabel('Email').fill(ADMIN_EMAIL);
    await page.getByLabel('Password').fill(ADMIN_PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/en\/admin$/);
    const nav = page.getByRole('navigation', { name: 'Main navigation' }).first();
    await expect(nav.getByRole('link', { name: 'Subscription plans' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Roles & permissions' })).toBeVisible();
    await expectNoTokensReachable(page);
  });

  test('an invited operations manager accepts, sees permission-filtered navigation, and resets the password by email', async ({
    page,
    request,
  }) => {
    // The SuperAdmin invites a colleague through the API (the admin UI for this lands in Phase 14).
    const csrf = (await (await request.get('/api/v1/auth/csrf')).json()).token as string;
    const signIn = await request.post('/api/v1/auth/staff/sign-in', {
      data: { email: ADMIN_EMAIL, password: ADMIN_PASSWORD },
      headers: { 'X-CSRF-Token': csrf },
    });
    expect(signIn.ok()).toBeTruthy();
    const rotated = (await request.storageState()).cookies.find((c) => c.name === 'trimme-csrf')!.value;
    const invitee = newEmail('ops');
    const invite = await request.post('/api/v1/admin/staff/invitations', {
      data: { email: invitee, role: 'OperationsManager', locale: 'en' },
      headers: { 'X-CSRF-Token': rotated },
    });
    expect(invite.status()).toBe(201);

    const inviteLink = await latestEmailLink(request, invitee);
    await page.goto(`${inviteLink.pathname}${inviteLink.search}`);
    await page.getByLabel('Full name').fill('Huda Operations');
    await page.getByLabel('New password').fill('operations passphrase');
    await page.getByLabel('Confirm password').fill('operations passphrase');
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page).toHaveURL(/\/en\/admin$/);
    const nav = page.getByRole('navigation', { name: 'Main navigation' }).first();
    await expect(nav.getByRole('link', { name: 'Shops' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Subscription plans' })).toHaveCount(0);

    await page.getByRole('button', { name: 'Sign out' }).first().click();
    await expect(page).toHaveURL(/\/en\/auth\/staff\/sign-in$/);

    await page.getByRole('link', { name: 'Forgot your password?' }).click();
    await page.getByLabel('Forgot your password').fill(invitee);
    await page.getByRole('button', { name: 'Send link' }).click();
    await expect(page.getByRole('heading', { name: 'Check your email', level: 2 })).toBeVisible();

    const resetLink = await latestEmailLink(request, invitee);
    expect(resetLink.pathname).toBe('/en/auth/reset-password');
    await page.goto(`${resetLink.pathname}${resetLink.search}`);
    await page.getByLabel('New password').fill('a fresh passphrase');
    await page.getByLabel('Confirm password').fill('a fresh passphrase');
    await page.getByRole('button', { name: 'Save password' }).click();
    await expect(page.getByRole('heading', { name: 'Password changed', level: 2 })).toBeVisible();

    await page.goto('/en/auth/staff/sign-in');
    await page.getByLabel('Email').fill(invitee);
    await page.getByLabel('Password').fill('a fresh passphrase');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/en\/admin$/);
  });

  test('a customer cannot open the admin area', async ({ page }) => {
    await signUpCustomer(page, newPhone(), 'Nora');
    await page.goto('/ar/admin');
    await expect(page.getByRole('heading', { name: 'هذه الصفحة غير متاحة لحسابك' })).toBeVisible();
  });
});
