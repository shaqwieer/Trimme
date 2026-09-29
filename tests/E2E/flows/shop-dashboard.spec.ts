import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';

/**
 * Flow E2 (spec §19) — the shop's day on TRIMME (Phase 13): the overview, a walk-in seen live by a second staff screen,
 * the calendar, the appointment drawer (note, cancel with a reason), no access to another shop's bookings, no customer
 * phone anywhere in the dashboard's API responses or live messages, and no export (R-NEG-03).
 * Barber House with Omar, two days ahead: the customer flows use him 3–13 days ahead, Majed is on seeded time off, and
 * Al Asala's Faisal is kept for the schedule flow. Each run cancels its walk-in, so reruns find free time again.
 */
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const OWNER = 'owner@barber-house.trimme.local';
const STAFF = 'staff@barber-house.trimme.local';
const OMAR = '0199a0de-5a10-7000-8000-000000000201';
const AL_ASALA_BOOKING = '0199a0de-5a10-7000-8000-000000000a12';
const CUSTOMER_PHONES = ['500100301', '500100302', '500100303'];

async function staffSignIn(page: Page, email: string) {
  await page.goto('/ar/auth/staff/sign-in');
  await page.getByLabel('البريد الإلكتروني').fill(email);
  await page.getByLabel('كلمة المرور').fill(DEMO_PASSWORD);
  await page.getByRole('button', { name: 'تسجيل الدخول' }).click();
  await expect(page).toHaveURL(/\/ar\/shop$/);
}

async function expectNoSeriousAxe(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    blocking.map(
      (v) => `${label} ${v.id}: ${v.help} @ ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`,
    ),
  ).toEqual([]);
}

/** Every dashboard API response body and every live-connection message the page sees. */
function recordTraffic(page: Page) {
  const seen: Array<{ source: string; body: string }> = [];
  page.on('response', async (response) => {
    const url = response.url();
    if (!/\/api\/v1\/shop\/(bookings|dashboard|calendar|availability)|\/hubs\/operations/.test(url)) return;
    try {
      seen.push({ source: url, body: await response.text() });
    } catch {
      // Streaming transports (SSE) have no final body; their messages are covered below or by long polling.
    }
  });
  page.on('websocket', (socket) => {
    socket.on('framereceived', (frame) =>
      seen.push({ source: `ws ${socket.url()}`, body: String(frame.payload) }),
    );
  });
  return seen;
}

function expectNoPhones(traffic: Array<{ source: string; body: string }>) {
  expect(traffic.length).toBeGreaterThan(0);
  for (const { source, body } of traffic) {
    for (const phone of CUSTOMER_PHONES) expect(body, source).not.toContain(phone);
    expect(body, source).not.toMatch(/"(customer)?(phone|mobile)[A-Za-z]*"\s*:/i);
    expect(body, source).not.toMatch(/"customerId"\s*:/);
  }
}

/** YYYY-MM-DD in Riyadh, `days` from today. */
function riyadhDate(days: number): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
  const [y, m, d] = today.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

