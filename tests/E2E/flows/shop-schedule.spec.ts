import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * Phase 09 flow (spec §19 E2, schedule part; R-AVL-02/03): the shop edits a professional's hours, records time off and
 * pauses online booking in /shop/schedule, and the public slot API follows each change. Staff see none of that: only
 * each barber's day by the hour, where they set breaks with no reason (D-134). Every change is undone, so the flow can
 * run again on the same stack. Demo data: Al Asala (DemoData), Faisal does the haircut, Sultan the beard trim.
 */
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const OWNER = 'owner@al-asala.trimme.local';
const STAFF = 'staff@al-asala.trimme.local';
const SLUG = 'al-asala';
const FAISAL = '0199a0de-5a10-7000-8000-000000000101';
const SULTAN = '0199a0de-5a10-7000-8000-000000000102';
const HAIRCUT = '0199a0de-5a10-7000-8000-000000000401';
const BEARD_TRIM = '0199a0de-5a10-7000-8000-000000000402';
/**
 * A save from a dialog is two API calls (conflict preview, then save) plus a page refresh. Under the parallel E2E load
 * one call alone has taken 3.4 s on this machine, so these waits allow 15 s instead of the default 5 s.
 */
const SAVE = { timeout: 15_000 };
const WEEK = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;

/** The next Sunday–Wednesday after today in Riyadh (Al Asala opens 09:00–23:00 on those days). */
function targetDate(): { iso: string; day: (typeof WEEK)[number] } {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
  const base = new Date(`${today}T12:00:00Z`);
  for (let offset = 1; offset <= 7; offset++) {
    const date = new Date(base.getTime() + offset * 86_400_000);
    const day = WEEK[date.getUTCDay()]!;
    if (['Sunday', 'Monday', 'Tuesday', 'Wednesday'].includes(day))
      return { iso: date.toISOString().slice(0, 10), day };
  }
  throw new Error('no target day');
}

async function staffSignIn(page: Page, email: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(DEMO_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/en\/shop$/);
}

async function csrf(context: BrowserContext, request: APIRequestContext): Promise<string> {
  const existing = (await context.cookies()).find((c) => c.name === 'trimme-csrf')?.value;
  if (existing) return existing;
  await request.get('/api/v1/auth/csrf');
  return (await context.cookies()).find((c) => c.name === 'trimme-csrf')!.value;
}

async function slots(request: APIRequestContext, date: string, professionalId?: string, serviceId = HAIRCUT) {
  const response = await request.get(
    `/api/v1/public/shops/${SLUG}/availability/slots?serviceId=${serviceId}&date=${date}${professionalId ? `&professionalId=${professionalId}` : ''}`,
  );
  expect(response.status()).toBe(200);
  return (await response.json()) as {
    bookable: boolean;
    blockedReason: string | null;
    slots: { localTime: string; professionalIds: string[] }[];
  };
}

/** Undo anything a failed earlier run left behind (pause, Faisal's own hours, Faisal's time off). */
async function resetSchedule(page: Page, context: BrowserContext) {
  const headers = { 'X-CSRF-Token': await csrf(context, page.request) };
  const schedule = await (await page.request.get('/api/v1/shop/schedule')).json();
  if (schedule.onlineBookingPaused)
    await page.request.post('/api/v1/shop/online-booking/resume', { headers });
  for (const entry of schedule.timeOff.filter((t: { professionalId: string }) => t.professionalId === FAISAL))
    await page.request.delete(`/api/v1/shop/schedule/time-off/${entry.id}`, { headers });
  const faisal = schedule.professionals.find((p: { professionalId: string }) => p.professionalId === FAISAL);
  if (!faisal.followsShopHours)
    await page.request.put(`/api/v1/shop/professionals/${FAISAL}/working-hours`, {
      headers,
      data: { followsShopHours: true, intervals: [], version: faisal.version },
    });
}

