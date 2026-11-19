import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * Flow E2 (spec §19) — the shop's day on TRIMME (Phase 13): the overview, a walk-in made by a barber and seen live on
 * the owner's screen, the calendar, the appointment drawer (note, cancel with a reason), all with Omar's own login,
 * signed in with his mobile (D-145); no access to another shop's bookings, no customer phone anywhere in the dashboard's
 * API responses or live messages (not even the mobile the barber typed for the walk-in, D-134), and no export (R-NEG-03).
 * Barber House with Omar, two days ahead: the customer flows use him 3–13 days ahead, Majed is on seeded time off, and
 * Al Asala's Faisal is kept for the schedule flow. Each run cancels its walk-in, so reruns find free time again.
 */
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const OWNER = 'owner@barber-house.trimme.local';
/** Omar's own login: the WhatsApp number on his profile, as he types it (D-145). */
const OMAR_LOGIN = '0500100201';
const OMAR = '0199a0de-5a10-7000-8000-000000000201';
const AL_ASALA_BOOKING = '0199a0de-5a10-7000-8000-000000000a12';
/** The mobile the barber takes for the walk-in (D-134): it goes to the platform team only. */
const WALK_IN_PHONE = '551000777';
const CUSTOMER_PHONES = ['500100301', '500100302', '500100303', WALK_IN_PHONE];

async function staffSignIn(page: Page, email: string) {
  await page.goto('/ar/auth/staff/sign-in');
  await page.getByLabel('البريد الإلكتروني').fill(email);
  await page.getByLabel('كلمة المرور').fill(DEMO_PASSWORD);
  await page.getByRole('button', { name: 'تسجيل الدخول' }).click();
  await expect(page).toHaveURL(/\/ar\/shop$/);
}

