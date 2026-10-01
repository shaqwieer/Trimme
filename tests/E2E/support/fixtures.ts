import { type Browser, type BrowserContext, test as base, expect, type Page } from '@playwright/test';

/**
 * Every spec imports `test` and `expect` from here. Any Content Security Policy violation in any page of any context
 * the test opens fails the test (D-117): a blocked script, style, map tile or worker would otherwise go unnoticed,
 * because most pages still render their server HTML without it.
 */
const violations: string[] = [];
const watched = new WeakSet<BrowserContext>();
const CSP_MESSAGE = /Content Security Policy/i;

function watchPage(page: Page) {
  page.on('console', (message) => {
    if (message.type() === 'error' && CSP_MESSAGE.test(message.text())) {
      violations.push(`${page.url()}: ${message.text()}`);
    }
  });
}

function watchContext(context: BrowserContext) {
  if (watched.has(context)) {
    return;
  }
  watched.add(context);
  context.pages().forEach(watchPage);
  context.on('page', watchPage);
}

export const test = base.extend<{ cspGuard: void }, { browser: Browser }>({
  browser: [
    async ({ browser }, use) => {
      const newContext = browser.newContext.bind(browser);
      browser.newContext = async (options) => {
        const context = await newContext(options);
        watchContext(context);
        return context;
      };
      await use(browser);
    },
    { scope: 'worker' },
  ],
  context: async ({ context }, use) => {
    watchContext(context);
    await use(context);
  },
  cspGuard: [
    async ({}, use) => {
      violations.length = 0;
      await use();
      expect(violations, 'Content Security Policy violations').toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };
