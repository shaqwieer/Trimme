import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import type { BrowserContext, Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';
import { png } from '../support/images';
import {
  ADMIN,
  type AuditRoute,
  bookFaisal,
  cancelBooking,
  customerSignUp,
  firstId,
  OWNER,
  type Role,
  routes,
  type RuntimeIds,
  staffSignIn,
} from './inventory';

/**
 * Phase 17 route audit (17.2–17.4): every page, as the role that can open it, in Arabic and English. On each page:
 * - accessibility: axe (WCAG 2.0/2.1/2.2 A and AA) with no serious or critical finding;
 * - language: `lang`/`dir`, no raw message key, no `MISSING_MESSAGE`, no UI string of the other locale's catalogue,
 *   no Arabic-Indic digits in English;
 * - indexing: `noindex` on every private page and none on public ones, which carry a canonical URL and hreflang;
 * - no page error, no horizontal overflow at 390 px, and (through the fixture) no CSP violation.
 * Problems are collected per page and reported together, so one run lists everything.
 */
const LOCALES = ['ar', 'en'] as const;
type Locale = (typeof LOCALES)[number];
const ROLES: Role[] = ['anonymous', 'customer', 'owner', 'admin'];
const BLANK_TILE = png(256, 256, [233, 238, 243]);
const AXE_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

type Catalog = Record<string, string>;

function flatten(value: unknown, prefix = '', out: Catalog = {}): Catalog {
  if (typeof value === 'string') out[prefix] = value;
  else if (value && typeof value === 'object')
    for (const [key, child] of Object.entries(value)) flatten(child, prefix ? `${prefix}.${key}` : key, out);
  return out;
}

const catalog = (locale: Locale) =>
  flatten(
    JSON.parse(readFileSync(new URL(`../../../apps/web/messages/${locale}.json`, import.meta.url), 'utf8')),
  );
const AR = catalog('ar');
const EN = catalog('en');
const plain = (value: string) => !/[{}<>]/.test(value) && value.trim().length > 1;
const ARABIC = /[؀-ۿ]/;

/** UI strings that exist only in one locale's catalogue: seeing one in the other locale is an untranslated string. */
const ONLY_AR = new Set(
  Object.entries(AR)
    .filter(([key, value]) => plain(value) && ARABIC.test(value) && EN[key] !== value)
    .map(([, value]) => value.trim()),
);
const AR_VALUES = new Set(Object.values(AR).map((value) => value.trim()));
const ONLY_EN = new Set(
  Object.entries(EN)
    .filter(
      ([key, value]) =>
        plain(value) && /[A-Za-z]{3}/.test(value) && AR[key] !== value && !AR_VALUES.has(value.trim()),
    )
    .map(([, value]) => value.trim()),
);
const RAW_KEY = /^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9_]+){1,}$/;

/** Data that happens to equal a catalogue string, by page: reviewed, not UI text. */
const DATA_STRINGS: Array<{ path: RegExp; text: string; why: string }> = [
  {
    path: /\/admin\/audit/,
    text: 'مالك المحل',
    why: "the seeded shop owner's display name, shown as the actor",
  },
];

async function stubTiles(page: Page) {
  await page.route('https://tile.openstreetmap.org/**', (route) =>
    route.fulfill({ status: 200, contentType: 'image/png', body: BLANK_TILE }),
  );
}

/**
 * Every visible text and every accessible-name attribute on the page, except content that declares its own language
 * (`lang` on an element inside the body: a message in the customer's language, a language's own name).
 */
async function pageStrings(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const strings: string[] = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const parent = node.parentElement;
      if (!parent || parent.closest('script, style, noscript, template, body [lang]')) continue;
      const text = node.textContent?.trim();
      if (text) strings.push(text);
    }
    for (const element of document.body.querySelectorAll('[aria-label], [placeholder], [title], img[alt]')) {
      if (element.closest('body [lang]')) continue;
      for (const name of ['aria-label', 'placeholder', 'title', 'alt']) {
        const value = element.getAttribute(name)?.trim();
        if (value) strings.push(value);
      }
    }
    return strings;
  });
}

