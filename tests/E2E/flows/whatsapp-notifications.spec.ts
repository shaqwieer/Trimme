import AxeBuilder from '@axe-core/playwright';
import { type APIRequestContext, type BrowserContext, expect, type Page, test } from '@playwright/test';

/**
 * Phase 15, flow E4 (R-NTF-02/03/04/06/07, R-AD-10, R-SD-08): an admin edits the separate Arabic customer and
 * professional confirmation templates; a customer's new booking produces both dispatches through the fake provider with
 * the new wording and both 30-minute reminder jobs; the admin pages show masked numbers only; the shop sees the booking
 * in its notifications; cancelling cancels both reminders. One OTP sign-in (the customer). The templates are restored at
 * the end, so later runs start from the same wording.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const AL_ASALA = { slug: 'al-asala', owner: 'owner@al-asala.trimme.local' };
const RAKAN = { id: '0199a0de-5a10-7000-8000-000000000103', whatsApp: '500100103' };

const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;

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
  await request.get('/api/v1/auth/csrf');
  return decodeURIComponent((await context.cookies()).find((c) => c.name === 'trimme-csrf')!.value);
}

/** YYYY-MM-DD in Riyadh, `days` from today. */
function riyadhDate(days: number): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
  const [y, m, d] = today.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** Signs a new customer up through the API (OTP from the development inbox) and completes the profile in Arabic. */
async function signUpCustomer(context: BrowserContext, page: Page, name: string): Promise<string> {
  const national = newPhone();
  const phone = `+966${national}`;
  await page.goto('/ar');
  let token = await csrf(context, page.request);
  const requested = await page.request.post('/api/v1/auth/otp/request', {
    headers: { 'X-CSRF-Token': token },
    data: { phone, termsAccepted: true, locale: 'ar' },
  });
  expect(requested.status()).toBe(202);
  const { challengeId } = (await requested.json()) as { challengeId: string };
  const code = (
    (await (
      await page.request.get(`/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(phone)}`)
    ).json()) as {
      code: string;
    }
  ).code;
  expect(
    (
      await page.request.post('/api/v1/auth/otp/verify', {
        headers: { 'X-CSRF-Token': token },
        data: { challengeId, code },
      })
    ).ok(),
  ).toBeTruthy();
  token = await csrf(context, page.request);
  const completed = await page.request.post('/api/v1/auth/profile/complete', {
    headers: { 'X-CSRF-Token': token },
    data: { displayName: name, preferredLocale: 'ar', termsAccepted: true },
  });
  expect(completed.ok()).toBeTruthy();
  return national;
}

/** Books Rakan at Al Asala six days ahead on the first free time he offers (retrying if another run takes it). */
async function bookRakan(
  context: BrowserContext,
  page: Page,
): Promise<{ id: string; startsAt: string; version: number }> {
  const services = (await (
    await page.request.get(`/api/v1/public/shops/${AL_ASALA.slug}/services`)
  ).json()) as Array<{
    id: string;
    onlineBookable: boolean;
    professionalIds: string[];
  }>;
  const service = services.find((s) => s.onlineBookable && s.professionalIds.includes(RAKAN.id))!;
  const token = await csrf(context, page.request);
  for (const days of [6, 7, 8]) {
    const slots = (await (
      await page.request.get(
        `/api/v1/public/shops/${AL_ASALA.slug}/availability/slots?serviceId=${service.id}&professionalId=${RAKAN.id}&date=${riyadhDate(days)}`,
      )
    ).json()) as { slots: Array<{ startsAt: string }> };
    for (const slot of slots.slots.slice(-3)) {
      const created = await page.request.post('/api/v1/bookings', {
        headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() },
        data: {
          shopSlug: AL_ASALA.slug,
          serviceId: service.id,
          professionalId: RAKAN.id,
          startsAt: slot.startsAt,
        },
      });
      if (created.status() === 201) {
        const booking = (await created.json()) as { id: string; startsAt: string; version: number };
        return booking;
      }
    }
  }
  throw new Error('Rakan has no free time six to eight days ahead.');
}

