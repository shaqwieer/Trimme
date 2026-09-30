import AxeBuilder from '@axe-core/playwright';
import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

/**
 * Phase 16 (R-QR-01/02, R-CUS-13, R-AD-09, D-114): an admin creates a shop code; a visitor scans it (the locale-less
 * printed URL) as a guest, signs up, books in the wizard, and the booking is credited to the code in the admin and shop
 * views; a barber code opens the barber's landing; a switched-off code is not found; reloads count once; the admin and
 * shop QR pages, files and A5 poster. Sign-up uses a fresh number (the OTP budget is per number). The booking is
 * cancelled at the end, so the suite can run again on the same stack.
 */
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@trimme.local';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'trimme local admin';
const DEMO_PASSWORD = process.env.E2E_DEMO_PASSWORD ?? 'trimme local demo';
const BARBER_HOUSE_OWNER = 'owner@barber-house.trimme.local';
const SULTAN_CODE = 'assu2tnm';
const RETIRED_CODE = 'bhxx8dfg';
const AL_ASALA_CODE = 'aswn7qkd';

const newPhone = () => `5${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;

async function expectNoSeriousAxe(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(blocking.map((v) => `${label} ${v.id}: ${v.help}`)).toEqual([]);
}

async function staffSignIn(page: Page, email: string, password: string) {
  await page.goto('/en/auth/staff/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/en\/(admin|shop)$/);
}

async function latestOtp(request: APIRequestContext, national: string): Promise<string> {
  const response = await request.get(
    `/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(`+966${national}`)}`,
  );
  expect(response.ok()).toBeTruthy();
  return (await response.json()).code as string;
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
  await expect(page).toHaveURL(/\/ar\/account$/);
}

/** YYYY-MM-DD in Riyadh, `days` from today. */
function riyadhDate(days: number): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
  const [y, m, d] = today.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

async function pickDate(page: Page, from: number) {
  await expect(page.locator('input[name="date"]').first()).toBeAttached();
  for (let offset = from; offset <= from + 4; offset++) {
    const input = page.locator(`input[name="date"][value="${riyadhDate(offset)}"]`);
    if ((await input.count()) > 0 && (await input.isEnabled())) {
      await page.locator(`label:has(input[name="date"][value="${riyadhDate(offset)}"])`).click();
      return;
    }
  }
  throw new Error('No bookable day found.');
}

/** A code's figures over the last 30 days, read as the admin (the page's session). */
async function codeFigures(page: Page, code: string) {
  const response = await page.request.get(`/api/v1/admin/qr/codes?search=${code}`);
  expect(response.ok()).toBeTruthy();
  const item = (await response.json()).items.find((c: { code: string }) => c.code === code);
  return { visits: item.visits as number, bookings: item.bookings as number, id: item.id as string };
}

async function waitForScan(page: Page, path: string) {
  const recorded = page.waitForResponse(
    (r) => r.url().includes('/api/v1/public/qr/') && r.url().endsWith('/visits'),
  );
  await page.goto(path);
  expect((await recorded).status()).toBe(204);
}

test.describe('QR codes and attribution (Phase 16)', () => {
  test('scan → book → credited: an admin creates a shop code, a visitor scans it, signs up and books; admin and shop see the credit', async ({
    browser,
    page,
  }) => {
    // The admin creates a code for Barber House from the QR page.
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/ar/admin/qr');
    await page.getByRole('button', { name: 'رمز جديد' }).click();
    const dialog = page.getByRole('dialog', { name: 'إنشاء رمز QR' });
    await dialog.getByLabel('المحل', { exact: true }).selectOption({ label: 'باربر هاوس' });
    await dialog.getByLabel(/مكان الاستخدام/).fill('ملصق اختبار');
    await dialog.getByRole('button', { name: 'إنشاء الرمز' }).click();
    const created = page.getByRole('status').filter({ hasText: 'تم إنشاء الرمز' });
    await expect(created).toBeVisible();
    const code = /تم إنشاء الرمز ([a-z2-9]{8})\./.exec((await created.textContent()) ?? '')![1]!;
    expect(await codeFigures(page, code)).toMatchObject({ visits: 0, bookings: 0 });

    // A guest scans the printed URL: no locale, so the web app picks one; the scan sets an HttpOnly cookie.
    const visitor = await browser.newContext({ locale: 'ar-SA' });
    const scan = await visitor.newPage();
    await waitForScan(scan, `/q/${code}`);
    await expect(scan).toHaveURL(new RegExp(`/ar/q/${code}$`));
    await expect(scan.getByText('دخلت عبر رمز المحل')).toBeVisible();
    await expect(scan.getByRole('heading', { level: 1 })).toContainText('باربر هاوس');
    await expect(scan.locator('meta[name="robots"]')).toHaveAttribute('content', /noindex/);
    await expect(scan.locator('link[rel="canonical"]')).toHaveAttribute('href', /\/ar\/shops\/barber-house$/);
    await expect(scan.getByRole('link', { name: 'احجز الآن' })).toHaveAttribute(
      'href',
      '/ar/shops/barber-house/book',
    );
    const qrCookie = async () => (await visitor.cookies()).find((c) => c.name === 'trimme-qr');
    const cookie = await qrCookie();
    expect(cookie?.httpOnly).toBe(true);
    expect(cookie?.path).toBe('/api/v1');
    await expectNoSeriousAxe(scan, 'qr landing');

    // The guest then signs up (the cookie survives the sign-up) and books in the wizard, without scanning again.
    await signUp(scan, 'ريم الشهري');
    await scan.goto('/ar/shops/barber-house/book');
    await expect(scan.getByTestId('wizard-title')).toHaveText('اختر الخدمة');
    await scan.getByText('قص وتصفيف').first().click();
    await scan.getByRole('button', { name: 'التالي' }).click();
    await expect(scan.getByTestId('wizard-title')).toHaveText('اختر الحلاق');
    await scan.getByRole('button', { name: 'التالي' }).click();
    await expect(scan.getByTestId('wizard-title')).toHaveText('اختر التاريخ');
    await pickDate(scan, 5);
    await scan.getByRole('button', { name: 'التالي' }).click();
    await expect(scan.getByTestId('wizard-title')).toHaveText('اختر الوقت');
    const slot = scan.locator('input[name="time"]').first();
    await expect(slot).toBeAttached();
    await scan.locator(`label:has(input[name="time"][value="${await slot.getAttribute('value')}"])`).click();
    await scan.getByRole('button', { name: 'التالي' }).click();
    expect((await qrCookie())?.value).toBe(cookie?.value);
    await scan.getByRole('button', { name: 'تأكيد الحجز' }).click();
    await expect(scan).toHaveURL(/\/ar\/account\/bookings\/[0-9a-f-]{36}\?created=1$/);
    const bookingId = /bookings\/([0-9a-f-]{36})/.exec(scan.url())![1]!;

    // Credited: the admin booking shows «رمز QR»; the code has one scan and one booking.
    await page.goto(`/ar/admin/bookings/${bookingId}`);
    await expect(page.getByText('رمز QR', { exact: true })).toBeVisible();
    expect(await codeFigures(page, code)).toMatchObject({ visits: 1, bookings: 1 });

    // The shop sees its own code with the credit on its QR page, and the source in its appointment.
    const shop = await browser.newContext();
    const owner = await shop.newPage();
    await staffSignIn(owner, BARBER_HOUSE_OWNER, DEMO_PASSWORD);
    await owner.goto('/ar/shop/qr');
    const card = owner.getByTestId('qr-code-card').filter({ hasText: `/q/${code}` });
    await expect(card).toContainText('1 مسح · 1 حجز');
    await expect(owner.getByText(AL_ASALA_CODE)).toHaveCount(0);

    // Clean up: the visitor cancels, so the time is free for the next run.
    await scan.getByRole('button', { name: 'إلغاء الموعد' }).click();
    const cancel = scan.getByRole('dialog');
    await cancel.getByRole('button', { name: 'ظرف طارئ' }).click();
    await cancel.getByRole('button', { name: 'نعم، ألغِ الموعد' }).click();
    await expect(scan.getByText('تم إلغاء الموعد وأُخطر المحل.')).toBeVisible();

    // Switched off: the printed code now shows «not found»; its figures stay.
    await page.goto('/ar/admin/qr');
    const row = page.locator('tr').filter({ hasText: `/q/${code}` });
    await row.getByRole('button', { name: 'إيقاف' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'إيقاف' }).click();
    await expect(row.getByText('موقوف')).toBeVisible();
    const gone = await scan.goto(`/ar/q/${code}`);
    expect(gone?.status()).toBe(404);
    expect(await codeFigures(page, code)).toMatchObject({ visits: 1, bookings: 1 });
    await visitor.close();
    await shop.close();
  });

  test("a barber's code opens the barber's landing (English too), a reload counts once, a retired code is not found", async ({
    browser,
    page,
  }) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    const before = await codeFigures(page, SULTAN_CODE);

    const visitor = await browser.newContext({ locale: 'en-US' });
    const landing = await visitor.newPage();
    await waitForScan(landing, `/q/${SULTAN_CODE}`);
    await expect(landing).toHaveURL(new RegExp(`/en/q/${SULTAN_CODE}$`));
    await expect(landing.getByTestId('qr-landing')).toHaveAttribute('data-target', 'Professional');
    await expect(landing.getByText("You came in with the barber's code")).toBeVisible();
    await expect(landing.getByRole('heading', { level: 1 })).toHaveText('Sultan Al-Harbi');
    await expect(landing.getByText('at Al Asala Barbershop')).toBeVisible();
    await expect(landing.locator('link[rel="canonical"]')).toHaveAttribute(
      'href',
      /\/en\/shops\/al-asala\/professionals\/sultan$/,
    );
    await expect(landing.getByRole('link', { name: 'Book now' })).toHaveAttribute(
      'href',
      /\/en\/shops\/al-asala\/book\?pro=0199a0de-5a10-7000-8000-000000000102/,
    );
    await expectNoSeriousAxe(landing, 'barber landing');

    // The same browser reloads: the visit is reused, so the scan counts once.
    await waitForScan(landing, `/en/q/${SULTAN_CODE}`);
    expect((await codeFigures(page, SULTAN_CODE)).visits).toBe(before.visits + 1);

    const retired = await landing.goto(`/ar/q/${RETIRED_CODE}`);
    expect(retired?.status()).toBe(404);
    await expect(landing.getByRole('heading', { level: 1 })).toHaveText('الصفحة غير موجودة');
    await visitor.close();
  });

  test('admin and shop QR pages: figures, files, the A5 poster, accessibility and no overflow on a phone', async ({
    browser,
    page,
  }) => {
    await staffSignIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.goto('/ar/admin/qr');
    await expect(page.getByTestId('qr-kpis')).toContainText('عمليات المسح');
    await expect(page.getByTestId('qr-by-shop')).toContainText('صالون الأصالة');
    await expect(page.getByText('لا نحفظ عنوان IP')).toBeVisible();
    await expectNoSeriousAxe(page, 'admin qr');

    // Files: PNG, SVG and PDF of the printed URL.
    const sultan = await codeFigures(page, SULTAN_CODE);
    for (const [format, type] of [
      ['png', 'image/png'],
      ['svg', 'image/svg+xml'],
      ['pdf', 'application/pdf'],
    ] as const) {
      const file = await page.request.get(`/api/v1/admin/qr/codes/${sultan.id}/image?format=${format}`);
      expect(file.ok()).toBeTruthy();
      expect(file.headers()['content-type']).toContain(type);
    }

    // The A5 poster: the code image, the barber and shop names and the call to scan.
    await page.goto(`/ar/admin/qr/${sultan.id}/poster`);
    const poster = page.getByTestId('qr-poster');
    await expect(poster).toContainText('سلطان الحربي');
    await expect(poster).toContainText('امسح الرمز واحجز دورك');
    await expect(poster.getByRole('img', { name: 'رمز QR لـ سلطان الحربي' })).toBeVisible();
    await expect
      .poll(() =>
        poster.getByRole('img', { name: /رمز QR/ }).evaluate((img: HTMLImageElement) => img.naturalWidth),
      )
      .toBeGreaterThan(0);
    await expectNoSeriousAxe(page, 'admin poster');

    // The shop owner: only its own codes, read-only (no create or switch), with files and the poster.
    const shop = await browser.newContext();
    const owner = await shop.newPage();
    await staffSignIn(owner, BARBER_HOUSE_OWNER, DEMO_PASSWORD);
    await owner.goto('/ar/shop/qr');
    await expect(owner.getByRole('link', { name: 'رموز QR' }).first()).toBeVisible();
    await expect(owner.getByTestId('qr-code-card').first()).toBeVisible();
    await expect(owner.getByText(SULTAN_CODE)).toHaveCount(0);
    await expect(owner.getByRole('button', { name: 'رمز جديد' })).toHaveCount(0);
    await expect(owner.getByRole('button', { name: 'إيقاف' })).toHaveCount(0);
    await expectNoSeriousAxe(owner, 'shop qr');
    expect((await owner.request.get(`/api/v1/shop/qr/codes/${sultan.id}/image`)).status()).toBe(404);

    for (const width of [390, 768, 1440]) {
      for (const [p, path] of [
        [owner, '/ar/shop/qr'],
        [page, `/ar/admin/qr/${sultan.id}/poster`],
        [page, `/ar/q/${AL_ASALA_CODE}`],
      ] as const) {
        await p.setViewportSize({ width, height: 900 });
        await p.goto(path, { waitUntil: 'load' });
        await p.evaluate(() => document.fonts.ready);
        const overflow = await p.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        expect(overflow, `${path} at ${width}px`).toBeLessThanOrEqual(1);
      }
    }
    await shop.close();
  });
});
