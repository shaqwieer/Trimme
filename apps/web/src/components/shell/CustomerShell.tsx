import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import { CustomerNav } from './CustomerNav';
import { ThemeMenu } from '@/components/theme/ThemeSelector';
import { LanguageSwitcher } from './LanguageSwitcher';

/**
 * Customer app shell (mobile-first). The bell opens the notifications page; the signed-in account area passes a bell
 * with the live unread count. Below 1200px the five primary destinations live in a bottom bar
 * (design: 5 items, min 44×44, active = colour + bold); from 1200px they move into the header.
 */
export function CustomerShell({ children, bell }: { children: ReactNode; bell?: ReactNode }) {
  const t = useTranslations();

  return (
    <div className="flex min-h-dvh flex-col bg-bg-page">
      <a href="#main" className="skip-link">
        {t('common.skipToContent')}
      </a>
      <header className="sticky top-0 z-20 border-b border-border-subtle bg-header-glass backdrop-blur-md">
        <div className="mx-auto flex h-16 max-w-[1280px] items-center gap-4 px-4 md:px-6 xl:px-10">
          <Link href="/" className="inline-flex min-h-11 items-center rounded-field">
            <Logo height={34} priority />
          </Link>
          <CustomerNav placement="header" />
          <div className="ms-auto flex items-center gap-2">
            <LanguageSwitcher className="hidden md:inline-flex" />
            <ThemeMenu />
            {bell ?? (
              <Link
                href="/account/notifications"
                aria-label={t('shell.notifications')}
                className="inline-flex size-11 shrink-0 items-center justify-center rounded-button text-text-strong transition-colors hover:bg-brand-100"
              >
                <Icon name="bell" className="size-[19px]" />
              </Link>
            )}
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
