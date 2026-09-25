import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('platform wiring', () => {
  test('the web origin proxies /api to the API (same-origin cookies)', async ({ request }) => {
    const response = await request.get('/api/v1/meta');
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    expect(body).toMatchObject({ name: 'TRIMME API', version: 'v1' });
  });

  test('web responses carry baseline security headers', async ({ request }) => {
    const response = await request.get('/ar');
    const headers = response.headers();
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['x-powered-by']).toBeUndefined();
  });
});

test.describe('accessibility baseline (R-WEB-09)', () => {
  for (const path of ['/ar', '/en']) {
    test(`no serious or critical axe violations on ${path}`, async ({ page }) => {
      await page.goto(path);
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
      expect(blocking.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
    });
  }
});