/**
 * JSON-LD (spec §6, R-WEB-11): every block parses, uses schema.org and names a type; a rating appears only with real
 * reviews and a value from 1 to 5.
 */
async function structuredDataProblems(page: Page): Promise<string[]> {
  const blocks = await page.locator('script[type="application/ld+json"]').allTextContents();
  const problems: string[] = [];
  const visit = (node: unknown) => {
    if (Array.isArray(node)) return node.forEach(visit);
    if (!node || typeof node !== 'object') return;
    const item = node as Record<string, unknown>;
    if (item['@type'] === 'AggregateRating') {
      const count = Number(item.reviewCount);
      const value = Number(item.ratingValue);
      if (!(count > 0) || !(value >= 1 && value <= 5))
        problems.push(`AggregateRating ${value} from ${count} reviews`);
    }
    Object.values(item).forEach(visit);
  };
  for (const block of blocks) {
    try {
      const data = JSON.parse(block) as Record<string, unknown> | Array<Record<string, unknown>>;
      for (const item of Array.isArray(data) ? data : [data]) {
        if (item['@context'] !== 'https://schema.org' || typeof item['@type'] !== 'string') {
          problems.push(`JSON-LD without schema.org context or type: ${block.slice(0, 80)}`);
        }
      }
      visit(data);
    } catch {
      problems.push(`JSON-LD does not parse: ${block.slice(0, 80)}`);
    }
  }
  return problems;
}