async function expectNoSeriousAxe(page: Page, label: string) {
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

/**
 * No customer number in what the page saw. The one reviewed exception (D-147) is a barber's own view of an open
 * walk-in or phone booking, so this checks the owner's screens and the calendar: the field may exist, always null.
 */
function expectNoPhones(traffic: Array<{ source: string; body: string }>) {
  expect(traffic.length).toBeGreaterThan(0);
  for (const { source, body } of traffic) {
    for (const phone of CUSTOMER_PHONES) expect(body, source).not.toContain(phone);
    expect(body, source).not.toMatch(/"(customer)?(phone|mobile)[A-Za-z]*"\s*:\s*"/i);
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
  test("E2: a phone booking appears live on the owner's screen, in the calendar and the drawer; cancelled with a reason; the number for the barber only; no export", async ({
    page,
    browser,
  }) => {
    const traffic = recordTraffic(page);
    const customer = `زائر ${Date.now().toString(36).slice(-5)}`;
    await staffSignIn(page, OMAR_LOGIN);
    await expect(page.getByTestId('shop-overview')).toBeVisible();
    // A barber's home is their day, no figures (D-147, D-151): the quick buttons, «جدولي» pressed.
    await expect(page.getByTestId('shop-kpis')).toHaveCount(0);
    await expect(page.getByTestId('quick-actions')).toBeVisible();
    await expect(page.getByRole('button', { name: 'جدولي' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('live-indicator')).toHaveAttribute('data-state', 'live', {
      timeout: 15_000,
    });
    await expectNoSeriousAxe(page, 'overview');

    // A second screen (the owner) watching the appointments (the simple list, D-136), one day at a time (D-151).
    const date = riyadhDate(2);
    const watcherContext = await browser.newContext();
    const watcher = await watcherContext.newPage();
    const watcherTraffic = recordTraffic(watcher);
    await staffSignIn(watcher, OWNER);
    await watcher.goto('/ar/shop/appointments');
    await expect(watcher.getByTestId('live-indicator')).toHaveAttribute('data-state', 'live', {
      timeout: 15_000,
    });
    await expect(watcher.getByTestId('coming-today')).toBeVisible();
    await expect(watcher.getByTestId('quick-actions')).toHaveCount(0);
    // The phone booking will be two days ahead: the owner moves to that day.
    await watcher.getByRole('button', { name: 'اليوم التالي' }).click();
    await watcher.getByRole('button', { name: 'اليوم التالي' }).click();
    const watched = watcher.getByTestId('coming-appointment').filter({ hasText: customer });
    await expect(watched).toHaveCount(0);

    // Omar's phone booking two days ahead (D-147): the kind, the service, himself, the last free time that day, the
    // name and the mobile (D-134).
    await page.goto('/ar/shop/walk-in');
    await expect(page.getByTestId('walk-in')).toBeVisible();
    await page.locator('label').filter({ hasText: 'بالتلفون' }).click();
    // The first service is ticked by default, and services.spec may have reordered the section (D-151): untick
    // anything but «قص وتصفيف», which both barbers do. A label click toggles, so «قص وتصفيف» is ticked only if it isn't.
    for (const box of await page
      .getByRole('group', { name: /الخدمات/ })
      .getByRole('checkbox', { checked: true })
      .all()) {
      const label = box.locator('xpath=ancestor::label[1]');
      if (!/قص وتصفيف/.test((await label.textContent()) ?? '')) await label.click();
    }
    if (!(await page.getByRole('checkbox', { name: /قص وتصفيف/ }).isChecked()))
      await page.locator('label').filter({ hasText: 'قص وتصفيف' }).click();
    await expect(page.getByRole('checkbox', { name: /قص وتصفيف/ })).toBeChecked();
    await page.getByLabel('اليوم', { exact: true }).fill(date);
    await page.locator('label').filter({ hasText: 'عمر السالم' }).click();
    // The day's last free time: the first ones can belong to the previous business day when its opening window runs
    // past midnight (D-100), and the calendar below shows the business day.
    const time = page.getByTestId('walk-in-times').getByRole('radio').last();
    await expect(time).toBeAttached();
    await page.locator('[data-testid="walk-in-times"] label').last().click();
    await page.getByLabel('اسم العميل').fill(customer);
    await page.getByLabel('جوال العميل').fill(`0${WALK_IN_PHONE}`);
    await expectNoSeriousAxe(page, 'walk-in');
    await page.getByRole('button', { name: 'تسجيل الموعد' }).click();
    await expect(page.getByText('سُجّل الموعد بالتلفون')).toBeVisible();
    await expect(page.locator('main')).not.toContainText(/551 ?000|٥٥١/);

    // The other screen shows it without a reload (live, R-SD-10).
    await expect(watched).toHaveCount(1, { timeout: 15_000 });
    await expectNoSeriousAxe(watcher, 'appointments');

    // The barber's drawer: the number he typed, while the booking is open (D-147), an internal note, then cancel with a
    // reason. The owner's drawer: no number.
    await page.getByRole('link', { name: 'عرض الموعد' }).click();
    const drawer = page.getByTestId('appointment-drawer');
    await expect(drawer).toContainText(customer);
    await expect(drawer.getByTestId('customer-phone')).toContainText(`+966${WALK_IN_PHONE}`);
    await watched.click();
    await expect(watcher.getByTestId('appointment-drawer')).toContainText(customer);
    await expect(watcher.getByTestId('appointment-drawer').getByTestId('customer-phone')).toHaveCount(0);
    await expect(
      watcher.getByTestId('appointment-drawer').getByRole('button', { name: 'إلغاء الموعد' }),
    ).toHaveCount(0);
    await watcher.keyboard.press('Escape');
    await expectNoSeriousAxe(page, 'drawer');
    await drawer.getByLabel('ملاحظة جديدة').fill('يفضّل المقص');
    await drawer.getByRole('button', { name: 'إضافة الملاحظة' }).click();
    await expect(drawer).toContainText('يفضّل المقص');
    const bookingId = new URL(page.url()).searchParams.get('booking')!;

    // The calendar shows it in Omar's column that day; the week view has the density strip.
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
    // The ready reasons (D-151, 2-7); «سبب ثاني» is typed.
    await dialog.locator('label').filter({ hasText: 'سبب ثاني' }).click();
    await dialog.getByLabel('اكتب السبب').fill('طلب العميل تأجيله');
    await dialog.getByRole('button', { name: 'نعم، ألغِ الموعد' }).click();
    await expect(drawer).toContainText('ألغاه الصالون');
    // The simple list has no cancellations: the walk-in leaves the other screen live.
    await expect(watched).toHaveCount(0, { timeout: 15_000 });

    // Another shop's booking is simply not found (tenant from the session, R-TEN-06).
    const foreign = await page.request.get(`/api/v1/shop/bookings/${AL_ASALA_BOOKING}`);
    expect(foreign.status()).toBe(404);
    expect((await page.request.get(`/api/v1/shop/bookings/${bookingId}`)).status()).toBe(200);

    // No customer phone in the owner's payloads, the calendar or any live message; no export anywhere (R-NEG-03).
    expectNoPhones([
      ...watcherTraffic,
      ...calendarTraffic,
      ...traffic.filter((t) => t.source.startsWith('ws ')),
    ]);
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
      for (const path of [
        '/ar/shop',
        '/ar/shop/calendar',
        '/ar/shop/appointments',
        // The owner has no walk-in page (D-146); their barbers' list instead.
        '/ar/shop/barbers',
        '/ar/shop/qr',
        // The owner's reports (D-147): wide tables scroll inside their card, never the page.
        '/ar/shop/reports',
      ]) {
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