test.describe('shop schedule and availability (R-AVL-02/03, E2 schedule part)', () => {
  test('owner edits hours, time off and the pause; the public slots follow each change', async ({
    page,
    context,
  }) => {
    const target = targetDate();
    await staffSignIn(page, OWNER);
    await resetSchedule(page, context);

    const before = await slots(page.request, target.iso, FAISAL);
    expect(before.bookable).toBe(true);
    expect(before.slots.length).toBeGreaterThan(50);
    expect(before.slots.map((s) => s.localTime)).toContain('15:30'); // the demo sets no fixed breaks (D-134)

    await page.goto('/en/shop/schedule');
    await expect(page.getByTestId('opening-hours')).toBeVisible();
    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(
      axe.violations
        .filter((v) => v.impact === 'serious' || v.impact === 'critical')
        .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`),
    ).toEqual([]);

    // Faisal's own hours: 10:00–11:00 on the target weekday.
    const pros = page.getByTestId('professional-hours');
    await pros.getByLabel('Professional').selectOption(FAISAL);
    await pros.getByRole('switch', { name: "Follows the shop's hours" }).click();
    const row = pros.getByTestId(`pro-hours-${target.day}`);
    await row.getByLabel('From').fill('10:00');
    await row.getByLabel('To').fill('11:00');
    await pros.getByRole('button', { name: "Save professional's hours" }).click();
    await expect(pros.getByText("Professional's hours saved")).toBeVisible(SAVE);
    expect((await slots(page.request, target.iso, FAISAL)).slots.map((s) => s.localTime)).toEqual([
      '10:00',
      '10:05',
      '10:10',
      '10:15',
      '10:20',
      '10:25',
      '10:30',
    ]);

    // Back to the shop's hours.
    await pros.getByRole('switch', { name: "Follows the shop's hours" }).click();
    await pros.getByRole('button', { name: "Save professional's hours" }).click();
    await expect(pros.getByText("Professional's hours saved")).toBeVisible(SAVE);
    await expect
      .poll(async () => (await slots(page.request, target.iso, FAISAL)).slots.length)
      .toBe(before.slots.length);

    // Whole-day time off removes Faisal's day; deleting it brings the slots back.
    const timeOff = page.getByTestId('time-off');
    await timeOff.getByRole('button', { name: 'Record time off' }).click();
    const form = page.getByTestId('time-off-form');
    await form.getByLabel('Professional').selectOption(FAISAL);
    await form.getByLabel('From date').fill(target.iso);
    await form.getByLabel('To date').fill(target.iso);
    await form.getByRole('button', { name: 'Save' }).click();
    await expect(form).toBeHidden(SAVE);
    const entry = timeOff.locator('[data-testid^="time-off-"]').filter({ hasText: 'Faisal' });
    await expect(entry).toContainText('Scheduled');
    expect((await slots(page.request, target.iso, FAISAL)).slots).toEqual([]);
    await entry.getByRole('button', { name: /Delete/ }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete' }).click();
    await expect(entry).toBeHidden(SAVE);
    expect((await slots(page.request, target.iso, FAISAL)).slots.length).toBe(before.slots.length);

    // Pause: no slots and a reason; resume restores them.
    const pause = page.getByTestId('pause-card');
    await pause.getByRole('button', { name: 'Pause' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Pause now' }).click();
    await expect(pause).toHaveAttribute('data-paused', 'true', SAVE);
    const paused = await slots(page.request, target.iso);
    expect(paused.bookable).toBe(false);
    expect(paused.blockedReason).toBe('shop.paused');
    await pause.getByRole('button', { name: 'Resume online booking' }).click();
    await expect(pause).toHaveAttribute('data-paused', 'false', SAVE);
    expect((await slots(page.request, target.iso)).bookable).toBe(true);
  });

  test("staff set a barber's breaks by the hour with no reason, and see nothing else of the schedule (D-134)", async ({
    page,
    context,
  }) => {
    const target = targetDate();
    await staffSignIn(page, STAFF);
    const headers = { 'X-CSRF-Token': await csrf(context, page.request) };
    const clear = { professionalId: SULTAN, date: target.iso, breaks: [] };
    expect(
      (await page.request.put('/api/v1/shop/schedule/barber-day', { headers, data: clear })).status(),
    ).toBe(200);
    expect((await page.request.get('/api/v1/shop/schedule')).status()).toBe(403);

    // Only the barbers' days: no shop hours, barbers' hours, fixed breaks, time off, closures or pause.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/ar/shop/schedule');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('البريكات');
    const card = page.getByTestId('barber-day');
    await expect(card).toBeVisible();
    for (const testId of ['opening-hours', 'professional-hours', 'breaks', 'pause-card'])
      await expect(page.getByTestId(testId)).toHaveCount(0);
    await expect(page.getByText('الإجازات والتعطيل')).toHaveCount(0);

    // Sultan, on the target day: 24 hours, he works noon to 10 PM.
    await card.getByLabel('الحلاق').selectOption(SULTAN);
    const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
    const ahead = Math.round((Date.parse(target.iso) - Date.parse(today)) / 86_400_000);
    for (let day = 0; day < ahead; day++) {
      const waiting = page.waitForResponse(
        (r) => r.url().includes('/barber-day') && r.request().method() === 'GET',
      );
      await card.getByRole('button', { name: 'اليوم التالي' }).click();
      await waiting;
    }
    const hours = card.getByTestId('barber-day-hours');
    await expect(hours.locator('[data-hour]')).toHaveCount(24);
    await expect(hours.locator('[data-hour="10"]')).toHaveAttribute('data-state', 'outside');
    const one = hours.locator('[data-hour="16"]');
    await expect(one).toHaveAttribute('data-state', 'free');
    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(
      axe.violations
        .filter((v) => v.impact === 'serious' || v.impact === 'critical')
        .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`),
    ).toEqual([]);

    // One tap: 4–5 PM is a break (a seeded booking holds 1 PM two days ahead), and the public times follow; another tap
    // undoes it.
    await one.click();
    await expect(one).toHaveAttribute('aria-pressed', 'true', SAVE);
    const during = (await slots(page.request, target.iso, SULTAN, BEARD_TRIM)).slots.map((s) => s.localTime);
    expect(during).not.toContain('16:00');
    expect(during).not.toContain('15:45'); // a 20-minute beard trim from 15:45 runs into the break
    expect(during).toContain('17:00');
    await one.click();
    await expect(one).toHaveAttribute('aria-pressed', 'false', SAVE);
    expect(
      (await slots(page.request, target.iso, SULTAN, BEARD_TRIM)).slots.map((s) => s.localTime),
    ).toContain('16:00');

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