async function auditPage(
  page: Page,
  locale: Locale,
  route: AuditRoute,
  origin: string,
  colorScheme: 'light' | 'dark' | 'no-preference' | null,
): Promise<string[]> {
  const problems: string[] = [];
  const errors: string[] = [];
  const onError = (error: Error) => errors.push(error.message);
  const onConsole = (message: { text(): string }) => {
    if (/MISSING_MESSAGE|IntlError/.test(message.text())) errors.push(message.text());
  };
  page.on('pageerror', onError);
  page.on('console', onConsole);
  try {
    await page.setViewportSize({ width: 1280, height: 900 });
    const url = `/${locale}${route.path === '/' ? '' : route.path}`;
    await page.goto(url, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    if (route.map) await page.waitForTimeout(800);

    if (colorScheme === 'dark') {
      const background = await page.evaluate(
        () => getComputedStyle(document.documentElement).backgroundColor,
      );
      if (background !== 'rgb(12, 22, 32)')
        problems.push(`not in the dark theme (page background ${background})`);
    }

    const head = await page.evaluate(() => ({
      lang: document.documentElement.getAttribute('lang'),
      dir: document.documentElement.getAttribute('dir'),
      title: document.title.trim(),
      robots: document.querySelector('meta[name="robots"]')?.getAttribute('content') ?? '',
      canonical: document.querySelector('link[rel="canonical"]')?.getAttribute('href') ?? null,
      hreflang: [...document.querySelectorAll('link[rel="alternate"][hreflang]')].map((l) =>
        l.getAttribute('hreflang'),
      ),
      ogImage: document.querySelectorAll('meta[property="og:image"]').length,
    }));
    if (head.lang !== locale) problems.push(`lang is ${head.lang}`);
    if (head.dir !== (locale === 'ar' ? 'rtl' : 'ltr')) problems.push(`dir is ${head.dir}`);
    if (!head.title) problems.push('empty <title>');
    if (route.indexable) {
      if (head.robots.includes('noindex')) problems.push('public page is noindex');
      const expected = new URL(url.split('?')[0]!, origin).toString();
      if (head.canonical !== expected) problems.push(`canonical ${head.canonical} ≠ ${expected}`);
      for (const lang of ['ar', 'en', 'x-default']) {
        if (head.hreflang.filter((h) => h === lang).length !== 1) problems.push(`hreflang ${lang} missing`);
      }
      if (head.ogImage === 0) problems.push('no og:image');
      problems.push(...(await structuredDataProblems(page)));
    } else if (!head.robots.includes('noindex')) {
      problems.push(`private page without noindex (robots="${head.robots}")`);
    }

    for (const text of await pageStrings(page)) {
      if (DATA_STRINGS.some((d) => d.text === text && d.path.test(route.path))) continue;
      if (RAW_KEY.test(text)) problems.push(`raw message key "${text}"`);
      if (locale === 'en' && ONLY_AR.has(text)) problems.push(`Arabic UI string in English: "${text}"`);
      if (locale === 'ar' && ONLY_EN.has(text)) problems.push(`English UI string in Arabic: "${text}"`);
      if (locale === 'en' && /[٠-٩]/.test(text)) problems.push(`Arabic-Indic digits in English: "${text}"`);
    }

    const axe = await new AxeBuilder({ page }).withTags(AXE_TAGS).analyze();
    for (const violation of axe.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')) {
      problems.push(
        `axe ${violation.impact} ${violation.id}: ${violation.nodes
          .slice(0, 3)
          .map((n) => n.target.join(' '))
          .join(', ')}`,
      );
    }

    // A phone loads the page at its own width (resizing a loaded desktop page keeps some measured layouts).
    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload({ waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    if (overflow > 0) problems.push(`${overflow}px horizontal overflow at 390px`);

    problems.push(...errors.map((e) => `page error: ${e.slice(0, 200)}`));
  } finally {
    page.off('pageerror', onError);
    page.off('console', onConsole);
  }
  return problems.map((p) => `/${locale}${route.path}: ${p}`);
}

async function signedIn(context: BrowserContext, page: Page, role: Role): Promise<() => Promise<void>> {
  await page.goto('/ar/terms');
  if (role === 'admin') await staffSignIn(context, page.request, ADMIN.email, ADMIN.password);
  if (role === 'owner') await staffSignIn(context, page.request, OWNER.email, OWNER.password);
  if (role === 'customer') {
    await customerSignUp(context, page.request);
    const bookingId = await bookFaisal(context, page.request);
    runtime.bookingId = bookingId;
    return () => cancelBooking(context, page.request, bookingId);
  }
  return async () => {};
}

const runtime: RuntimeIds = { roleId: '', templateId: '', dispatchId: '', bookingId: '' };

test.describe('route audit (Phase 17)', () => {
  test.describe.configure({ mode: 'parallel' });

  for (const role of ROLES) {
    for (const locale of LOCALES) {
      test(`${role} pages in ${locale}: accessibility, language, indexing, overflow`, async ({
        browser,
        baseURL,
      }, testInfo) => {
        test.setTimeout(10 * 60_000);
        // The a11y-dark project audits the same pages with the OS in dark mode (D-124).
        const colorScheme = testInfo.project.use.colorScheme ?? 'light';
        const context = await browser.newContext({
          locale: locale === 'ar' ? 'ar-SA' : 'en-US',
          colorScheme,
        });
        const page = await context.newPage();
        await stubTiles(page);
        const cleanUp = await signedIn(context, page, role);
        if (role === 'admin') {
          runtime.roleId = await firstId(page.request, '/api/v1/admin/roles');
          runtime.templateId = await firstId(page.request, '/api/v1/admin/whatsapp/templates');
          runtime.dispatchId = await firstId(page.request, '/api/v1/admin/whatsapp/dispatches');
        }

        const problems: string[] = [];
        const visited = routes(role, runtime);
        try {
          for (const route of visited) {
            problems.push(...(await auditPage(page, locale, route, baseURL!, colorScheme)));
          }
        } finally {
          await cleanUp();
          await context.close();
        }
        await testInfo.attach('pages', {
          body: visited.map((r) => r.path).join('\n'),
          contentType: 'text/plain',
        });
        expect(problems, `${visited.length} pages`).toEqual([]);
      });
    }
  }
});
