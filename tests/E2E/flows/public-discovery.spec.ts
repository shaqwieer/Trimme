import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';
import { png } from '../support/images';
import { captureViewports } from '../support/viewports';

/**
 * Phase 11 public discovery against the compose stack (R-CUS-01…06, R-WEB-10/11, R-NEG-10; spec §19 E1 discovery part).
 * Assertions use Barber House: the schedule flow may pause Al Asala while it runs in parallel (D-013 hides paused shops).
 */
const BLANK_TILE = png(256, 256, [233, 238, 243]);
const BARBER_HOUSE = 'barber-house';

async function stubTiles(page: Page) {
  // No third-party tiles in automated runs: every tile is a blank image.
  await page.route('https://tile.openstreetmap.org/**', (route) =>
    route.fulfill({ status: 200, contentType: 'image/png', body: BLANK_TILE }),
  );
}

async function expectNoSeriousAxe(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    blocking.map((v) => `${label} ${v.id}: ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}

async function jsonLd(page: Page): Promise<Array<Record<string, unknown>>> {
  const blocks = await page.locator('script[type="application/ld+json"]').allTextContents();
  return blocks.flatMap((text) => {
    const parsed = JSON.parse(text) as Record<string, unknown> | Array<Record<string, unknown>>;
    return Array.isArray(parsed) ? parsed : [parsed];
  });
}

test.describe('public discovery (Phase 11)', () => {
  test('landing_renders_ar_en with real figures, top-rated shops, canonical, hreflang and organization data', async ({
    page,
  }) => {
    await page.goto('/ar');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      'احجز حلاقتك في دقيقة، بلا اتصال ولا انتظار',
    );
    await expect(page.getByLabel('تريمي بالأرقام')).toContainText('صالون شريك');
    const topRated = page
      .getByRole('region', { name: /الأعلى تقييماً/ })
      .or(page.locator('section[aria-labelledby="top-heading"]'));
    await expect(topRated.getByRole('link', { name: 'باربر هاوس' })).toHaveAttribute(
      'href',
      `/ar/shops/${BARBER_HOUSE}`,
    );
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', /\/ar$/);
    await expect(page.locator('link[rel="alternate"][hreflang="en"]')).toHaveAttribute('href', /\/en$/);
    await expect(page.locator('link[rel="alternate"][hreflang="x-default"]')).toHaveAttribute(
      'href',
      /\/ar$/,
    );
    const data = await jsonLd(page);
    expect(data.map((d) => d['@type'])).toEqual(expect.arrayContaining(['Organization', 'WebSite']));
    expect(JSON.stringify(data)).not.toContain('AggregateRating');

    await page.goto('/en');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      'Book your haircut in a minute, no calls, no waiting',
    );
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('link', { name: 'Barber House' }).first()).toHaveAttribute(
      'href',
      `/en/shops/${BARBER_HOUSE}`,
    );
  });

  test('manual_location_sets_search_origin and the search lists nearest shops with their matched service and earliest time', async ({
    page,
  }) => {
    await page.goto('/ar/onboarding/location?returnTo=/search');
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', /noindex/);
    await page.getByRole('button', { name: 'أدخل الحي يدوياً' }).click();
    await page.getByRole('searchbox', { name: 'ابحث عن الحي' }).fill('حطين');
    await page.getByRole('button', { name: /حطين، الرياض/ }).click();
    await expect(page).toHaveURL(/\/ar\/search$/);

    // The choice lives in a first-party cookie with rounded coordinates; nothing in Web Storage.
    const cookie = (await page.context().cookies()).find((c) => c.name === 'trimme-location');
    expect(decodeURIComponent(cookie?.value ?? '')).toContain('"label":"حطين"');
    expect(await page.evaluate(() => localStorage.length + sessionStorage.length)).toBe(0);

    await expect(page.getByText(/ضمن 10 كم/)).toBeVisible();
    const first = page.getByRole('article').first();
    await expect(first.getByRole('link', { name: 'باربر هاوس' })).toBeVisible();
    await expect(first).toContainText('كم');

    // Text search with an Arabic spelling variant finds the service and says which one matched.
    await page.getByRole('searchbox', { name: 'ابحث عن محل أو خدمة' }).fill('تحديد لحيه');
    await page.getByRole('searchbox', { name: 'ابحث عن محل أو خدمة' }).press('Enter');
    await expect(page).toHaveURL(/q=/);
    const result = page.getByRole('article').filter({ hasText: 'باربر هاوس' });
    await expect(result).toContainText('تحديد لحية');
    await expectNoSeriousAxe(page, '/ar/search');
  });

  test('the filter drawer shows a live count, applies to the URL, and the map shows price pins with a list alternative', async ({
    page,
  }) => {
    await stubTiles(page);
    await page.goto('/ar/search');
    await page.getByRole('button', { name: /^فلاتر/ }).click();
    const drawer = page.getByRole('dialog', { name: 'التصفية والترتيب' });
    await expect(drawer).toBeVisible();
    await drawer.getByRole('switch', { name: 'يقبل الحجز اليوم' }).click();
    await expect(drawer.getByRole('button', { name: /^عرض|لا نتائج/ })).toBeVisible();
    await drawer.getByRole('switch', { name: 'يقبل الحجز اليوم' }).click();
    await drawer.getByRole('radio', { name: 'الأعلى تقييماً' }).check();
    await drawer.getByRole('button', { name: /^عرض/ }).click();
    await expect(page).toHaveURL(/sort=rating/);

    await page.getByRole('link', { name: 'عرض الخريطة' }).click();
    await expect(page).toHaveURL(/view=map/);
    await expect(page.getByTestId('results-map')).toBeVisible();
    const pin = page.getByRole('button', { name: /باربر هاوس، يبدأ من/ });
    await expect(pin).toBeVisible();
    await pin.click();
    await expect(
      page.getByRole('region', { name: 'المحل المختار' }).getByRole('link', { name: 'باربر هاوس' }),
    ).toBeVisible();
    await page.getByRole('link', { name: 'عرض القائمة' }).click();
    await expect(page).not.toHaveURL(/view=map/);
  });

  test('shop_page_sections: gallery, identity, live status, services and packages, barbers, reviews and about, with LocalBusiness data', async ({
    page,
  }) => {
    await stubTiles(page);
    await page.goto(`/ar/shops/${BARBER_HOUSE}`);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('باربر هاوس');
    await expect(page.getByTestId('shop-open-status')).toBeVisible();
    await expect(page.getByRole('link', { name: 'الاتجاهات' })).toHaveAttribute(
      'href',
      /openstreetmap\.org\/directions/,
    );

    // Services and packages with the shop's own prices, each bookable.
    await expect(page.getByRole('heading', { name: 'قص وتصفيف' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'الباقات' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'احجز قص وتصفيف' })).toHaveAttribute(
      'href',
      /\/ar\/shops\/barber-house\/book\?service=/,
    );

    // Barbers link to their profiles.
    await page.getByRole('tab', { name: 'الحلاقون' }).click();
    await expect(page).toHaveURL(/tab=professionals/);
    await expect(page.getByRole('link', { name: 'عمر السالم' })).toHaveAttribute(
      'href',
      `/ar/shops/${BARBER_HOUSE}/professionals/omar`,
    );

    // Reviews show first name + initial only (D-017), linked to the booked service.
    await page.getByRole('tab', { name: 'التقييمات' }).click();
    const reviews = page.getByRole('tabpanel', { name: 'التقييمات' });
    await expect(reviews).toContainText('خالد د.');
    await expect(reviews).not.toContainText('الدوسري');
    await expect(reviews).toContainText('قص وتصفيف');

    // About: hours past midnight, the cancellation policy from the platform settings, the map.
    await page.getByRole('tab', { name: 'عن المحل' }).click();
    const about = page.getByRole('tabpanel', { name: 'عن المحل' });
    await expect(about).toContainText('الإلغاء مجاني حتى ساعتين قبل الموعد');
    await expect(about.getByRole('row', { name: /الخميس/ })).toContainText('١:٠٠ ص');
    await expect(page.getByTestId('shop-mini-map')).toBeVisible();

    const data = await jsonLd(page);
    const business = data.find((d) => d['@type'] === 'HairSalon')!;
    expect(business).toMatchObject({ name: 'باربر هاوس', geo: { '@type': 'GeoCoordinates' } });
    expect(business.aggregateRating).toMatchObject({ '@type': 'AggregateRating', reviewCount: 2 });
    expect(data.map((d) => d['@type'])).toContain('BreadcrumbList');
    await expect(page.locator('link[rel="alternate"][hreflang="en"]')).toHaveAttribute(
      'href',
      /\/en\/shops\/barber-house$/,
    );

    // No customer or professional phone anywhere in the page (spec §7/§8); the shop's own number is business data.
    const html = await page.content();
    expect(html).not.toMatch(/\+96650010/);
    await expectNoSeriousAxe(page, `/ar/shops/${BARBER_HOUSE}`);
  });

  test('professional profile: services, next free times and reviews, in English too', async ({ page }) => {
    await page.goto(`/en/shops/${BARBER_HOUSE}/professionals/omar`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Omar Al-Salem');
    await expect(page.getByRole('link', { name: /Works at Barber House/ })).toHaveAttribute(
      'href',
      `/en/shops/${BARBER_HOUSE}`,
    );
    await expect(page.getByRole('heading', { name: 'Services offered' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Book with Omar' })).toHaveAttribute(
      'href',
      /\/en\/shops\/barber-house\/book\?pro=/,
    );
    const data = await jsonLd(page);
    expect(data.find((d) => d['@type'] === 'Person')).toMatchObject({
      name: 'Omar Al-Salem',
      worksFor: { name: 'Barber House' },
    });
    expect(await page.content()).not.toMatch(/\+96650010/);
    await expectNoSeriousAxe(page, 'professional en');
  });

  test('discover page, /shops listing and legal pages are accessible, and personalised pages are not indexed', async ({
    page,
  }) => {
    await page.goto('/ar/discover');
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', /noindex/);
    await expect(page.getByRole('heading', { name: /قريب منك|الأعلى تقييماً/ })).toBeVisible();
    await expectNoSeriousAxe(page, '/ar/discover');

    await page.goto('/ar/shops');
    await expect(page.locator('meta[name="robots"]')).toHaveCount(0);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('صالونات الحلاقة');
    await expect(page.getByRole('link', { name: 'باربر هاوس' })).toBeVisible();
    await expectNoSeriousAxe(page, '/ar/shops');

    await page.goto('/en/terms');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Terms of use');
    await expect(page.getByText('Draft, pending legal review before launch.')).toBeVisible();
    await expectNoSeriousAxe(page, '/en/terms');
  });

  test('private_routes_emit_noindex, robots.txt and sitemap_lists_shops', async ({
    page,
    request,
    baseURL,
  }) => {
    for (const path of ['/ar/search', '/en/discover', '/ar/auth/sign-in', '/ar/account']) {
      await page.goto(path);
      await expect(page.locator('meta[name="robots"]'), path).toHaveAttribute('content', /noindex/);
    }

    const robots = await (await request.get('/robots.txt')).text();
    expect(robots).toContain('Disallow: /ar/admin');
    expect(robots).toContain('Disallow: /ar/shop$');
    // The public shop pages stay crawlable: only the dashboard (/ar/shop) and the booking wizard are excluded.
    const lines = robots.split(/\r?\n/).map((line) => line.trim());
    expect(lines).not.toContain('Disallow: /ar/shops');
    expect(lines).not.toContain('Disallow: /ar/shops/');
    // The origin is runtime configuration (TRIMME_SITE_URL), which the stack sets to the address under test.
    expect(robots).toContain(`Sitemap: ${new URL('/sitemap.xml', baseURL).toString()}`);
    expect(robots).toContain('Disallow: /ar/shops/*/book');

    const sitemap = await (await request.get('/sitemap.xml')).text();
    expect(sitemap).toContain(`/ar/shops/${BARBER_HOUSE}</loc>`);
    expect(sitemap).toContain(`/ar/shops/${BARBER_HOUSE}/professionals/omar</loc>`);
    expect(sitemap).toMatch(new RegExp(`hreflang="en"[^>]*/en/shops/${BARBER_HOUSE}"`));
    expect(sitemap).not.toContain('lamsat-al-rajul');
  });

  test('captures the public pages at 390 / 768 / 1440 in RTL and LTR, with no horizontal overflow', async ({
    page,
  }, testInfo) => {
    test.slow();
    await stubTiles(page);
    const paths = [
      '/ar',
      '/en',
      '/ar/discover',
      '/ar/search',
      `/ar/shops/${BARBER_HOUSE}`,
      `/en/shops/${BARBER_HOUSE}`,
      '/ar/shops',
    ];
    const failures: string[] = [];
    for (const width of [390, 768, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      for (const path of paths) {
        await page.goto(path, { waitUntil: 'load' });
        const overflow = await page.evaluate(async () => {
          await document.fonts.ready;
          return document.documentElement.scrollWidth - document.documentElement.clientWidth;
        });
        if (overflow > 0) failures.push(`${path} @${width}px overflows by ${overflow}px`);
      }
    }
    expect(failures).toEqual([]);
    await captureViewports(page, testInfo, 'landing-ar', '/ar');
    await captureViewports(page, testInfo, 'shop-ar', `/ar/shops/${BARBER_HOUSE}`);
    await captureViewports(page, testInfo, 'shop-en', `/en/shops/${BARBER_HOUSE}`);
    await captureViewports(page, testInfo, 'search-ar', '/ar/search');
  });
});
