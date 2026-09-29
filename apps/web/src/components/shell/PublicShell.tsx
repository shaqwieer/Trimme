import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { ButtonLink } from '@/components/ui/Button';
import { Link } from '@/i18n/navigation';
import { LanguageSwitcher } from './LanguageSwitcher';

/**
 * Public pages. `marketing` (landing, listing, shop and legal pages) has the design's top navigation (c-landing
 * 826–835: logo, links, language switch, sign in) and a footer with the legal links (DV-A28). `minimal` (auth flows)
 * keeps only the logo and the language switch.
 */
export function PublicShell({
  children,
  variant = 'minimal',
}: {
  children: ReactNode;
  variant?: 'marketing' | 'minimal';
}) {
  const t = useTranslations('common');
  const nav = useTranslations('publicNav');

  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#main" className="skip-link">
        {t('skipToContent')}
      </a>
      <header className="sticky top-0 z-20 border-b border-border-subtle bg-header-glass backdrop-blur-md">
        <div className="mx-auto flex h-16 max-w-[1280px] items-center justify-between gap-4 px-4 md:px-6 xl:px-10">
          <div className="flex items-center gap-8">
            <Link href="/" className="rounded-field">
              <Logo height={38} priority />
            </Link>
            {variant === 'marketing' && (
              <nav aria-label={nav('label')} className="hidden md:block">
                <ul className="flex items-center gap-1">
                  <li>
                    <Link
                      href="/"
                      className="inline-flex min-h-11 items-center rounded-field px-3 text-label font-bold text-text-strong hover:bg-bg-subtle"
                    >
                      {nav('home')}
                    </Link>
                  </li>
                  <li>
                    <Link
                      href="/shops"
                      className="inline-flex min-h-11 items-center rounded-field px-3 text-label font-medium text-text-secondary hover:bg-bg-subtle"
                    >
                      {nav('shops')}
                    </Link>
                  </li>
                  <li>
                    <Link
                      href="/discover"
                      className="inline-flex min-h-11 items-center rounded-field px-3 text-label font-medium text-text-secondary hover:bg-bg-subtle"
                    >
                      {nav('discover')}
                    </Link>
                  </li>
                </ul>
              </nav>
            )}
          </div>
          <div className="flex items-center gap-2">
            <LanguageSwitcher />
            {variant === 'marketing' && (
              <ButtonLink href="/auth/sign-in" variant="primary" size="sm" className="hidden sm:inline-flex">
                {nav('signIn')}
              </ButtonLink>
            )}
          </div>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        {children}
      </main>
      {variant === 'marketing' && <SiteFooter />}
    </div>
  );
}

function SiteFooter() {
  const t = useTranslations('footer');

  return (
    <footer className="border-t border-border bg-surface">
      <div className="mx-auto flex max-w-[1280px] flex-col gap-4 px-4 py-8 md:flex-row md:items-center md:justify-between md:px-6 xl:px-10">
        <div className="flex flex-col gap-2">
          <Logo height={30} />
          <p className="max-w-[46ch] text-helper text-text-secondary">{t('tagline')}</p>
        </div>
        <nav aria-label={t('label')}>
          <ul className="flex flex-wrap items-center gap-x-5 gap-y-2 text-helper">
            <li>
              <Link
                href="/shops"
                className="inline-flex min-h-11 items-center text-text-link hover:underline"
              >
                {t('shops')}
              </Link>
            </li>
            <li>
              <Link
                href="/terms"
                className="inline-flex min-h-11 items-center text-text-link hover:underline"
              >
                {t('terms')}
              </Link>
            </li>
            <li>
              <Link
                href="/privacy"
                className="inline-flex min-h-11 items-center text-text-link hover:underline"
              >
                {t('privacy')}
              </Link>
            </li>
          </ul>
        </nav>
        <p className="text-helper text-text-tertiary">{t('rights', { year: new Date().getFullYear() })}</p>
      </div>
    </footer>
  );
}
