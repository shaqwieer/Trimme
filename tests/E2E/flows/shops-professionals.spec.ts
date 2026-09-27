import AxeBuilder from '@axe-core/playwright';
import { expect, type Locator, type Page, test } from '@playwright/test';
import { png } from '../support/images';

/**
 * Phase 06 flows against the compose stack (spec §19 E3, partial): the admin edits a shop profile with images, places
 * the shop with the map pin, and adds a professional with a masked WhatsApp number — with no transfer action anywhere.
 * The shop owner edits only what the admin policy opens. The stack uses the built-in (fake) geocoder.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const BLANK_TILE = png(256, 256, [233, 238, 243]);

async function staffSignIn(page: Page, email: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

async function expectNoSeriousAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    // The map canvas is a third-party widget; its keyboard/text alternative is the coordinate form.
    .exclude('[data-testid="location-map"]')
    .analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    blocking.map((v) => `${v.id}: ${v.help} @ ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`),
  ).toEqual([]);
}

/** The element's box once it stops moving (the map eases to a search result before the pin can be dragged). */
async function settledBox(page: Page, locator: Locator) {
  let previous = await locator.boundingBox();
  for (let attempt = 0; attempt < 30; attempt++) {
    await page.waitForTimeout(150);
    const current = await locator.boundingBox();
    if (
      previous &&
      current &&
      Math.abs(current.x - previous.x) < 0.5 &&
      Math.abs(current.y - previous.y) < 0.5
    ) {
      return current;
    }
    previous = current;
  }
  throw new Error('The element kept moving.');
}

/** A fresh, valid Saudi mobile for each run (numbers are unique across professionals). */
function randomMobile(): string {
  return `50${Math.floor(1_000_000 + Math.random() * 8_999_999)}`;
}