test.describe('WhatsApp templates, dispatches and reminders (Phase 15, E4)', () => {
  test('separate customer and professional templates render a new booking, schedule both reminders and leak no number', async ({
    browser,
  }) => {
    test.setTimeout(180_000);
    const marker = `E4-${Date.now().toString(36)}`;
    const admin = await browser.newContext();
    const page = await admin.newPage();
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);

    // 1. The admin edits the Arabic "booking confirmed" template for the customer and, separately, for the professional.
    const edited: Record<'Customer' | 'Professional', string> = { Customer: '', Professional: '' };
    for (const audience of ['Customer', 'Professional'] as const) {
      await page.goto('/en/admin/whatsapp/templates');
      const slot = page
        .getByTestId('template-list')
        .locator('section')
        .filter({ hasText: 'Booking confirmed' })
        .locator(`li[data-audience="${audience}"][data-locale="ar"]`);
      await slot.getByRole('link', { name: /Edit/ }).click();
      await expect(page).toHaveURL(/\/en\/admin\/whatsapp\/templates\/[0-9a-f-]+$/);
      edited[audience] = page.url();
      const text = page.getByLabel('Text', { exact: true });
      await text.fill(`${marker} ${audience === 'Customer' ? 'العميل' : 'الحلاق'}: `);
      await page.getByRole('button', { name: /Customer name/ }).click();
      await expect(text).toHaveValue(new RegExp(`${marker} .+: \\{\\{customer_name\\}\\}`));
      await expect(page.getByTestId('bubble-preview')).toContainText(`${marker}`);
      await expect(page.getByTestId('bubble-preview')).toContainText('سارة العتيبي');
      if (audience === 'Customer') {
        // A professional template cannot use the manage-booking link; the customer's can.
        await expect(page.getByRole('button', { name: /Manage-booking link/ })).toBeVisible();
        await expectNoSeriousAxeViolations(page);
      } else {
        await expect(page.getByRole('button', { name: /Manage-booking link/ })).toHaveCount(0);
      }
      await page.getByRole('button', { name: 'Activate' }).click();
      await page.getByRole('alertdialog').getByRole('button', { name: 'Activate' }).click();
      await expect(page.getByText(/Version \d+ is now active\./)).toBeVisible();
    }

    // 2. A new customer books Rakan (notifications on, fake number) through the API.
    const customer = await browser.newContext();
    const customerPage = await customer.newPage();
    const customerName = `عميل ${marker}`;
    const national = await signUpCustomer(customer, customerPage, customerName);
    const booking = await bookRakan(customer, customerPage);

    // 3. The admin booking page shows both dispatches (delivered by the fake provider) and both reminders.
    await page.goto(`/en/admin/bookings/${booking.id}`);
    const section = page.getByTestId('booking-whatsapp');
    await expect(async () => {
      await page.reload();
      await expect(
        section.locator('li[data-event="BookingConfirmed"][data-audience="Customer"]'),
      ).toContainText('Delivered');
      await expect(
        section.locator('li[data-event="BookingConfirmed"][data-audience="Professional"]'),
      ).toContainText('Delivered');
    }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 3_000] });
    const reminders = page.getByTestId('booking-reminders');
    await expect(reminders.locator('li[data-status="Scheduled"]')).toHaveCount(2);
    await expect(reminders.locator('li[data-audience="Customer"]')).toBeVisible();
    await expect(reminders.locator('li[data-audience="Professional"]')).toBeVisible();
    const due = new Intl.DateTimeFormat('en-GB', {
      hour: 'numeric',
      minute: '2-digit',
      hour12: true,
      timeZone: 'Asia/Riyadh',
    }).format(new Date(new Date(booking.startsAt).getTime() - 30 * 60_000));
    await expect(reminders.locator('li').first()).toContainText(due);
    // The booking page lists messages without recipients; the detail page shows the masked number only.
    const pageText = await page.locator('main').innerText();
    expect(pageText).not.toContain(national);
    expect(pageText).not.toContain(RAKAN.whatsApp);
    await expectNoSeriousAxeViolations(page);

    // 4. Each message was rendered from its own new template version; neither carries the customer's number.
    await section.locator('li[data-audience="Customer"]').getByRole('link').click();
    await expect(page.getByTestId('bubble-preview')).toContainText(`${marker} العميل: ${customerName}`);
    await expect(page.getByTestId('bubble-preview')).not.toContainText(national);
    await expectNoSeriousAxeViolations(page);
    await page.goBack();
    await section.locator('li[data-audience="Professional"]').getByRole('link').click();
    await expect(page.getByTestId('bubble-preview')).toContainText(`${marker} الحلاق: ${customerName}`);
    await expect(page.locator('main')).toContainText('+966 5•• ••• •03');
    await expect(page.getByTestId('bubble-preview')).not.toContainText(national);

    // 5. The dispatch log lists them with masked recipients and the delivery rate.
    await page.goto(`/en/admin/whatsapp/dispatches?bookingId=${booking.id}`);
    await expect(page.getByTestId('dispatch-stats')).toContainText('delivery rate');
    await expect(page.getByRole('link', { name: 'Booking confirmed' }).first()).toBeVisible();
    expect(await page.locator('main').innerText()).not.toContain(national);
    await expectNoSeriousAxeViolations(page);

    // 6. The shop sees the booking in its notifications (no phone number).
    const shop = await browser.newContext();
    const shopPage = await shop.newPage();
    await staffSignIn(shopPage, AL_ASALA.owner, DEMO_PASSWORD);
    await shopPage.goto('/en/shop/notifications');
    await expect(shopPage.getByTestId('notification').filter({ hasText: customerName })).toBeVisible();
    await expect(shopPage.getByTestId('notification-bell')).toHaveAccessibleName(/Notifications/);
    expect(await shopPage.locator('main').innerText()).not.toContain(national);
    await expectNoSeriousAxeViolations(shopPage);
    await shop.close();

    // 7. The customer cancels: both reminders are cancelled and the cancellation messages go out.
    const token = await csrf(customer, customerPage.request);
    const current = (await (await customerPage.request.get(`/api/v1/me/bookings/${booking.id}`)).json()) as {
      version: number;
    };
    const cancelled = await customerPage.request.post(`/api/v1/me/bookings/${booking.id}/cancel`, {
      headers: { 'X-CSRF-Token': token },
      data: { reason: 'E4 clean-up', version: current.version },
    });
    expect(cancelled.ok()).toBeTruthy();
    await customer.close();
    await page.goto(`/en/admin/bookings/${booking.id}`);
    await expect(async () => {
      await page.reload();
      await expect(page.getByTestId('booking-reminders').locator('li[data-status="Cancelled"]')).toHaveCount(
        2,
      );
      await expect(
        page.getByTestId('booking-whatsapp').locator('li[data-event="BookingCancelled"]'),
      ).toHaveCount(2);
    }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 3_000] });

    // 8. Restore the previous wording of both templates (a new version from the archived one, activated).
    for (const audience of ['Customer', 'Professional'] as const) {
      await page.goto(edited[audience]);
      const history = page.getByTestId('template-history');
      await history
        .locator('li[data-status="Archived"]')
        .filter({ hasNotText: /E4-/ })
        .first()
        .getByRole('button', { name: 'Restore to draft' })
        .click();
      await expect(page.getByText('The text was copied into the draft.')).toBeVisible();
      await page.getByRole('button', { name: 'Activate' }).click();
      await page.getByRole('alertdialog').getByRole('button', { name: 'Activate' }).click();
      await expect(page.getByText(/Version \d+ is now active\./)).toBeVisible();
      await expect(page.getByLabel('Text', { exact: true })).not.toHaveValue(new RegExp(marker));
    }

    // 9. The operations dashboard is reachable through the web origin for this admin (read-only), with the recurring
    //    jobs registered; an anonymous visitor is refused.
    await page.goto('/api/ops/jobs/recurring');
    await expect(page.getByText('outbox-maintenance')).toBeVisible();
    await expect(page.getByText('notifications-sweep')).toBeVisible();
    await expect(page.getByText('subscription-expiry')).toBeVisible();
    const anonymous = await browser.newContext();
    const refused = await (await anonymous.newPage()).goto('/api/ops/jobs');
    expect([401, 403]).toContain(refused?.status());
    await anonymous.close();
    await admin.close();
  });
});
