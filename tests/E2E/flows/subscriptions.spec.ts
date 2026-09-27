import AxeBuilder from '@axe-core/playwright';
import { type APIRequestContext, type BrowserContext, expect, type Page, test } from '@playwright/test';

/**
 * Phase 08 flows (spec §19): E5 — SuperAdmin creates a plan with a price, it is assigned to a shop, a future price
 * version is added, and the recorded history does not change while the renewal takes the new price; and the
 * subscription part of E3 — admins see statuses, suspend/reinstate and override with a reason; shops see their own
 * status. The compose seed provides one subscription per status (DemoSubscriptionsSeeder).
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';

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

/** Riyadh calendar day, as the API's platform calendar (D-077). */
function riyadhDay(offsetDays = 0): string {
  const now = new Date(Date.now() + offsetDays * 86_400_000);
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(now);
}

type Period = { amount: number; priceVersionNumber: number | null; periodStart: string; kind: string };

test.describe('subscriptions (R-SUB-01..05, E5, E3 subscription part)', () => {
  test('E5: a future plan price never rewrites a recorded period; the renewal takes it', async ({
    page,
    context,
  }) => {
    const suffix = Date.now().toString(36);
    const planName = `E2E quarterly ${suffix}`;
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);

    // SuperAdmin creates and publishes a plan with its first price (all plan data comes from the form).
    await page.goto('/en/admin/subscription-plans');
    await expect(page.getByRole('link', { name: 'New plan' })).toBeVisible();
    await expectNoSeriousAxeViolations(page);
    await page.getByRole('link', { name: 'New plan' }).click();
    const form = page.getByTestId('plan-form');
    await form.getByLabel('Plan name in Arabic').fill(`باقة ربع سنوية ${suffix}`);
    await form.getByLabel('Plan name in English').fill(planName);
    await form.getByLabel('Billing length').fill('3');
    await form.getByRole('button', { name: 'Add feature' }).click();
    await form.getByLabel('Feature 1 in Arabic').fill('ظهور في البحث');
    await form.getByLabel('Feature 1 in English').fill('Listed in search');
    await form.getByLabel(/^Price \(SAR\)/).fill('300');
    await form.getByRole('button', { name: 'Create plan' }).click();
    await expect(page).toHaveURL(/\/en\/admin\/subscription-plans\/[0-9a-f-]{36}$/);
    const planId = page.url().split('/').pop()!;
    await page.getByRole('button', { name: 'Publish' }).click();
    await expect(page.getByText('Plan updated.')).toBeVisible();
    await expect(page.getByTestId('plan-prices')).toContainText('Version 1: SAR 300');
    await expectNoSeriousAxeViolations(page);

    // A new shop (no subscription yet) is activated on the plan.
    const created = await page.request.post('/api/v1/admin/shops', {
      headers: { 'X-CSRF-Token': await csrf(context, page.request) },
      data: {
        slug: `e2e-sub-${suffix}`,
        nameAr: `محل اشتراك ${suffix}`,
        nameEn: `Subscription shop ${suffix}`,
      },
    });
    expect(created.status()).toBe(201);
    const shopId = (await created.json()).id as string;
    await page.goto(`/en/admin/shops/${shopId}?tab=subscription`);
    await expect(page.getByTestId('shop-subscription')).toContainText('No subscription');
    const record = page.getByTestId('record-period-form');
    await record.getByLabel('Plan', { exact: true }).selectOption({ label: planName });
    await expect(record).toContainText('Plan period (3 months)');
    await expect(record).toContainText('Recorded price: SAR 300 (version 1)');
    await record.getByLabel('Notes').fill('Contract E2E');
    await record.getByRole('button', { name: 'Record activation' }).click();
    await expect(page.getByText(/Subscription active until/)).toBeVisible();
    await expect(page.getByTestId('shop-subscription')).toContainText('Active');
    await expectNoSeriousAxeViolations(page);

    // SuperAdmin schedules a new price version after the current period ends.
    const nextVersionDate = riyadhDay(10);
    await page.goto(`/en/admin/subscription-plans/${planId}`);
    const priceForm = page.getByTestId('plan-price-form');
    await priceForm.getByLabel('New price (SAR)').fill('450');
    await priceForm.getByLabel('Effective from').fill(nextVersionDate);
    await priceForm.getByRole('button', { name: 'Add price version' }).click();
    await expect(page.getByText('Price version added.')).toBeVisible();
    await expect(page.getByTestId('plan-prices')).toContainText('Version 2: SAR 450');
    await expect(page.getByTestId('plan-prices')).toContainText('Scheduled');

    // The recorded period is unchanged: same amount and price version.
    let subscription = await (await page.request.get(`/api/v1/admin/shops/${shopId}/subscription`)).json();
    expect(subscription.periods).toHaveLength(1);
    expect(subscription.periods[0]).toMatchObject({ amount: 300, priceVersionNumber: 1, kind: 'Assigned' });
    await page.goto(`/en/admin/shops/${shopId}?tab=subscription`);
    await expect(page.getByTestId('subscription-history')).toContainText('SAR 300');
    await expect(page.getByTestId('subscription-history')).toContainText('price version 1');

    // The renewal starts after the current end (later than the new version's date) and records the new price.
    await page.getByTestId('record-period-form').getByRole('button', { name: 'Confirm renewal' }).click();
    await expect(page.getByText(/Renewed until/)).toBeVisible();
    subscription = await (await page.request.get(`/api/v1/admin/shops/${shopId}/subscription`)).json();
    const periods = subscription.periods as Period[];
    expect(periods).toHaveLength(2);
    expect(periods.find((p) => p.kind === 'Renewed')).toMatchObject({ amount: 450, priceVersionNumber: 2 });
    expect(periods.find((p) => p.kind === 'Assigned')).toMatchObject({ amount: 300, priceVersionNumber: 1 });

    // Wait for the refreshed page (new version) before the next write; an older version is a real 409.
    await expect(page.getByTestId('subscription-history')).toContainText('Renewal');

    // E3: suspend with a reason and reinstate; SuperAdmin override with a reason; all in the history.
    await page.getByRole('button', { name: 'Suspend subscription' }).click();
    await page.getByRole('dialog').getByLabel('Reason').fill('Contract under review');
    await page.getByRole('dialog').getByRole('button', { name: 'Suspend subscription' }).click();
    await expect(page.getByText('Subscription suspended.')).toBeVisible();
    await expect(page.getByTestId('shop-subscription')).toContainText('Suspended');
    await expect(page.getByRole('button', { name: 'Reinstate subscription' })).toBeVisible();
    await page.getByRole('button', { name: 'Reinstate subscription' }).click();
    await expect(page.getByText('Subscription reinstated.')).toBeVisible();
    // Wait for the refreshed state (new version) before the next write; an older version is a real 409.
    await expect(page.getByRole('button', { name: 'Suspend subscription' })).toBeVisible();

    const override = page.getByTestId('override-form');
    await override.getByLabel(/Price for this period/).fill('250');
    await override.getByLabel('Reason for the override').fill('Launch discount agreed');
    await override.getByRole('button', { name: 'Save override' }).click();
    await expect(page.getByText('Override saved.')).toBeVisible();
    await expect(page.getByTestId('subscription-history')).toContainText(
      'Override: price SAR 300 to SAR 250',
    );
    await expect(page.getByTestId('subscription-history')).toContainText('Launch discount agreed');

    // D-081: a custom length is a SuperAdmin override with an explicit total and a reason, kept in the history.
    const record2 = page.getByTestId('record-period-form');
    await record2.getByText('Custom number of days').click();
    await record2.getByLabel('Number of days', { exact: true }).fill('45');
    await expect(record2.getByTestId('custom-pricing')).toBeVisible();
    await record2.getByLabel('Total price for this period (SAR)').fill('200');
    await record2.getByLabel('Reason for the custom period').fill('Extended trial agreed');
    await record2.getByRole('button', { name: 'Confirm renewal' }).click();
    await expect(page.getByText(/Renewed until/)).toBeVisible();
    await expect(page.getByTestId('subscription-history')).toContainText(
      'SuperAdmin custom price (Extended trial agreed; plan price SAR 450)',
    );
    subscription = await (await page.request.get(`/api/v1/admin/shops/${shopId}/subscription`)).json();
    expect(subscription.periods[0]).toMatchObject({
      amount: 200,
      standardAmount: 450,
      pricingReason: 'Extended trial agreed',
    });

    // Leave the catalogue tidy: retire the test plan (its history stays).
    const archived = await page.request.post(`/api/v1/admin/subscription-plans/${planId}/archive`, {
      headers: { 'X-CSRF-Token': await csrf(context, page.request) },
    });
    expect(archived.status()).toBe(200);
  });

  test('E3: admins see every status; shops see their own subscription and warning', async ({ browser }) => {
    const adminContext = await browser.newContext();
    const admin = await adminContext.newPage();
    await staffSignIn(admin, ADMIN_EMAIL, ADMIN_PASSWORD);
    await admin.goto('/en/admin/subscriptions');
    const kpis = admin.getByTestId('subscription-kpis');
    for (const label of ['Active', 'Expiring soon', 'Expired', 'Suspended', 'No subscription'])
      await expect(kpis).toContainText(label);
    await expectNoSeriousAxeViolations(admin);

    await admin.getByLabel('Status', { exact: true }).selectOption('Expired');
    await admin.getByRole('button', { name: 'Search' }).click();
    await expect(admin).toHaveURL(/status=Expired/);
    await expect(admin.getByRole('table')).toContainText('Lamsat Al Rajul');
    await expect(admin.getByRole('table')).not.toContainText('Al Asala Barbershop');
    await admin.getByLabel('Status', { exact: true }).selectOption('Suspended');
    await admin.getByRole('button', { name: 'Search' }).click();
    await expect(admin.getByRole('table')).toContainText('Al Madina Barbers');

    // Arabic, RTL.
    await admin.goto('/ar/admin/subscriptions?status=ExpiringSoon');
    await expect(admin.getByRole('table')).toContainText('باربر هاوس');
    await expectNoSeriousAxeViolations(admin);
    await adminContext.close();

    // Barber House is expiring soon: the shop sees the warning; Al Asala is active with its renewal history.
    const shopContext = await browser.newContext();
    const owner = await shopContext.newPage();
    await staffSignIn(owner, 'owner@barber-house.trimme.local', DEMO_PASSWORD);
    await owner.goto('/en/shop/subscription');
    await expect(owner.getByText(/Your subscription ends in \d+ days?/)).toBeVisible();
    await expect(owner.getByTestId('shop-subscription-card')).toContainText('Semi-annual');
    await expect(owner.getByTestId('shop-subscription-card')).toContainText('Expiring soon');
    await expectNoSeriousAxeViolations(owner);
    const forbidden = await owner.request.get('/api/v1/admin/subscription-plans');
    expect(forbidden.status()).toBe(403);
    await shopContext.close();

    const asalaContext = await browser.newContext();
    const asala = await asalaContext.newPage();
    await staffSignIn(asala, 'owner@al-asala.trimme.local', DEMO_PASSWORD);
    await asala.goto('/ar/shop/subscription');
    await expect(asala.getByTestId('shop-subscription-card')).toContainText('سنوي');
    await expect(asala.getByTestId('shop-renewals').locator('li')).toHaveCount(2);
    await expect(asala.getByText(/ينتهي اشتراكك|انتهى اشتراكك|اشتراكك موقوف/)).toHaveCount(0);
    await expectNoSeriousAxeViolations(asala);
    await asalaContext.close();
  });

  test('platform settings: validated, saved with the version, and restored', async ({ page }) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/en/admin/settings');
    const form = page.getByTestId('platform-settings-form');
    await expect(form.getByLabel('Time zone')).toHaveValue('Asia/Riyadh');
    await expectNoSeriousAxeViolations(page);

    const reminder = form.getByLabel('Reminder before the appointment (minutes)');
    await reminder.fill('2');
    await form.getByRole('button', { name: 'Save settings' }).click();
    await expect(form.getByText('This value is out of range')).toBeVisible();

    await reminder.fill('35');
    await form.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('Settings saved.')).toBeVisible();
    expect((await (await page.request.get('/api/v1/admin/settings')).json()).reminderOffsetMinutes).toBe(35);

    // Restore the default (wait for the refreshed version first).
    await page.reload();
    await form.getByLabel('Reminder before the appointment (minutes)').fill('30');
    await form.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('Settings saved.')).toBeVisible();
    expect((await (await page.request.get('/api/v1/admin/settings')).json()).reminderOffsetMinutes).toBe(30);
  });
});
