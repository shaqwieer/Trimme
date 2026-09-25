import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { Link } from '@/i18n/navigation';
import { LanguageSwitcher } from './LanguageSwitcher';

/** Public pages (landing, shop pages, auth): glass header with the logo and language switch. */
export function PublicShell({ children }: { children: ReactNode }) {
  const t = useTranslations('common');

  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#main" className="skip-link">
        {t('skipToContent')}
      </a>
      <header className="sticky top-0 z-20 border-b border-border-subtle bg-header-glass backdrop-blur-md">
        <div className="mx-auto flex h-16 max-w-[1280px] items-center justify-between gap-4 px-4 md:px-6 xl:px-10">
          <Link href="/" className="rounded-field">
            <Logo height={38} priority />
          </Link>
          <LanguageSwitcher />
        </div>
      </header>
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        {children}
      </main>
    </div>
  );
}
