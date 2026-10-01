import type { Metadata, Viewport } from 'next';
import { Inter, Tajawal } from 'next/font/google';
import { locale as localeParam } from 'next/root-params';
import { notFound } from 'next/navigation';
import { connection } from 'next/server';
import { hasLocale, NextIntlClientProvider } from 'next-intl';
import { getTranslations } from 'next-intl/server';
import { DirectionProvider } from '@/components/providers/DirectionProvider';
import { localeDirection, routing } from '@/i18n/routing';
import { OG_IMAGE, OG_LOCALE, siteUrl } from '@/lib/seo/site';
import { BRAND_NAVY_900 } from '@/styles/brand';
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
 * contains Arabic glyphs and would otherwise capture Arabic text before Tajawal.
 */
const inter = Inter({
  subsets: ['latin'],
  weight: ['400', '500', '600', '700', '800'],
  variable: '--font-inter',
  display: 'swap',
  adjustFontFallback: false,
  fallback: [],
  // Not preloaded (Phase 17 Lighthouse): nine preloaded font files competed with the page's largest paint. Tajawal,
  // which covers Arabic and has Latin of its own, stays preloaded; Inter swaps in when the stylesheet first uses it.
  preload: false,
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
    title: { default: t('title'), template: `%s · ${t('appName')}` },
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
  };
}

export const viewport: Viewport = {
  themeColor: BRAND_NAVY_900,
  width: 'device-width',
  initialScale: 1,
};

export default async function LocaleLayout({ children }: LayoutProps<'/[locale]'>) {
  // Every page renders per request so its scripts carry that request's CSP nonce (D-117); a prerendered page would
  // have none, and the browser would block its scripts.
  await connection();
  const locale = await localeParam();
  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }

  return (
    <html lang={locale} dir={localeDirection[locale]} className={`${tajawal.variable} ${inter.variable}`}>
      <body>
        <NextIntlClientProvider>
          <DirectionProvider dir={localeDirection[locale]}>{children}</DirectionProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
