import type { Metadata, Viewport } from 'next';
import { Inter, Tajawal } from 'next/font/google';
import localFont from 'next/font/local';
import { locale as localeParam } from 'next/root-params';
import { notFound } from 'next/navigation';
import { cookies } from 'next/headers';
import { connection } from 'next/server';
import { hasLocale, NextIntlClientProvider } from 'next-intl';
import { getTranslations } from 'next-intl/server';
import { DirectionProvider } from '@/components/providers/DirectionProvider';
import { ThemeProvider } from '@/components/theme/ThemeProvider';
import { localeDirection, routing } from '@/i18n/routing';
import { OG_IMAGE, OG_LOCALE, siteUrl, siteVerification } from '@/lib/seo/site';
import { colorScheme, parseTheme, THEME_COOKIE, themeAttribute } from '@/lib/theme/theme';
import { BRAND_NAVY_900, DARK_BG_PAGE } from '@/styles/brand';
import '../globals.css';

const tajawal = Tajawal({
  subsets: ['arabic', 'latin'],
  weight: ['400', '500', '700', '800'],
  variable: '--font-tajawal',
  display: 'swap',
});

/**
 * Inter is listed first in the font stack so Latin letters and Latin digits render in Inter while Arabic
 * glyphs fall through to Tajawal. Its automatic metric-adjusted fallback (Arial) is disabled because Arial
 * contains Arabic glyphs and would otherwise capture Arabic text before Tajawal. `latin-ext` carries Turkish
 * letters (ğ, ş, İ, ı).
 */
const inter = Inter({
  subsets: ['latin', 'latin-ext'],
  weight: ['400', '500', '600', '700', '800'],
  variable: '--font-inter',
  display: 'swap',
  adjustFontFallback: false,
  fallback: [],
  // Not preloaded (Phase 17 Lighthouse): nine preloaded font files competed with the page's largest paint. Tajawal,
  // which covers Arabic and has Latin of its own, stays preloaded; Inter swaps in when the stylesheet first uses it.
  preload: false,
});

/**
 * The new Saudi Riyal sign (U+20C1, approved 20 February 2025; D-148): one glyph built from the official symbol (SAMA's
 * public-domain SVG), first in every font stack but limited to that one code point, so prices show the sign wherever
 * they are text. 508 bytes; browsers fetch it only when the sign is on the page.
 */
const riyal = localFont({
  src: '../fonts/saudi-riyal.woff2',
  variable: '--font-riyal',
  display: 'block',
  preload: false,
  declarations: [{ prop: 'unicode-range', value: 'U+20C1' }],
  // No generated "riyal Fallback" face: it would be local Arial without the unicode-range, first in every font stack,
  // and would draw all the text in Arial. The real fonts after it in the stack are the fallback.
  adjustFontFallback: false,
  fallback: [],
});

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata(): Promise<Metadata> {
  const param = await localeParam();
  const locale = hasLocale(routing.locales, param) ? param : routing.defaultLocale;
  const t = await getTranslations({ locale, namespace: 'metadata' });
  return {
    // Canonical, hreflang and Open Graph URLs resolve against the public origin (R-WEB-10).
    metadataBase: siteUrl(),
    title: { default: t('title'), template: `%s — ${t('appName')}` },
    description: t('description'),
    applicationName: t('appName'),
    // Defaults for every page; public pages set their own title, description, URL and, where they have one, image.
    openGraph: {
      type: 'website',
      siteName: t('appName'),
      locale: OG_LOCALE[locale],
      images: [{ ...OG_IMAGE, alt: t('appName') }],
    },
    twitter: { card: 'summary_large_image' },
    // Meta Business domain verification (D-141); absent unless the deployment sets the token.
    verification: siteVerification(),
  };
}

async function themePreference() {
  return parseTheme((await cookies()).get(THEME_COOKIE)?.value);
}

/**
 * `color-scheme` follows the saved theme (D-124), so the browser paints its canvas, scrollbars and form controls in the
 * right scheme before the stylesheet arrives. The browser UI colour follows the scheme in System mode.
 */
export async function generateViewport(): Promise<Viewport> {
  const preference = await themePreference();
  return {
    themeColor:
      preference === 'dark'
        ? DARK_BG_PAGE
        : preference === 'light'
          ? BRAND_NAVY_900
          : [
              { media: '(prefers-color-scheme: dark)', color: DARK_BG_PAGE },
              { media: '(prefers-color-scheme: light)', color: BRAND_NAVY_900 },
            ],
    colorScheme: colorScheme(preference),
    width: 'device-width',
    initialScale: 1,
  };
}

export default async function LocaleLayout({ children }: LayoutProps<'/[locale]'>) {
  // Every page renders per request so its scripts carry that request's CSP nonce (D-117); a prerendered page would
  // have none, and the browser would block its scripts.
  await connection();
  const locale = await localeParam();
  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }
  // The saved theme is rendered into the HTML (no flash, no hydration mismatch, no inline script); System has no
  // attribute and the stylesheet follows the OS directly (D-124).
  const theme = await themePreference();

  return (
    <html
      lang={locale}
      dir={localeDirection[locale]}
      data-theme={themeAttribute(theme)}
      className={`${tajawal.variable} ${inter.variable} ${riyal.variable}`}
    >
      <body>
        <NextIntlClientProvider>
          <ThemeProvider initial={theme}>
            <DirectionProvider dir={localeDirection[locale]}>{children}</DirectionProvider>
          </ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
