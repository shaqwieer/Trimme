import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * Phase 07 flows (spec §19 E2, service part; R-SVC-01/04): a shop sets its own service price and duration, reorders
 * and archives; the admin moderates, corrects with an audit reason and assigns services to a professional of that
 * shop only. The compose seed provides the demo shops, professionals and catalogue (DemoData).
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const FAISAL = '0199a0de-5a10-7000-8000-000000000101';

async function staffSignIn(page: Page, email: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/en\/(admin|shop)$/);
}

async function expectNoSeriousAxeViolations(page: Page) {
  // A soft refresh replaces the <title> element; let it settle so axe never sees the page between the two.
  await expect(page).toHaveTitle(/\S/);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    blocking.map((v) => `${v.id}: ${v.help} @ ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`),
  ).toEqual([]);
}

async function csrf(context: BrowserContext, request: APIRequestContext): Promise<string> {
  const existing = (await context.cookies()).find((c) => c.name === 'trimme-csrf')?.value;
  if (existing) return existing;
  await request.get('/api/v1/auth/csrf');
  return (await context.cookies()).find((c) => c.name === 'trimme-csrf')!.value;
}

test.describe('services and packages (R-SVC-01/04, E2 service part)', () => {
  test('the salon owner adds a service with its own price, duration and barbers, edits, reorders and archives it; the desk cannot (D-137)', async ({
    browser,
  }) => {
    const name = `حلاقة تجربة ${Date.now().toString(36)}`;
    const ownerContext = await browser.newContext();
    const page = await ownerContext.newPage();
    await staffSignIn(page, 'owner@barber-house.trimme.local', DEMO_PASSWORD);
    // The sidebar at this width; the drawer on a phone.
    await page.getByRole('link', { name: 'Services', exact: true }).click();
    await expect(page).toHaveURL(/\/en\/shop\/services$/);
    await expect(page.getByTestId('shop-services')).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    await page.getByRole('link', { name: 'Add service' }).click();
    const form = page.getByTestId('service-form');
    await form.getByLabel('Service name in Arabic').fill(name);
    await form.getByLabel('Price (SAR)').fill('٧٢٫٥'); // Arabic-Indic digits and decimal separator
    await form.getByLabel('Duration').selectOption('45');
    // Every active barber starts picked; keep one.
    const barbers = form.getByTestId('service-professionals').getByRole('checkbox');
    const count = await barbers.count();
    expect(count).toBeGreaterThan(0);
    for (let i = 1; i < count; i++) await barbers.nth(i).uncheck();
    await expectNoSeriousAxeViolations(page);
    await form.getByRole('button', { name: 'Create service' }).click();
    await expect(page).toHaveURL(/\/en\/shop\/services\/[0-9a-f-]{36}$/);
    const serviceId = page.url().split('/').pop()!;
    await expect(page.getByRole('heading', { name })).toBeVisible();
    const created = await (await page.request.get(`/api/v1/shop/services/${serviceId}`)).json();
    expect(created.price).toBe(72.5);
    expect(created.professionalIds).toHaveLength(1);

    // Bookable at once by customers, with that barber.
    const listed = await (await page.request.get('/api/v1/public/shops/barber-house/services')).json();
    expect(listed.find((s: { id: string }) => s.id === serviceId)?.professionalIds).toEqual(
      created.professionalIds,
    );

    await form.getByLabel('Price (SAR)').fill('80');
    await form.getByLabel('Duration').selectOption('50');
    await form.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText('Changes saved.')).toBeVisible();
    const saved = await (await page.request.get(`/api/v1/shop/services/${serviceId}`)).json();
    expect(saved.price).toBe(80);
    expect(saved.durationMinutes).toBe(50);
    expect(saved.professionalIds).toEqual(created.professionalIds);

    // Keyboard reorder: the new service is last; move it up once.
    await page.goto('/en/shop/services');
    const row = page.getByTestId(`catalog-row-${serviceId}`);
    await expect(row).toContainText('SAR 80');
    await expect(row).toContainText('50 min');
    const moveUp = row.getByRole('button', { name: `Move ${name} up` });
    await moveUp.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByText(new RegExp(`${name} moved to position`))).toBeAttached();

    // Off, then archive (confirm): the demo shop keeps no test service in use.
    await row.getByRole('switch', { name: `${name}: bookable` }).click();
    await expect(row).toContainText('Off');
    await row.getByRole('button', { name: `Archive ${name}` }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Archive' }).click();
    await expect(page.getByTestId(`catalog-row-${serviceId}`)).toHaveCount(0);
    await page.getByRole('link', { name: 'Show archived' }).click();
    await expect(page.getByTestId(`catalog-row-${serviceId}`)).toContainText('Archived');

    // The desk has no services page and no way to add one.
    const deskContext = await browser.newContext();
    const desk = await deskContext.newPage();
    await staffSignIn(desk, 'staff@barber-house.trimme.local', DEMO_PASSWORD);
    await expect(desk.getByRole('link', { name: 'Services', exact: true })).toHaveCount(0);
    await desk.goto('/en/shop/services');
    await expect(desk.getByTestId('shop-services')).toHaveCount(0);
    const refused = await desk.request.post('/api/v1/shop/services', {
      headers: { 'X-CSRF-Token': await csrf(deskContext, desk.request) },
      data: { nameAr: name, price: 50, durationMinutes: 30, onlineBookable: true },
    });
    expect(refused.status()).toBe(403);
    await deskContext.close();
    await ownerContext.close();
  });

  test('the admin adds the salon’s package from its Services tab (D-130)', async ({ browser }) => {
    const ownerContext = await browser.newContext();
    const owner = await ownerContext.newPage();
    await staffSignIn(owner, 'owner@barber-house.trimme.local', DEMO_PASSWORD);

    const adminContext = await browser.newContext();
    const admin = await adminContext.newPage();
    await staffSignIn(admin, ADMIN_EMAIL, ADMIN_PASSWORD);
    const shops = await (await admin.request.get('/api/v1/admin/shops?search=Barber%20House')).json();
    const shopId = shops.items[0].id as string;
    await admin.goto(`/en/admin/shops/${shopId}?tab=services`);
    await admin.getByRole('link', { name: 'Add a package' }).click();
    await expect(admin).toHaveURL(/\/en\/admin\/packages\/new\?shopId=/);
    const name = `باقة تجربة ${Date.now().toString(36)}`;
    const form = admin.getByTestId('package-form');
    await form
      .getByLabel(/name in Arabic/i)
      .first()
      .fill(name);
    await form.getByLabel(/Price/).fill('99');
    const choices = form.getByRole('checkbox');
    await choices.nth(0).check();
    await choices.nth(1).check();
    await expectNoSeriousAxeViolations(admin);
    await form.getByRole('button').last().click();
    await expect(admin).toHaveURL(new RegExp(`/en/admin/shops/${shopId}\\?tab=services`));
    const listed = admin.getByTestId('shop-packages').getByRole('link', { name });
    await expect(listed).toBeVisible();

    // Leave the demo shop clean: retire the package this run created.
    const id = ((await listed.getAttribute('href')) ?? '').split('/').pop()!;
    const archived = await owner.request.post(`/api/v1/shop/packages/${id}/archive`, {
      headers: { 'X-CSRF-Token': await csrf(ownerContext, owner.request) },
    });
    expect(archived.status()).toBe(200);
    await adminContext.close();
    await ownerContext.close();
  });

  test('admin moderates, edits the shop’s service and assigns only the shop’s own services', async ({
    browser,
  }) => {
    const ownerContext = await browser.newContext();
    const owner = await ownerContext.newPage();
    await staffSignIn(owner, 'owner@al-asala.trimme.local', DEMO_PASSWORD);
    const created = await owner.request.post('/api/v1/shop/services', {
      headers: { 'X-CSRF-Token': await csrf(ownerContext, owner.request) },
      data: {
        nameAr: `خدمة للإشراف ${Date.now().toString(36)}`,
        price: 60,
        durationMinutes: 30,
        onlineBookable: true,
      },
    });
    expect(created.status()).toBe(201);
    const service = await created.json();

    const adminContext = await browser.newContext();
    const admin = await adminContext.newPage();
    await staffSignIn(admin, ADMIN_EMAIL, ADMIN_PASSWORD);
    await admin.goto('/en/admin/services');
    await expect(admin.getByRole('link', { name: 'Categories', exact: true })).toBeVisible();
    await expectNoSeriousAxeViolations(admin);

    // Moderation: hidden from the public catalogue until shown again.
    await admin.goto(`/en/admin/services/${service.id}`);
    await admin.getByRole('button', { name: 'Hide from customers' }).first().click();
    await admin.getByRole('dialog').getByLabel('Reason').fill('Misleading description');
    await admin.getByRole('dialog').getByRole('button', { name: 'Hide from customers' }).click();
    await expect(admin.getByText('Service hidden.')).toBeVisible();
    const hidden = await (await admin.request.get('/api/v1/public/shops/al-asala/services')).json();
    expect(hidden.map((s: { id: string }) => s.id)).not.toContain(service.id);
    await admin.getByRole('button', { name: 'Show to customers' }).click();
    await expect(admin.getByText('Service visible again.')).toBeVisible();
    // Wait for the refreshed page (new version) before editing again; an older version is a real conflict (409).
    await expect(admin.getByRole('button', { name: 'Hide from customers' })).toBeVisible();

    // An admin who builds shops' catalogues edits the service directly (D-127); the support override with a reason
    // is for admins without that permission. Either way the price stays the shop's own and the change is audited.
    const manage = admin.getByTestId('service-manage');
    await expect(admin.getByTestId('service-override')).toHaveCount(0);
    await manage.getByLabel('Price (SAR)').fill('55');
    await manage.getByRole('button', { name: 'Save changes' }).click();
    await expect(admin.getByText('Changes saved.')).toBeVisible();
    expect((await (await admin.request.get(`/api/v1/admin/services/${service.id}`)).json()).price).toBe(55);
    await expectNoSeriousAxeViolations(admin);

    // Assignment: only the professional's own shop's services are offered.
    await admin.goto(`/en/admin/professionals/${FAISAL}`);
    const assignment = admin.getByTestId('professional-services');
    await expect(assignment).toContainText('Haircut');
    await expect(assignment).not.toContainText('Cut & style'); // Barber House's service
    await assignment.getByLabel(new RegExp(service.nameAr)).check();
    await assignment.getByRole('button', { name: 'Save assignment' }).click();
    await expect(admin.getByText('Service assignment saved.')).toBeVisible();
    const assigned = await (await admin.request.get(`/api/v1/admin/professionals/${FAISAL}/services`)).json();
    expect(assigned.services.find((s: { serviceId: string }) => s.serviceId === service.id).assigned).toBe(
      true,
    );

    // The shop cannot assign (R-NEG-06).
    const forbidden = await owner.request.put(`/api/v1/admin/professionals/${FAISAL}/services`, {
      headers: { 'X-CSRF-Token': await csrf(ownerContext, owner.request) },
      data: { serviceIds: [service.id] },
    });
    expect(forbidden.status()).toBe(403);

    // Leave the demo shop clean: retire the service this run created.
    const archived = await owner.request.post(`/api/v1/shop/services/${service.id}/archive`, {
      headers: { 'X-CSRF-Token': await csrf(ownerContext, owner.request) },
    });
    expect(archived.status()).toBe(200);

    await adminContext.close();
    await ownerContext.close();
  });
});
