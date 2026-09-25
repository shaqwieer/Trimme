'use client';

import { useTranslations } from 'next-intl';
import { Link, usePathname } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { activeHref, customerNav } from './navigation';

/** The customer's five primary destinations, rendered as a bottom bar (<1200px) or header links (≥1200px). */
export function CustomerNav({ placement }: { placement: 'bottom' | 'header' }) {
  const t = useTranslations('nav.customer');
  const tShell = useTranslations('shell');
  const pathname = usePathname();
  const current = activeHref(
    pathname,
    customerNav.map((item) => item.href),
  );

  if (placement === 'header') {
    return (
      <nav aria-label={tShell('mainNavigation')} className="hidden lg:block">
        <ul className="flex items-center gap-1">
          {customerNav.map(({ key, href, icon: Icon }) => {
            const active = current === href;
            return (
              <li key={key}>
                <Link
                  href={href}
                  aria-current={active ? 'page' : undefined}
                  className={cn(
                    'inline-flex min-h-11 items-center gap-2 rounded-field px-3 text-label transition-colors',
                    active
                      ? 'bg-brand-100 font-bold text-navy-900'
                      : 'font-medium text-text-secondary hover:bg-bg-subtle',
                  )}
                >
                  <Icon aria-hidden="true" className="size-[18px]" strokeWidth={1.75} />
                  {t(key)}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
    );
  }

  return (
    <nav
      aria-label={tShell('mainNavigation')}
      className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
    >
      <ul className="mx-auto grid h-[var(--layout-bottom-nav-height)] max-w-[640px] grid-cols-5">
        {customerNav.map(({ key, href, icon: Icon }) => {
          const active = current === href;
          return (
            <li key={key} className="flex">
              <Link
                href={href}
                aria-current={active ? 'page' : undefined}
                className={cn(
                  'flex min-h-11 flex-1 flex-col items-center justify-center gap-1 text-nav transition-colors',
                  active ? 'font-bold text-navy-900' : 'font-medium text-text-secondary',
                )}
              >
                <Icon aria-hidden="true" className="size-5" strokeWidth={active ? 2 : 1.75} />
                {t(key)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
