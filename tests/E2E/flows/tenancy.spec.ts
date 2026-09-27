import AxeBuilder from '@axe-core/playwright';
import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

/**
 * Phase 05 tenancy flows against the compose stack. The `seed` service provides the bootstrap SuperAdmin and two
 * demo shops (Al Asala, Barber House), each with an owner and a staff account (DemoData).
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';
const BARBER_HOUSE_ID = '0199a0de-5a10-7000-8000-000000000002';

async function staffSignIn(page: Page, email: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
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
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(blocking.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
}

test.describe('shops and tenancy (R-SHP-01, R-AUTH-02, R-TEN-01/06)', () => {
  test('admin creates and activates a shop, invites its owner, then suspends it', async ({ browser }) => {
    const adminContext = await browser.newContext();
    const ownerContext = await browser.newContext();
    const admin = await adminContext.newPage();
    const owner = await ownerContext.newPage();
    const slug = `e2e-${Date.now().toString(36)}`;
    const ownerEmail = `owner.${slug}@trimme.test`;

    await staffSignIn(admin, ADMIN_EMAIL, ADMIN_PASSWORD);
    await expect(admin).toHaveURL(/\/en\/admin$/);
    await admin
      .getByRole('navigation', { name: 'Main navigation' })
      .first()
      .getByRole('link', { name: 'Shops' })
      .click();
    await expect(admin).toHaveURL(/\/en\/admin\/shops$/);
    await expectNoSeriousAxeViolations(admin);

    await admin.getByRole('link', { name: 'Add shop' }).click();
    await admin.getByLabel('Shop name in Arabic').fill('صالون التجربة');
    await admin.getByLabel('Shop name in English').fill('Trial Salon');
    await admin.getByLabel('Shop page link').fill(slug);
    await admin.getByRole('button', { name: 'Create shop' }).click();
    await expect(admin).toHaveURL(/\/en\/admin\/shops\/[0-9a-f-]{36}$/);
    await expect(admin.getByText('Draft', { exact: true })).toBeVisible();
    await expectNoSeriousAxeViolations(admin);

    await admin.getByRole('button', { name: 'Activate shop' }).click();
    await expect(admin.getByText('Shop activated')).toBeVisible();
    await expect(admin.getByText('Active', { exact: true })).toBeVisible();

    // Invitations live on the shop's "Accounts" tab (Phase 06 tabs: profile, location, accounts, professionals).
    await admin.getByRole('link', { name: 'Accounts' }).click();
    await admin.getByLabel('Email').fill(ownerEmail);
    await admin.getByRole('button', { name: 'Send invitation' }).click();
    await expect(admin.getByText('Invitation sent.')).toBeVisible();

    const link = await latestEmailLink(owner.request, ownerEmail);
    await owner.goto(`${link.pathname}${link.search}`);
    await owner.getByLabel('Full name').fill('Trial Owner');
    await owner.getByLabel('New password').fill('trial owner passphrase');
    await owner.getByLabel('Confirm password').fill('trial owner passphrase');
    await owner.getByRole('button', { name: 'Create account' }).click();
    await expect(owner).toHaveURL(/\/en\/shop$/);
    await expect(owner.getByTestId('shop-name')).toHaveText('Trial Salon');

    // Suspension applies on the owner's very next request.
    await admin.getByRole('button', { name: 'Suspend shop' }).click();
    await admin.getByRole('alertdialog').getByLabel('Reason (optional)').fill('E2E check');
    await admin.getByRole('alertdialog').getByRole('button', { name: 'Suspend shop' }).click();
    await expect(admin.getByText('Shop suspended')).toBeVisible();

    await owner.reload();
    await expect(owner.getByText('Your shop is suspended')).toBeVisible();

    await adminContext.close();
    await ownerContext.close();
  });

  test('shop_user_cannot_open_another_shop', async ({ page }) => {
    await staffSignIn(page, 'owner@al-asala.trimme.local', DEMO_PASSWORD);
    await expect(page).toHaveURL(/\/en\/shop$/);
    await expect(page.getByTestId('shop-name')).toHaveText('Al Asala Barbershop');
    await expectNoSeriousAxeViolations(page);

    // Another shop's admin page: refused in the UI and by the API.
    await page.goto(`/en/admin/shops/${BARBER_HOUSE_ID}`);
    await expect(
      page.getByRole('heading', { name: 'This page is not available to your account' }),
    ).toBeVisible();
    const api = await page.request.get(`/api/v1/admin/shops/${BARBER_HOUSE_ID}`);
    expect(api.status()).toBe(403);

    // A shop id in the request is ignored: the tenant comes from the session.
    const mine = await page.request.get(`/api/v1/shop/me?shopId=${BARBER_HOUSE_ID}`);
    expect((await mine.json()).slug).toBe('al-asala');
  });
});
