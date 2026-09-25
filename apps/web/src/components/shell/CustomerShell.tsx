import type { ReactNode } from 'react';
import { Bell } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { Link } from '@/i18n/navigation';
import { CustomerNav } from './CustomerNav';
import { LanguageSwitcher } from './LanguageSwitcher';

/**
 * Customer app shell (mobile-first). Below 1200px the five primary destinations live in a bottom bar
 * (design: 5 items, min 44×44, active = colour + bold); from 1200px they move into the header.
 */
export function CustomerShell({ children }: { children: ReactNode }) {
  const t = useTranslations();

  return (
    <div className="flex min-h-dvh flex-col bg-bg-page">
      <a href="#main" className="skip-link">
        {t('common.skipToContent')}
      </a>
      <header className="sticky top-0 z-20 border-b border-border-subtle bg-header-glass backdrop-blur-md">
        <div className="mx-auto flex h-16 max-w-[1280px] items-center gap-4 px-4 md:px-6 xl:px-10">
          <Link href="/" className="rounded-field">
            <Logo height={34} priority />
          </Link>
          <CustomerNav placement="header" />
          <div className="ms-auto flex items-center gap-2">
            <LanguageSwitcher className="hidden md:inline-flex" />
            <button
              type="button"
              aria-label={t('shell.notifications')}
              className="inline-flex size-11 items-center justify-center rounded-button text-text-strong hover:bg-brand-100"
            >
              <Bell aria-hidden="true" className="size-5" strokeWidth={1.75} />
            </button>
          </div>
        </div>
      </header>
      <main
        id="main"
        tabIndex={-1}
        className="flex-1 pb-[calc(var(--layout-bottom-nav-height)+env(safe-area-inset-bottom))] outline-none lg:pb-0"
      >
        {children}
      </main>
      <CustomerNav placement="bottom" />
    </div>
  );
}