test.describe('shops, locations and professionals (R-SHP-01/02, R-PRO-01, R-NEG-01)', () => {
  test('admin edits the profile, uploads a cover, pins the location and adds a professional', async ({
    page,
  }) => {
    // No third-party tiles in automated runs: every tile is a blank image.
    await page.route('https://tile.openstreetmap.org/**', (route) =>
      route.fulfill({ status: 200, contentType: 'image/png', body: BLANK_TILE }),
    );
    const slug = `e2e-pin-${Date.now().toString(36)}`;

    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await expect(page).toHaveURL(/\/en\/admin$/);
    await page.goto('/en/admin/shops/new');
    await page.getByLabel('Shop name in Arabic').fill('صالون الخريطة');
    await page.getByLabel('Shop name in English').fill('Map Salon');
    await page.getByLabel('Shop page link').fill(slug);
    await page.getByRole('button', { name: 'Create shop' }).click();
    await expect(page).toHaveURL(/\/en\/admin\/shops\/[0-9a-f-]{36}$/);
    const shopId = page.url().split('/').pop()!;

    // Profile and images.
    const profile = page.getByTestId('shop-profile-form');
    await profile.getByLabel(/Description in Arabic/).fill('حلاقة رجالية في حي الملقا');
    await profile.getByLabel('Wi-Fi').check();
    await profile.getByRole('switch', { name: 'Verified shop' }).click();
    await profile.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByText('Shop profile saved.')).toBeVisible();
    await expect(page.getByText('Verified', { exact: true })).toBeVisible();

    await page.locator('input[type="file"][aria-label="Cover image"]').setInputFiles({
      name: 'cover.png',
      mimeType: 'image/png',
      buffer: png(1600, 900),
    });
    const cover = page.getByTestId('cover-image');
    await expect(cover).toBeVisible();
    await expect.poll(() => cover.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1600);
    await expectNoSeriousAxeViolations(page);

    // Location: search, then drag the pin to the entrance, confirm and save.
    await page.getByRole('link', { name: 'Location' }).click();
    await expect(page).toHaveURL(/tab=location/);
    await page.getByLabel('Search for an address or district').fill('Malqa');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.getByRole('button', { name: /Al Malqa, Riyadh/ }).click();
    await expect(page.getByTestId('coordinates')).toHaveText('24.812300, 46.601100');

    // The pin exists once the lazily imported MapLibre map has loaded (WebGL); allow for a cold stack.
    const pin = page.getByTestId('map-pin');
    await expect(pin).toBeVisible({ timeout: 15_000 });
    const box = await settledBox(page, pin);
    await page.mouse.move(box.x + box.width / 2, box.y + box.height - 4);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 60, box.y + box.height + 40, { steps: 12 });
    await page.mouse.up();
    await expect(page.getByTestId('coordinates')).not.toHaveText('24.812300, 46.601100');
    await expect(page.getByTestId('resolved-address')).toContainText('Riyadh');
    await expectNoSeriousAxeViolations(page);
    await page.getByRole('button', { name: 'Confirm location' }).click();
    await expect(page.getByText('Location saved.')).toBeVisible();

    const saved = await (await page.request.get(`/api/v1/admin/shops/${shopId}`)).json();
    expect(saved.location.source).toBe('Manual');
    expect(saved.location.latitude).not.toBe(24.8123);
    expect(saved.location.latitude).toBeGreaterThan(24.7);
    expect(saved.location.latitude).toBeLessThan(24.9);
    expect(saved.location.longitude).toBeGreaterThan(46.55);
    expect(saved.location.longitude).toBeLessThan(46.7);

    // A professional in exactly this shop, with a masked WhatsApp number.
    await page.getByRole('link', { name: 'Professionals', exact: true }).last().click();
    await page.getByRole('link', { name: 'Add a professional to this shop' }).click();
    const form = page.getByTestId('create-professional-form');
    await expect(form.getByLabel('Shop')).toHaveValue(shopId);
    await form.getByLabel('Name in Arabic').fill('فيصل التجربة');
    await form.getByLabel('Name in English').fill('Faisal Trial');
    const mobile = randomMobile();
    await form.getByLabel(/Professional's WhatsApp number/).fill(mobile);
    await form.getByRole('button', { name: 'Add professional' }).click();
    await expect(page).toHaveURL(/\/en\/admin\/professionals\/[0-9a-f-]{36}$/);

    const masked = page.getByTestId('whatsapp-masked');
    await expect(masked).toHaveText(`+966 5•• ••• •${mobile.slice(-2)}`);
    await expect(page.getByTestId('professional-shop')).toContainText('Map Salon');
    await expect(page.getByText('The shop is set only when the professional is added.')).toBeVisible();

    // No transfer or move action exists anywhere on the page (DV-S01).
    await expect(page.getByRole('button', { name: /transfer|move|reassign/i })).toHaveCount(0);
    await expect(page.getByRole('link', { name: /transfer|move|reassign/i })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Shop' })).toHaveCount(0);
    await expectNoSeriousAxeViolations(page);

    // Audited reveal needs a reason.
    await page.getByRole('button', { name: 'Show the full number' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Reason for showing it').fill('Confirm the number by phone');
    await dialog.getByRole('button', { name: 'Show', exact: true }).click();
    await expect(masked).toHaveText(`+966${mobile}`);

    // The public page lists the professional without any number.
    const publicList = await page.request.get(`/api/v1/public/shops/${slug}/professionals`);
    expect(publicList.status()).toBe(404); // the shop is still a draft: not published
    const publicText = JSON.stringify(
      await (await page.request.get(`/api/v1/admin/professionals?shopId=${shopId}`)).json(),
    );
    expect(publicText).not.toContain(mobile);
  });

  test('shop owner edits only the fields the admin policy opens', async ({ page }) => {
    await staffSignIn(page, 'owner@al-asala.trimme.local', DEMO_PASSWORD);
    await expect(page).toHaveURL(/\/en\/shop$/);
    await page.goto('/en/shop/settings');

    const form = page.getByTestId('shop-profile-form');
    await expect(form.getByLabel('Shop name in Arabic')).toBeDisabled();
    await expect(form.getByLabel('Shop type')).toBeDisabled();
    await expect(form.getByText('Locked by the admin').first()).toBeVisible();

    const description = form.getByLabel(/Description in English/);
    await description.fill(`Classic cuts and fades — updated ${Date.now().toString(36)}`);
    await form.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByText('Shop profile saved.')).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    // The location is locked by the default policy: read-only, pointing to the admin.
    await page.getByRole('link', { name: 'Location' }).click();
    await expect(page.getByText('Location locked')).toBeVisible();
    await expect(page.getByTestId('location-readonly')).toContainText('24.812300, 46.601100');
    await expect(page.getByRole('button', { name: 'Confirm location' })).toHaveCount(0);

    // The API enforces the same lock whatever the page shows.
    const csrf = (await page.context().cookies()).find((c) => c.name === 'trimme-csrf')?.value ?? '';
    const locked = await page.request.put('/api/v1/shop/location', {
      headers: { 'X-CSRF-Token': csrf },
      data: { latitude: 24.7, longitude: 46.6, source: 'Manual' },
    });
    expect(locked.status()).toBe(403);
  });
});
