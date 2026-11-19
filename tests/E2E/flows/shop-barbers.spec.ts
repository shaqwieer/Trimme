import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * D-146 — the salon owner adds and edits their barbers, and has no walk-in page (the barbers book walk-ins). A barber
 * the owner adds gets a login at once: the temporary password the server generates is shown once, the barber signs in
 * with their WhatsApp number and that password, chooses their own and lands on their own dashboard. Each run adds a
 * barber with a new number and turns them off at the end, so reruns never collide and the demo keeps its two barbers
 * bookable.
 */
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const OWNER = 'owner@barber-house.trimme.local';

async function staffSignIn(page: Page, identifier: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email or mobile number').fill(identifier);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

async function expectNoSeriousAxeViolations(page: Page, label: string) {
  // A soft refresh replaces the <title> element; let it settle so axe never sees the page between the two.
  await expect(page).toHaveTitle(/\S/);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    blocking.map(
      (v) => `${label} ${v.id}: ${v.help} @ ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`,
    ),
  ).toEqual([]);
}

test.describe('the owner’s barbers (D-146)', () => {
  test('the owner adds a barber, who signs in with the generated password and replaces it; no walk-ins for the owner', async ({
    browser,
  }) => {
    const ownerContext = await browser.newContext();
    const owner = await ownerContext.newPage();
    await staffSignIn(owner, OWNER, DEMO_PASSWORD);
    await expect(owner).toHaveURL(/\/en\/shop$/);

    // Walk-ins are the barbers': no menu entry and no page for the owner.
    await expect(owner.getByRole('link', { name: 'Walk-in' })).toHaveCount(0);
    await owner.goto('/en/shop/walk-in');
    await expect(owner.getByText('This page is not available to your account')).toBeVisible();

    // A new barber, with a number no run has used (055 + the clock's last 7 digits).
    const suffix = Date.now().toString().slice(-7);
    const national = `55${suffix}`;
    const nameEn = `Barber ${suffix}`;
    await owner.getByRole('link', { name: 'Barbers' }).first().click();
    await expect(owner).toHaveURL(/\/en\/shop\/barbers$/);
    await expectNoSeriousAxeViolations(owner, 'barbers');
    await owner.getByRole('link', { name: 'Add a barber' }).click();
    await expect(owner).toHaveURL(/\/en\/shop\/barbers\/new$/);
    await expect(owner.getByLabel('Shop')).toHaveCount(0);
    // Added by mobile (D-149): the mobile first, then the names and the languages the barber speaks.
    await owner.getByLabel("Barber's mobile").fill(national);
    await owner.getByLabel('Name in Arabic').fill(`حلاق ${suffix}`);
    await owner.getByLabel('Name in English').fill(nameEn);
    await owner.getByRole('checkbox', { name: 'Urdu' }).check();
    await owner.getByRole('button', { name: 'Add the barber' }).click();

    // The password is shown once, in a dialog, never in the address bar.
    const dialog = owner.getByRole('dialog', { name: `${nameEn}'s temporary password` });
    await expect(dialog).toBeVisible();
    const temporary = (await owner.getByTestId('temporary-password-value').textContent())!.trim();
    expect(temporary).toMatch(/^[a-km-np-z2-9]{12}$/);
    expect(owner.url()).not.toContain(temporary);
    await expectNoSeriousAxeViolations(owner, 'password dialog');
    await dialog.getByRole('button', { name: 'Done' }).click();
    await expect(owner).toHaveURL(/\/en\/shop\/barbers\/[0-9a-f-]{36}$/);
    expect(owner.url()).not.toContain(temporary);
    await expect(owner.getByTestId('barber-name')).toHaveText(nameEn);
    await expect(owner.getByText('On — waiting for the first sign-in')).toBeVisible();
    await expect(owner.getByTestId('whatsapp-masked')).not.toContainText(national);

    // The services the barber does, from their page.
    const services = owner.getByTestId('professional-services');
    await services.getByRole('checkbox').first().check();
    await services.getByRole('button', { name: 'Save assignment' }).click();
    await expect(owner.getByText('Service assignment saved.')).toBeVisible();
    await expectNoSeriousAxeViolations(owner, 'barber page');

    // The barber: their number and the temporary password lead to the new-password step, then their own dashboard.
    const barberContext = await browser.newContext();
    const barber = await barberContext.newPage();
    await staffSignIn(barber, `0${national}`, temporary);
    await expect(barber.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
    expect((await barber.request.get('/api/v1/me')).status()).toBe(401);
    await barber.getByLabel('New password').fill('barber own passphrase');
    await barber.getByLabel('Confirm password').fill('barber own passphrase');
    await barber.getByRole('button', { name: 'Save and sign in' }).click();
    await expect(barber).toHaveURL(/\/en\/shop$/);
    const me = (await (await barber.request.get('/api/v1/me')).json()) as {
      professionalId: string;
      permissions: string[];
    };
    expect(owner.url()).toContain(me.professionalId);
    expect(me.permissions).toContain('Shop.Bookings.CreateWalkIn');
    expect(me.permissions).not.toContain('Shop.Barbers.Manage');
    await expect(barber.getByRole('link', { name: 'Walk-in' }).first()).toBeVisible();
    await expect(barber.getByRole('link', { name: 'Barbers' })).toHaveCount(0);

    // Turned off at the end: no longer bookable, and signed out.
    await owner.reload();
    await owner.getByRole('button', { name: 'Disable professional' }).click();
    await owner.getByRole('alertdialog').getByRole('button', { name: 'Disable professional' }).click();
    await expect(owner.getByText('Professional disabled.')).toBeVisible();
    await expect.poll(async () => (await barber.request.get('/api/v1/me')).status()).toBe(401);

    await ownerContext.close();
    await barberContext.close();
  });
});
