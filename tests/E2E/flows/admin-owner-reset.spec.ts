import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * D-158 (the client's request): the admin's shop page names the shop's owner, and «New temporary password» gives them
 * one, shown once, to hand over; the owner's next sign-in asks for a password of their own (D-139). The test makes its
 * own owner on Barber House: the seeded owners keep their passwords, since other specs sign in as them.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const BARBER_HOUSE = '0199a0de-5a10-7000-8000-000000000002';
const FIRST_PASSWORD = 'first temporary 2026';

async function staffSignIn(page: Page, email: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email or mobile number').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test.describe('the shop owner on the admin shop page (D-158)', () => {
  test('the admin sees the owner and gives a new temporary password, which the owner replaces at sign-in', async ({
    page,
    browser,
  }) => {
    const suffix = Date.now().toString(36);
    const email = `owner-${suffix}@barber-house.trimme.local`;
    const name = `Owner ${suffix}`;

    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await expect(page).toHaveURL(/\/en\/admin$/);
    // A second owner account for Barber House, made by hand with a temporary password (D-139).
    await page.goto(`/en/admin/shops/${BARBER_HOUSE}?tab=users`);
    await page.getByRole('tab', { name: 'Create manually' }).click();
    await page.getByLabel('Name', { exact: true }).fill(name);
    await page.getByLabel('Email', { exact: true }).fill(email);
    await page.getByLabel('Temporary password').fill(FIRST_PASSWORD);
    await page.getByRole('button', { name: 'Create the account' }).click();
    await expect(page.getByText('Account created.')).toBeVisible();

    // The owner replaces the first password; then forgets the new one.
    const ownerContext = await browser.newContext();
    const owner = await ownerContext.newPage();
    await staffSignIn(owner, email, FIRST_PASSWORD);
    await expect(owner.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
    await owner.getByLabel('New password').fill('my own passphrase 1');
    await owner.getByLabel('Confirm password').fill('my own passphrase 1');
    await owner.getByRole('button', { name: 'Save and sign in' }).click();
    await expect(owner).toHaveURL(/\/en\/shop$/);

    // «Owner» on the shop page, on any tab: the name, the sign-in email, and the button.
    await page.goto(`/en/admin/shops/${BARBER_HOUSE}`);
    const card = page.getByTestId('shop-owner');
    const row = card.getByRole('listitem').filter({ hasText: email });
    await expect(row).toContainText(name);
    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(axe.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')).toEqual([]);

    await row.getByRole('button', { name: 'New temporary password' }).click();
    const confirm = page.getByRole('alertdialog');
    await expect(confirm).toContainText(`A new temporary password for ${name}?`);
    await confirm.getByRole('button', { name: 'New temporary password' }).click();

    const dialog = page.getByRole('dialog', { name: `${name}'s temporary password` });
    await expect(dialog).toContainText(`with their email ${email}`);
    const temporary = (await page.getByTestId('temporary-password-value').textContent())!.trim();
    expect(temporary).toMatch(/^[a-km-np-z2-9]{12}$/);
    expect(page.url()).not.toContain(temporary);
    await dialog.getByRole('button', { name: 'Done' }).click();
    await expect(row).toContainText('Waiting for a new password');

    // The owner's open session ended at once.
    await owner.goto('/en/shop');
    await expect(owner).toHaveURL(/\/en\/auth\/staff\/sign-in/);

    // The temporary password signs in only through choosing a new one.
    await staffSignIn(owner, email, temporary);
    await expect(owner.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
    await owner.getByLabel('New password').fill('my own passphrase 2');
    await owner.getByLabel('Confirm password').fill('my own passphrase 2');
    await owner.getByRole('button', { name: 'Save and sign in' }).click();
    await expect(owner).toHaveURL(/\/en\/shop$/);
    await ownerContext.close();
  });
});
