import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';
import { captureViewports } from '../support/viewports';

/**
 * Phase 12 customer journeys against the compose stack (seeded demo data, dev OTP inbox):
 * - E1: from the shop page through the wizard as a guest, sign in at confirmation, then the booking's page (calendar
 *   file, reschedule, cancel);
 * - E6: two customers confirm the same time at once — one booking, one "just taken";
 * - E7: a customer rates a completed visit once; a second review and a review of an upcoming visit are refused;
 * - favorites, the profile, and R-NEG-02 (no payment step anywhere).
 * Each demo customer signs in once per test run (5 codes per number per hour); the suite may run three times in a row
 * on one stack, so bookings pick the first free time and are cancelled at the end.
 */
const BARBER_HOUSE = 'barber-house';
const CUT_AND_STYLE = '0199a0de-5a10-7000-8000-000000000411';
const OMAR = '0199a0de-5a10-7000-8000-000000000201';
const SARA_UPCOMING = '0199a0de-5a10-7000-8000-000000000b11';
const NOURA = '500100301';
const KHALID = '500100302';
const SARA = '500100303';

const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;

async function expectNoSeriousAxe(page: Page, label: string) {
  // A soft refresh replaces the <title> element; let it settle so axe never sees the page between the two.
  await expect(page).toHaveTitle(/\S/);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(blocking.map((v) => `${label} ${v.id}: ${v.help}`)).toEqual([]);
}

async function latestOtp(request: APIRequestContext, national: string): Promise<string> {
  const response = await request.get(
    `/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(`+966${national}`)}`,
  );
  expect(response.ok()).toBeTruthy();
  return (await response.json()).code as string;
}

/** Completes the mobile sign-in on the page it is on (the sign-in page, possibly with a returnTo). */
async function signInHere(page: Page, national: string) {
  await expect(page).toHaveURL(/\/ar\/auth\/sign-in/);
  await page.getByLabel('رقم الجوال').fill(national);
  await page.getByRole('button', { name: 'إرسال الرمز' }).click();
  await expect(page).toHaveURL(/\/ar\/auth\/verify/);
  await page.getByLabel('رمز التحقق').fill(await latestOtp(page.request, national));
  await page.getByRole('button', { name: 'تحقق' }).click();
}

