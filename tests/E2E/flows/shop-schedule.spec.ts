import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

/**
 * Phase 09 flow (spec §19 E2, schedule part; R-AVL-02/03): the shop edits a professional's hours, records time off and
 * pauses online booking in /shop/schedule, and the public slot API follows each change. A barber's own login sees none
 * of that: only their day by the hour, where they set breaks with no reason (D-134, D-145). Every change is undone, so the flow can
 * run again on the same stack. Demo data: Al Asala (DemoData), Faisal does the haircut, Sultan the beard trim.
 */
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const OWNER = 'owner@al-asala.trimme.local';
/** Sultan's own login, by the WhatsApp number on his profile (D-145). */
const SULTAN_LOGIN = '0500100102';
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

/**
 * Undo anything a failed earlier run left behind (Faisal's own hours, Faisal's time off). A pause left behind is the
 * barbers' to lift (D-147): the barber's test resumes first.
 */
async function resetSchedule(page: Page, context: BrowserContext) {
  const headers = { 'X-CSRF-Token': await csrf(context, page.request) };
  const schedule = await (await page.request.get('/api/v1/shop/schedule')).json();
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
  test('owner edits hours and time off and only sees the pause; the public slots follow each change', async ({
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

    // The pause is the barbers' now (D-147): the owner sees its state, with no button, and the API refuses it.
    const pause = page.getByTestId('pause-card');
    await expect(pause).toBeVisible();
    await expect(pause.getByRole('button')).toHaveCount(0);
    const headers = { 'X-CSRF-Token': await csrf(context, page.request) };
    expect(
      (await page.request.post('/api/v1/shop/online-booking/pause', { headers, data: {} })).status(),
    ).toBe(403);
    // Nor does the owner set a barber's one-off break: the breaks card adds weekly breaks only.
    await page.getByTestId('breaks').getByRole('button', { name: 'Add break' }).click();
    await expect(page.getByRole('dialog').getByLabel('Date')).toHaveCount(0);
    await page.keyboard.press('Escape');
  });

  test('a barber sets their own breaks every 5 minutes with a confirmation and no reason, and sees nothing else of the schedule (D-134, D-137, D-145)', async ({
    page,
    context,
  }) => {
    const target = targetDate();
    await staffSignIn(page, SULTAN_LOGIN);
    const headers = { 'X-CSRF-Token': await csrf(context, page.request) };
    const clear = { professionalId: SULTAN, date: target.iso, breaks: [] };
    expect(
      (await page.request.put('/api/v1/shop/schedule/barber-day', { headers, data: clear })).status(),
    ).toBe(200);
    expect((await page.request.get('/api/v1/shop/schedule')).status()).toBe(403);
    const state = await (await page.request.get('/api/v1/shop/online-booking')).json();
    if (state.paused) await page.request.post('/api/v1/shop/online-booking/resume', { headers });

    // Only their own day and the pause (D-147): no shop hours, barbers' hours, fixed breaks, time off or closures.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/ar/shop/schedule');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('البريكات');
    const card = page.getByTestId('barber-day');
    await expect(card).toBeVisible();
    for (const testId of ['opening-hours', 'professional-hours', 'breaks'])
      await expect(page.getByTestId(testId)).toHaveCount(0);
    await expect(page.getByTestId('pause-card')).toBeVisible();
    await expect(page.getByText('الإجازات والتعطيل')).toHaveCount(0);

    // Sultan, on the target day: he works noon to 10 PM.
    // His own login has no barber picker: the card names him (D-145).
    await expect(card.getByLabel('الحلاق')).toHaveCount(0);
    await expect(card).toContainText('سلطان الحربي');
    const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
    const ahead = Math.round((Date.parse(target.iso) - Date.parse(today)) / 86_400_000);
    for (let day = 0; day < ahead; day++) {
      const waiting = page.waitForResponse(
        (r) => r.url().includes('/barber-day') && r.request().method() === 'GET',
      );
      await card.getByRole('button', { name: 'اليوم التالي' }).click();
      await waiting;
    }
    // His working hours only (D-151, «بريكاتي»): noon to 10 PM; 10 and 11 AM and 10 PM are not offered.
    await expect(card.getByRole('heading', { name: 'بريكاتي' })).toBeVisible();
    const hours = card.getByTestId('barber-day-hours');
    for (const hour of [12, 16, 21]) await expect(hours.locator(`[data-hour="${hour}"]`)).toBeVisible();
    for (const hour of [10, 11, 22]) await expect(hours.locator(`[data-hour="${hour}"]`)).toHaveCount(0);
    const one = hours.locator('[data-hour="16"]');
    await expect(one).toHaveAttribute('data-state', 'free');

    // The hour opens its twelve 5-minute slots, as the booking does (D-137).
    await one.click();
    await expect(one).toHaveAttribute('aria-expanded', 'true');
    const minutes = card.getByTestId('barber-day-minutes');
    await expect(minutes.locator('[data-minute]')).toHaveCount(12);
    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(
      axe.violations
        .filter((v) => v.impact === 'serious' || v.impact === 'critical')
        .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`),
    ).toEqual([]);

    // 4:00–4:15 PM picked and confirmed is a break (a seeded booking holds 1 PM two days ahead); the page says so and
    // the public times follow. Cancelling it, after its own confirmation, frees it again.
    for (const minute of [960, 965, 970]) await minutes.locator(`[data-minute="${minute}"]`).click();
    await expect(minutes.locator('[aria-pressed="true"]')).toHaveCount(3);
    await minutes.getByRole('button', { name: 'تأكيد الاستراحة' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'تأكيد الاستراحة' }).click();
    await expect(card.getByText(/^تم حجز .* استراحة لـ /)).toBeVisible(SAVE);
    await expect(one).toHaveAttribute('data-state', 'partBreak');
    const during = (await slots(page.request, target.iso, SULTAN, BEARD_TRIM)).slots.map((s) => s.localTime);
    expect(during).not.toContain('16:00');
    expect(during).not.toContain('15:45'); // a 20-minute beard trim from 15:45 runs into the break
    expect(during).toContain('16:15');
    expect(during).toContain('17:00');

    await minutes.locator('[data-minute="965"]').click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'إلغاء الاستراحة' }).click();
    await expect(card.getByText(/^أُلغيت استراحة /)).toBeVisible(SAVE);
    await expect(one).toHaveAttribute('data-state', 'free');
    expect(
      (await slots(page.request, target.iso, SULTAN, BEARD_TRIM)).slots.map((s) => s.localTime),
    ).toContain('16:00');

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);

    // The shop's pause is the barbers' (D-147): no slots while paused; resuming brings them back.
    const pause = page.getByTestId('pause-card');
    await pause.getByRole('button', { name: 'إيقاف مؤقت' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'إيقاف الآن' }).click();
    await expect(pause).toHaveAttribute('data-paused', 'true', SAVE);
    const paused = await slots(page.request, target.iso);
    expect(paused.bookable).toBe(false);
    expect(paused.blockedReason).toBe('shop.paused');
    await pause.getByRole('button', { name: 'إعادة تفعيل الحجز' }).click();
    await expect(pause).toHaveAttribute('data-paused', 'false', SAVE);
    expect((await slots(page.request, target.iso)).bookable).toBe(true);
  });
});
