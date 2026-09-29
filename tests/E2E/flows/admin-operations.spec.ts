import AxeBuilder from '@axe-core/playwright';
import { type APIRequestContext, type BrowserContext, expect, type Page, test } from '@playwright/test';

/**
 * Phase 14 flows (R-AD-01/05/06/07/11/12/13): the platform overview, a booking intervention (reschedule, then cancel on
 * the shop's behalf) with its audit trail, the customers directory with the audited phone reveal, reviews moderation,
 * roles and staff, and the settings screen. Staff sign-in only (no OTP budget). Every test creates its own data or
 * leaves shared data as it found it: no seeded review is hidden and no setting is saved, because other specs assert
 * them.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const BARBER_HOUSE_STAFF = 'staff@barber-house.trimme.local';
const SARA = { id: '0199a0de-5a10-7000-8000-000000000903', phone: '+966500100303' };

async function staffSignIn(page: Page, email: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/en\/(admin|shop)$/);
}

async function expectNoSeriousAxeViolations(page: Page) {
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

/** Riyadh calendar day, as the platform calendar (D-077). */
function riyadhDay(offsetDays = 0): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(
    new Date(Date.now() + offsetDays * 86_400_000),
  );
}

test.describe('admin operations (Phase 14)', () => {
  test('overview KPIs; a booking rescheduled and cancelled on the shop’s behalf, with its audit trail', async ({
    browser,
  }) => {
    // A walk-in at Barber House four days ahead (no other spec books there on that day), recorded by the shop's staff.
    const shopContext = await browser.newContext();
    const shopPage = await shopContext.newPage();
    await staffSignIn(shopPage, BARBER_HOUSE_STAFF, DEMO_PASSWORD);
    const request = shopPage.request;
    const token = await csrf(shopContext, request);
    const services = (await (await request.get('/api/v1/shop/services')).json()) as Array<{
      id: string;
      isActive: boolean;
      isArchived: boolean;
    }>;
    const service = services.find((s) => s.isActive && !s.isArchived)!;
    const day = riyadhDay(4);
    const options = (await (
      await request.get(`/api/v1/shop/availability/walk-in?serviceId=${service.id}&date=${day}`)
    ).json()) as {
      professionals: Array<{ id: string; starts: string[] }>;
    };
    const professional = options.professionals.find((p) => p.starts.length > 2)!;
    const customerName = `E2E admin ${Date.now().toString(36)}`;
    const walkIn = await request.post('/api/v1/shop/bookings/walk-in', {
      headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() },
      data: {
        serviceId: service.id,
        professionalId: professional.id,
        startsAt: professional.starts.at(-1),
        customerName,
      },
    });
    expect(walkIn.status()).toBe(201);
    const bookingId = ((await walkIn.json()) as { id: string }).id;
    await shopContext.close();

    const context = await browser.newContext();
    const page = await context.newPage();
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);

    // The overview: KPIs from the API, the 14-day trend, a period switch.
    await expect(page.getByTestId('admin-kpis')).toBeVisible();
    await expect(page.getByTestId('booking-trend')).toBeVisible();
    await expect(page.getByText('Appointments today')).toBeVisible();
    await expectNoSeriousAxeViolations(page);
    await page.getByRole('link', { name: 'Last 7 days' }).click();
    await expect(page).toHaveURL(/\/en\/admin\?days=7$/);
    await expect(page.getByRole('link', { name: 'Last 7 days' })).toHaveAttribute('aria-current', 'page');

    // Global search by the customer's name finds the walk-in; the detail has no phone.
    await page.goto(`/en/admin/bookings?q=${encodeURIComponent(customerName)}`);
    await page.getByRole('link', { name: /\d/ }).filter({ hasText: /am|pm/ }).first().click();
    await expect(page).toHaveURL(new RegExp(`/en/admin/bookings/${bookingId}$`));
    await expect(page.getByText(customerName).first()).toBeVisible();
    await expect(page.getByText('+966')).toHaveCount(0);
    await expectNoSeriousAxeViolations(page);

    // Reschedule to the next day's first free time, with a reason.
    const panel = page.getByTestId('booking-intervention');
    await panel.getByRole('button', { name: 'Reschedule' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Date').fill(riyadhDay(5));
    await dialog.getByTestId('reschedule-slots').getByRole('radio').first().check({ force: true });
    await dialog.getByLabel('Reason').fill('Customer asked by phone');
    await dialog.getByRole('button', { name: 'Confirm the new time' }).click();
    await expect(page.getByText('The booking was rescheduled.')).toBeVisible();
    await expect(page.getByText(/Time changed \(was/)).toBeVisible();

    // Cancel on the shop's behalf.
    await panel.getByRole('button', { name: 'Cancel' }).click();
    await page.getByRole('dialog').getByLabel('Reason').fill('Shop closed that day');
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click();
    await expect(page.getByText('The booking was updated.')).toBeVisible();
    await expect(page.getByText('Cancelled by shop').first()).toBeVisible();

    // Both interventions are in the activity log with their reasons.
    await page.goto(`/en/admin/audit?entityType=Booking&entityId=${bookingId}`);
    const log = page.getByTestId('audit-log');
    await expect(log.getByText('Booking rescheduled by the platform')).toBeVisible();
    await expect(log.getByText('Booking cancelled by the platform')).toBeVisible();
    await expect(log.getByText('Reason: Customer asked by phone')).toBeVisible();
    await expectNoSeriousAxeViolations(page);
    await context.close();
  });

  test('customers directory: figures, masked number, audited reveal with a reason', async ({ page }) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/en/admin/customers');
    await expect(page.getByRole('link', { name: 'سارة العنزي' }).first()).toBeVisible();
    await expect(page.getByText(SARA.phone)).toHaveCount(0);
    await expectNoSeriousAxeViolations(page);

    await page.goto(`/en/admin/customers/${SARA.id}`);
    const phone = page.getByTestId('customer-phone');
    await expect(phone).toContainText('03');
    await expect(phone).not.toContainText(SARA.phone);
    await expectNoSeriousAxeViolations(page);
    await page.getByRole('button', { name: 'Show number' }).click();
    await page.getByRole('dialog').getByLabel('Reason').fill('E2E support check');
    await page.getByRole('dialog').getByRole('button', { name: 'Show number' }).click();
    await expect(phone).toHaveText(SARA.phone);

    await page.goto(`/en/admin/audit?entityType=Customer&entityId=${SARA.id}`);
    await expect(page.getByTestId('audit-log').getByText("Customer's number viewed").first()).toBeVisible();
    await expect(page.getByTestId('audit-log').getByText(SARA.phone)).toHaveCount(0);
  });

  test('reviews moderation: report a review, then clear the report (the public rating is untouched)', async ({
    page,
  }) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/en/admin/reviews?queue=Published');
    await expectNoSeriousAxeViolations(page);
    const review = page
      .getByTestId('admin-review')
      .filter({ has: page.getByRole('button', { name: 'Report' }) })
      .first();
    const author = await review.getByRole('heading').innerText();
    await review.getByRole('button', { name: 'Report' }).click();
    await page.getByRole('dialog').getByLabel('Reason').fill('E2E moderation check');
    await page.getByRole('dialog').getByRole('button', { name: 'Report' }).click();
    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(page.getByText('Reported: E2E moderation check').first()).toBeVisible();

    await page.goto('/en/admin/reviews?flag=Reported');
    const reported = page
      .getByTestId('admin-review')
      .filter({ hasText: author })
      .filter({ hasText: 'E2E moderation check' })
      .first();
    await expect(reported).toBeVisible();
    await expect(reported.getByText('Reported: E2E moderation check')).toBeVisible();
    await reported.getByRole('button', { name: 'Publish' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Publish' }).click();
    await expect(page.getByRole('alertdialog')).toBeHidden();
    await expect(
      page
        .getByTestId('admin-review')
        .filter({ hasText: author })
        .filter({ hasText: 'E2E moderation check' }),
    ).toHaveCount(0);
  });

  test('roles and staff: create a role, grant a permission, see it in the matrix, delete it', async ({
    page,
  }) => {
    const roleName = `E2E auditors ${Date.now().toString(36)}`;
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/en/admin/roles');
    await expect(page.getByTestId('permission-matrix')).toBeVisible();
    await expectNoSeriousAxeViolations(page);
    await page.getByLabel('Role name').fill(roleName);
    await page.getByRole('button', { name: 'Create role' }).click();
    await expect(page.getByRole('heading', { name: roleName })).toBeVisible();

    const editor = page.getByTestId('role-editor');
    await editor.getByLabel('View the activity log').check();
    await editor.getByRole('button', { name: 'Save permissions' }).click();
    await expect(page.getByText('Permissions saved.')).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    await page.goto('/en/admin/roles');
    await expect(page.getByTestId('permission-matrix').getByRole('link', { name: roleName })).toBeVisible();
    await page.getByTestId('permission-matrix').getByRole('link', { name: roleName }).click();
    await page.getByTestId('role-editor').getByRole('button', { name: 'Delete role' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete role' }).click();
    await expect(page).toHaveURL(/\/en\/admin\/roles$/);
    await expect(page.getByTestId('permission-matrix').getByRole('link', { name: roleName })).toHaveCount(0);

    await page.goto('/en/admin/roles/staff');
    await expect(page.getByText('You').first()).toBeVisible();
    await expectNoSeriousAxeViolations(page);
  });

  test('settings: sectioned screen with a save bar that tracks changes (nothing is saved)', async ({
    page,
  }) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/en/admin/settings');
    const bar = page.getByTestId('settings-save-bar');
    await expect(bar.getByText('All changes are saved.')).toBeVisible();
    await expect(bar.getByRole('button', { name: 'Save settings' })).toBeDisabled();
    await expectNoSeriousAxeViolations(page);
    const threshold = page.getByLabel('Expiring-soon threshold (days)');
    await threshold.fill('500');
    await expect(bar.getByText('You have unsaved changes.')).toBeVisible();
    await bar.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('This value is out of range')).toBeVisible();
    await bar.getByRole('button', { name: 'Discard changes' }).click();
    await expect(bar.getByText('All changes are saved.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Recent changes' })).toBeVisible();
  });

  test('admin pages fit phone, tablet and desktop widths', async ({ page }, testInfo) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    const paths = [
      '/ar/admin',
      '/ar/admin/bookings',
      '/ar/admin/customers',
      `/ar/admin/customers/${SARA.id}`,
      '/ar/admin/reviews',
      '/ar/admin/roles',
      '/ar/admin/roles/staff',
      '/ar/admin/audit',
      '/ar/admin/settings',
    ];
    for (const width of [390, 768, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      for (const path of paths) {
        await page.goto(path, { waitUntil: 'load' });
        await page.evaluate(() => document.fonts.ready);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        expect(overflow, `${path} at ${width}px`).toBeLessThanOrEqual(1);
        await testInfo.attach(`${path.replaceAll('/', '_')}-${width}`, {
          body: await page.screenshot({ fullPage: true }),
          contentType: 'image/png',
        });
      }
    }
  });
});
