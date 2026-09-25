import type { Page, TestInfo } from '@playwright/test';

/** Design reference widths (design/reference/README.md): phone, tablet, desktop. */
export const VIEWPORTS = [
  { name: 'phone', width: 390, height: 844 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'desktop', width: 1440, height: 900 },
] as const;

/**
 * Visits `path` at each reference width and attaches a full-page screenshot to the test report,
 * so layouts can be compared against design/reference at 390 / 768 / 1440.
 */
export async function captureViewports(page: Page, testInfo: TestInfo, name: string, path: string) {
  for (const viewport of VIEWPORTS) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.goto(path);
    await page.waitForLoadState('networkidle');
    await testInfo.attach(`${name}-${viewport.width}`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  }
}