test.describe('shop operations dashboard (E2, Phase 13)', () => {
  test('E2: a walk-in appears live on another screen, in the calendar and the drawer; cancelled with a reason; no phones, no export', async ({
    page,
    browser,
  }) => {
    const traffic = recordTraffic(page);
    const customer = `زائر ${Date.now().toString(36).slice(-5)}`;
    await staffSignIn(page, OWNER);
    await expect(page.getByTestId('shop-overview')).toBeVisible();
    await expect(page.getByTestId('shop-kpis')).toBeVisible();
    await expect(page.getByTestId('live-indicator')).toHaveAttribute('data-state', 'live', {
      timeout: 15_000,
    });
    await expectNoSeriousAxe(page, 'overview');

    // A second screen (staff) watching the appointments two days ahead.
    const date = riyadhDate(2);
    const watcherContext = await browser.newContext();
    const watcher = await watcherContext.newPage();
    const watcherTraffic = recordTraffic(watcher);
    await staffSignIn(watcher, STAFF);
    await watcher.goto(`/ar/shop/appointments?from=${date}&to=${date}&professional=${OMAR}`);
    await expect(watcher.getByTestId('live-indicator')).toHaveAttribute('data-state', 'live', {
      timeout: 15_000,
    });
    const rowsBefore = await watcher.getByTestId('appointment-row').count();

    // Walk-in at the desk: service, barber, first free time that day, the name only (no phone field).
    await page.goto('/ar/shop/walk-in');
    await expect(page.getByTestId('walk-in')).toBeVisible();
    await expect(page.locator('input[type="tel"]')).toHaveCount(0);
    await page.locator('label').filter({ hasText: 'قص وتصفيف' }).click();
    await page.getByLabel('اليوم', { exact: true }).fill(date);
    await page.locator('label').filter({ hasText: 'عمر السالم' }).click();
    const time = page.getByTestId('walk-in-times').getByRole('radio').first();
    await expect(time).toBeAttached();
    await page.locator('[data-testid="walk-in-times"] label').first().click();
    await page.getByLabel('اسم العميل').fill(customer);
    await expectNoSeriousAxe(page, 'walk-in');
    await page.getByRole('button', { name: 'تسجيل الحجز' }).click();
    await expect(page.getByText('سُجل الحجز الحضوري')).toBeVisible();

    // The other screen shows it without a reload (live, R-SD-10).
    await expect(watcher.getByTestId('appointment-row')).toHaveCount(rowsBefore + 1, { timeout: 15_000 });
    await expect(watcher.getByTestId('appointments-board')).toContainText(customer);
    await expectNoSeriousAxe(watcher, 'appointments');

    // The drawer: details without a phone, an internal note, then cancel with a reason.
    await page.getByRole('link', { name: 'عرض الموعد' }).click();
    const drawer = page.getByTestId('appointment-drawer');
    await expect(drawer).toContainText(customer);
    await expect(drawer.getByTestId('no-phone-note')).toBeVisible();
    await expectNoSeriousAxe(page, 'drawer');
    await drawer.getByLabel('ملاحظة جديدة').fill('يفضّل المقص');
    await drawer.getByRole('button', { name: 'إضافة الملاحظة' }).click();
    await expect(drawer).toContainText('يفضّل المقص');
    const bookingId = new URL(page.url()).searchParams.get('booking')!;

    // The calendar shows it in Majed's column that day; the week view has the density strip.
    const calendar = await page.context().newPage();
    const calendarTraffic = recordTraffic(calendar);
    await calendar.goto(`/ar/shop/calendar?date=${date}&professional=${OMAR}`);
    await expect(calendar.getByRole('button', { name: new RegExp(customer) })).toBeVisible();
    await expectNoSeriousAxe(calendar, 'calendar day');
    await calendar
      .locator('label')
      .filter({ hasText: /^أسبوع$/ })
      .click();
    await expect(calendar.getByTestId('calendar-density')).toBeVisible();
    await expectNoSeriousAxe(calendar, 'calendar week');
    await calendar.close();

    await drawer.getByRole('button', { name: 'إلغاء الموعد' }).click();
    const dialog = page.getByRole('dialog', { name: 'إلغاء الموعد؟' });
    await dialog.getByLabel('سبب الإلغاء').fill('طلب العميل تأجيله');
    await dialog.getByRole('button', { name: 'نعم، ألغِ الموعد' }).click();
    await expect(drawer).toContainText('ألغاه المحل');
    await expect(watcher.getByTestId('appointments-board')).toContainText('ألغاه المحل', { timeout: 15_000 });

    // Another shop's booking is simply not found (tenant from the session, R-TEN-06).
    const foreign = await page.request.get(`/api/v1/shop/bookings/${AL_ASALA_BOOKING}`);
    expect(foreign.status()).toBe(404);
    expect((await page.request.get(`/api/v1/shop/bookings/${bookingId}`)).status()).toBe(200);

    // No customer phone in any dashboard payload or live message; no export anywhere (R-NEG-03).
    expectNoPhones([...traffic, ...watcherTraffic, ...calendarTraffic]);
    for (const path of ['/ar/shop', '/ar/shop/appointments', '/ar/shop/calendar']) {
      await page.goto(path);
      await expect(page.getByText(/تصدير|Export|CSV/)).toHaveCount(0);
    }
    await watcherContext.close();
  });

  test('dashboard pages fit phone, tablet and desktop widths', async ({ page }, testInfo) => {
    await staffSignIn(page, OWNER);
    for (const width of [390, 768, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      for (const path of ['/ar/shop', '/ar/shop/calendar', '/ar/shop/appointments', '/ar/shop/walk-in']) {
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