async function signIn(page: Page, national: string, returnTo: string) {
  await page.goto(`/ar/auth/sign-in?returnTo=${encodeURIComponent(returnTo)}`);
  await signInHere(page, national);
  await expect(page).not.toHaveURL(/\/auth\//);
}

async function signUp(page: Page, name: string) {
  const national = newPhone();
  await page.goto('/ar/auth/sign-up');
  await page.getByLabel('رقم الجوال').fill(national);
  await page.getByLabel(/أوافق على/).check();
  await page.getByRole('button', { name: 'إرسال الرمز' }).click();
  await expect(page).toHaveURL(/\/ar\/auth\/verify/);
  await page.getByLabel('رمز التحقق').fill(await latestOtp(page.request, national));
  await page.getByRole('button', { name: 'تحقق' }).click();
  await expect(page).toHaveURL(/\/ar\/auth\/complete-profile/);
  await page.getByLabel('الاسم الكامل').fill(name);
  await page.getByLabel(/أوافق على/).check();
  await page.getByRole('button', { name: 'حفظ ومتابعة' }).click();
  await expect(page).toHaveURL(/\/ar\/discover$/);
}

/** YYYY-MM-DD in Riyadh, `days` from today. */
function riyadhDate(days: number): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
  const [y, m, d] = today.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** On a date strip, picks the first bookable day from `from` days ahead (up to `from + 4`). */
async function pickDate(page: Page, name: string, from: number): Promise<string> {
  await expect(page.locator(`input[name="${name}"]`).first()).toBeAttached();
  for (let offset = from; offset <= from + 4; offset++) {
    const date = riyadhDate(offset);
    const input = page.locator(`input[name="${name}"][value="${date}"]`);
    if ((await input.count()) > 0 && (await input.isEnabled())) {
      await page.locator(`label:has(input[name="${name}"][value="${date}"])`).click();
      return date;
    }
  }
  throw new Error(`No bookable day between ${from} and ${from + 4} days ahead for ${name}.`);
}

/**
 * Picks the n-th offered time on a slot grid and returns its start instant. The wizard asks for the hour first
 * (D-125): its first hour is chosen, then the n-th minute of it.
 */
async function pickSlot(page: Page, name: string, index = 0): Promise<string> {
  await expect(page.locator(`input[name="${name}"], input[name="${name}-hour"]`).first()).toBeAttached();
  const hour = page.locator(`input[name="${name}-hour"]`).first();
  if ((await hour.count()) > 0 && !(await hour.isChecked())) {
    const value = (await hour.getAttribute('value'))!;
    await page.locator(`label:has(input[name="${name}-hour"][value="${value}"])`).click();
  }
  const input = page.locator(`input[name="${name}"]`).nth(index);
  await expect(input).toBeAttached();
  const value = (await input.getAttribute('value'))!;
  await page.locator(`label:has(input[name="${name}"][value="${value}"])`).click();
  return value;
}

async function csrfHeaders(
  context: BrowserContext,
  request: APIRequestContext,
): Promise<Record<string, string>> {
  let token = (await context.cookies()).find((c) => c.name === 'trimme-csrf')?.value;
  if (!token) {
    await request.get('/api/v1/auth/csrf');
    token = (await context.cookies()).find((c) => c.name === 'trimme-csrf')!.value;
  }
  return { 'X-CSRF-Token': decodeURIComponent(token) };
}

async function cancelFromDetail(page: Page) {
  await page.getByRole('button', { name: 'إلغاء الموعد' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'ظرف طارئ' }).click();
  await dialog.getByRole('button', { name: 'نعم، ألغِ الموعد' }).click();
  await expect(page.getByText('تم إلغاء الموعد وأُخطر الصالون.')).toBeVisible();
  await expect(page.getByTestId('booking-detail')).toContainText('ألغاه العميل');
}

test.describe('customer booking and account (Phase 12: E1, E6, E7, R-CUS-07…10, R-NEG-02)', () => {
  test('E1: a guest books through the wizard, signs in at confirmation, then adds it to the calendar, reschedules and cancels', async ({
    page,
  }) => {
    // Shop page → the service's "book" link opens the wizard on the professional step ("any" preselected).
    await page.goto(`/ar/shops/${BARBER_HOUSE}`);
    await page.getByRole('link', { name: 'احجز قص وتصفيف' }).click();
    await expect(page).toHaveURL(/\/ar\/shops\/barber-house\/book\?service=/);
    await expect(page.getByTestId('wizard-title')).toHaveText('اختر المختص');
    await expect(page.getByRole('radio', { name: /أي مختص متاح/ })).toBeChecked();
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', /noindex/);
    await expectNoSeriousAxe(page, 'wizard professional');
    await page.getByText('عمر السالم').click();
    await page.getByRole('button', { name: 'التالي' }).click();

    // The day's times show under the days at once, without a Next (D-129).
    await expect(page.getByTestId('wizard-title')).toHaveText('اختر اليوم والوقت');
    await pickDate(page, 'date', 3);
    await expect(page.getByTestId('day-times')).toBeVisible();
    await pickSlot(page, 'time');
    await expectNoSeriousAxe(page, 'wizard time');
    await page.getByRole('button', { name: 'التالي' }).click();

    // Review: the price is paid at the shop; nothing asks for a card (R-NEG-02).
    const review = page.getByTestId('booking-review');
    await expect(review).toContainText('الإجمالي (يُدفع في الصالون)');
    await expect(review).toContainText('عمر السالم');
    await expect(page.locator('input[autocomplete^="cc-"], input[name*="card" i]')).toHaveCount(0);
    await expect(page.getByText(/ادفع الآن|بطاقة ائتمان|الدفع الإلكتروني/)).toHaveCount(0);
    await expectNoSeriousAxe(page, 'wizard review');
    const reviewUrl = page.url();

    // A guest signs in at confirmation and comes back to the same review (D-096).
    await page.getByRole('button', { name: 'تأكيد الحجز' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in\?returnTo=%2Fshops%2Fbarber-house%2Fbook/);
    await signInHere(page, NOURA);
    await expect(page).toHaveURL(/\/ar\/shops\/barber-house\/book\?.*step=review/);
    expect(new URL(page.url()).searchParams.get('time')).toBe(new URL(reviewUrl).searchParams.get('time'));
    await page.getByRole('button', { name: 'تأكيد الحجز' }).click();

    // The booking's page: confirmed (auto-confirm, DV-S13), countdown, calendar file, policy.
    await expect(page).toHaveURL(/\/ar\/account\/bookings\/[0-9a-f-]{36}\?created=1$/);
    const detail = page.getByTestId('booking-detail');
    await expect(page.getByText('تم تأكيد حجزك')).toBeVisible();
    await expect(detail).toContainText('مؤكد');
    await expect(detail).toContainText('قص وتصفيف');
    await expect(page.getByTestId('booking-countdown')).not.toBeEmpty();
    await expectNoSeriousAxe(page, 'booking detail');
    const ics = await page.request.get(
      (await page.getByRole('link', { name: 'أضف للتقويم' }).getAttribute('href'))!,
    );
    expect(ics.headers()['content-type']).toContain('text/calendar');
    expect(await ics.text()).toContain('BEGIN:VEVENT');

    // It is listed under upcoming appointments.
    const bookingPath = new URL(page.url()).pathname;
    await page.goto('/ar/account/bookings');
    const card = page.getByTestId('booking-card').filter({ has: page.locator(`a[href="${bookingPath}"]`) });
    await expect(card).toContainText('باربر هاوس');
    await expect(card.getByRole('link', { name: 'إعادة جدولة' })).toBeVisible();
    await expectNoSeriousAxe(page, 'bookings list');

    // Reschedule: the current time stays booked until the new one is confirmed (D-089).
    await card.getByRole('link', { name: 'إعادة جدولة' }).click();
    await expect(page.getByText(/سيبقى محجوزاً حتى تؤكد الموعد الجديد/)).toBeVisible();
    await pickDate(page, 'reschedule-date', 4);
    const moved = await pickSlot(page, 'reschedule-time', 1);
    await expectNoSeriousAxe(page, 'reschedule');
    await page.getByRole('button', { name: 'تأكيد الموعد الجديد' }).click();
    await expect(page.getByText('تم تحديث موعدك.')).toBeVisible();
    expect(moved).toBeTruthy();

    // Cancel within the policy: the shop is told and the time is released.
    await cancelFromDetail(page);
  });

  test('E6: two customers confirm the same time at once — exactly one booking, the other sees "just taken"', async ({
    browser,
  }) => {
    const contexts = await Promise.all([browser.newContext(), browser.newContext()]);
    const [first, second] = (await Promise.all(contexts.map((c) => c.newPage()))) as [Page, Page];
    await signIn(first, KHALID, '/account');
    await signUp(second, 'ريان المطيري');

    // Find the first free time with Omar a week or so ahead, then bring both customers to its review step.
    await first.goto(`/ar/shops/${BARBER_HOUSE}/book?service=${CUT_AND_STYLE}&pro=${OMAR}&step=date`);
    await pickDate(first, 'date', 9);
    await pickSlot(first, 'time');
    await first.getByRole('button', { name: 'التالي' }).click();
    await expect(first.getByTestId('booking-review')).toBeVisible();
    const review = first.url();
    await second.goto(review);
    for (const page of [first, second]) {
      await expect(page.getByRole('button', { name: 'تأكيد الحجز' })).toBeEnabled();
    }

    await Promise.all(
      [first, second].map((page) => page.getByRole('button', { name: 'تأكيد الحجز' }).click()),
    );

    const outcome = async (page: Page) => {
      const booked = page.getByTestId('booking-detail');
      const taken = page.getByText('حُجز هذا الوقت للتو');
      await expect(booked.or(taken)).toBeVisible({ timeout: 20_000 });
      return (await booked.isVisible()) ? 'booked' : 'taken';
    };
    const results = [await outcome(first), await outcome(second)];
    expect([...results].sort()).toEqual(['booked', 'taken']);

    // The loser is back on fresh times, without the taken one selected; the winner cancels to keep reruns free.
    const loser = results[0] === 'taken' ? first : second;
    const winner = results[0] === 'booked' ? first : second;
    await expect(loser.getByTestId('wizard-title')).toHaveText('اختر اليوم والوقت');
    await expect(loser).not.toHaveURL(/time=/);
    await expectNoSeriousAxe(loser, 'conflict');
    await cancelFromDetail(winner);
    await Promise.all(contexts.map((c) => c.close()));
  });

  test('E7: a customer rates a completed visit once; the review is published with the first name and initial', async ({
    page,
    context,
  }) => {
    await signIn(page, SARA, '/account/bookings?tab=past');
    const rate = page.getByRole('link', { name: 'قيّم الزيارة' }).first();
    await expect(
      rate,
      'Sara has no reviewable visit left: her seeded visits expire after the 7-day review window — reset the volume (docker compose down -v).',
    ).toBeVisible();
    await rate.click();

    await expect(page).toHaveURL(/\/ar\/account\/bookings\/[0-9a-f-]{36}\/review$/);
    const bookingId = page.url().split('/').at(-2)!;
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('كيف كانت زيارتك مع ماجد؟');
    await expect(page.getByRole('button', { name: 'اختر عدد النجوم أولاً' })).toBeDisabled();
    await page.locator('label:has(input[name="rating"][value="5"])').click();
    await expect(page.getByTestId('star-label')).toHaveText('استثنائية');
    await page.getByRole('button', { name: 'الالتزام بالوقت' }).click();
    await page.getByLabel(/تعليق/).fill('مواعيد دقيقة وخدمة ممتازة.');
    await expectNoSeriousAxe(page, 'review form');
    await page.getByRole('button', { name: 'إرسال التقييم' }).click();
    await expect(page.getByText('شكراً لتقييمك')).toBeVisible();

    // Once only: the page says so, and the API refuses a second review (D-017).
    await page.goto(`/ar/account/bookings/${bookingId}/review`);
    await expect(page.getByText('قيّمت هذه الزيارة من قبل.')).toBeVisible();
    const again = await page.request.post(`/api/v1/me/bookings/${bookingId}/review`, {
      headers: await csrfHeaders(context, page.request),
      data: { rating: 1 },
    });
    expect(again.status()).toBe(409);
    expect((await again.json()).errorCode).toBe('review.already_exists');
    await page.goto(`/ar/account/bookings/${bookingId}`);
    await expect(page.getByText('تم التقييم ★ 5.0')).toBeVisible();

    // An upcoming visit cannot be rated.
    await page.goto(`/ar/account/bookings/${SARA_UPCOMING}/review`);
    await expect(page.getByText('يمكنك التقييم بعد اكتمال الزيارة.')).toBeVisible();

    // Published on the shop page at once, as "سارة ع.".
    await page.goto(`/ar/shops/${BARBER_HOUSE}?tab=reviews`);
    await expect(page.getByRole('tabpanel', { name: 'التقييمات' })).toContainText('سارة ع.');
  });

  test('favorites and profile: hearts on the shop and barber pages, the favorites list, and editing the name', async ({
    page,
  }) => {
    // A guest's heart asks them to sign in; a new customer signs up from there and lands back on the shop page
    // (client-side navigation), where the heart now works (the signed-out answer is not kept).
    await page.goto(`/ar/shops/${BARBER_HOUSE}`);
    const guestHeart = page.getByRole('link', { name: /سجّل الدخول لحفظ باربر هاوس/ });
    await expect(guestHeart).toHaveAttribute('href', '/ar/auth/sign-in?returnTo=%2Fshops%2Fbarber-house');
    await guestHeart.click();
    await expect(page).toHaveURL(/\/ar\/auth\/sign-in\?returnTo=%2Fshops%2Fbarber-house/);
    await page.getByRole('link', { name: 'أنشئ حساباً' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/sign-up\?returnTo=%2Fshops%2Fbarber-house/);
    await expect(page.getByRole('heading', { name: 'أهلاً بك في تريمي' })).toBeVisible();
    const national = newPhone();
    await page.getByLabel('رقم الجوال').fill(national);
    await page.getByLabel(/أوافق على/).check();
    await page.getByRole('button', { name: 'إرسال الرمز' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/verify\?returnTo=/);
    await page.getByLabel('رمز التحقق').fill(await latestOtp(page.request, national));
    await page.getByRole('button', { name: 'تحقق' }).click();
    await expect(page).toHaveURL(/\/ar\/auth\/complete-profile\?returnTo=/);
    await page.getByLabel('الاسم الكامل').fill('ليان القحطاني');
    await page.getByLabel(/أوافق على/).check();
    await page.getByRole('button', { name: 'حفظ ومتابعة' }).click();
    await expect(page).toHaveURL(/\/ar\/shops\/barber-house$/);
    await expect(page.getByRole('button', { name: 'أضف باربر هاوس إلى المفضلة' })).toBeVisible();

    // The heart turns at once (optimistic); wait for the API to store it before leaving the page.
    const saved = (kind: string) =>
      page.waitForResponse(
        (r) => r.url().includes(`/api/v1/me/favorites/${kind}/`) && r.request().method() === 'PUT',
      );
    await page.goto(`/ar/shops/${BARBER_HOUSE}`);
    const shopSaved = saved('shops');
    await page.getByRole('button', { name: 'أضف باربر هاوس إلى المفضلة' }).click();
    expect((await shopSaved).status()).toBe(204);
    await expect(page.getByRole('button', { name: 'أزل باربر هاوس من المفضلة' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await page.goto(`/ar/shops/${BARBER_HOUSE}/professionals/omar`);
    const proSaved = saved('professionals');
    await page.getByRole('button', { name: 'أضف عمر السالم إلى المفضلة' }).click();
    expect((await proSaved).status()).toBe(204);
    await expect(page.getByRole('button', { name: 'أزل عمر السالم من المفضلة' })).toBeVisible();

    await page.goto('/ar/account/favorites');
    const favorites = page.getByTestId('favorites');
    await expect(favorites.getByRole('link', { name: 'باربر هاوس' })).toBeVisible();
    await expect(favorites.getByRole('link', { name: 'احجز مع عمر السالم' })).toHaveAttribute(
      'href',
      new RegExp(`/ar/shops/barber-house/book\\?pro=${OMAR}`),
    );
    await expectNoSeriousAxe(page, 'favorites');
    const removed = page.waitForResponse(
      (r) => r.url().includes('/api/v1/me/favorites/shops/') && r.request().method() === 'DELETE',
    );
    await favorites.getByRole('button', { name: 'أزل باربر هاوس من المفضلة' }).click();
    expect((await removed).status()).toBe(204);
    await expect(favorites.getByRole('button', { name: 'أضف باربر هاوس إلى المفضلة' })).toBeVisible();
    await page.reload();
    await expect(favorites.getByRole('link', { name: 'باربر هاوس' })).toHaveCount(0);
    await expect(favorites.getByText('عمر السالم')).toBeVisible();

    // Account: payment is "at the shop" (no payment in v1), and the name can be changed.
    await page.goto('/ar/account');
    await expect(page.getByText('طريقة الدفع')).toBeVisible();
    await expect(page.getByText('في الصالون')).toBeVisible();
    await expectNoSeriousAxe(page, 'account');
    await page.getByRole('link', { name: 'تعديل البيانات الشخصية' }).click();
    await page.getByLabel('الاسم الكامل').fill('ليان محمد القحطاني');
    await page.getByRole('button', { name: 'حفظ التغييرات' }).click();
    await expect(page).toHaveURL(/\/ar\/account\?saved=1$/);
    await expect(page.getByText('ليان محمد القحطاني')).toBeVisible();
  });

  test('wizard, appointments and account layouts at phone, tablet and desktop widths', async ({
    page,
  }, testInfo) => {
    await captureViewports(
      page,
      testInfo,
      'booking-wizard',
      `/ar/shops/${BARBER_HOUSE}/book?service=${CUT_AND_STYLE}&pro=${OMAR}&step=date`,
    );
    await signUp(page, 'نواف العتيبي');
    await captureViewports(page, testInfo, 'my-appointments', '/ar/account/bookings');
    await captureViewports(page, testInfo, 'account', '/ar/account');
    for (const width of [390, 768, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`/ar/shops/${BARBER_HOUSE}/book?service=${CUT_AND_STYLE}`);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      expect(overflow, `no horizontal scroll at ${width}px`).toBeLessThanOrEqual(1);
    }
  });
});
