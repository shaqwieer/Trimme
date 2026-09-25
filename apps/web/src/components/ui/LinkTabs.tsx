import type { ReactNode } from 'react';
import { Link } from '@/i18n/navigation';
import { cn } from '@/lib/cn';

type LinkTab = { href: string; label: ReactNode; active: boolean };

/**
 * Navigation tabs whose state lives in the URL (e.g. `/shops/[slug]?tab=reviews`), so each tab is
 * a shareable, crawlable link. Same visual language as <Tabs>.
 */
export function LinkTabs({ tabs, label, className }: { tabs: LinkTab[]; label: string; className?: string }) {
  return (
    <nav aria-label={label} className={className}>
      <ul className="flex gap-6 overflow-x-auto border-b border-border">
        {tabs.map((tab) => (
          <li key={tab.href}>
            <Link
              href={tab.href}
              aria-current={tab.active ? 'page' : undefined}
              scroll={false}
              className={cn(
                '-mb-px inline-flex min-h-11 items-end border-b-[2.5px] pb-2.5 text-button whitespace-nowrap transition-colors',
                tab.active
                  ? 'border-navy-900 font-bold text-navy-900'
                  : 'border-transparent text-text-secondary hover:text-text-primary',
              )}
            >
              {tab.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
