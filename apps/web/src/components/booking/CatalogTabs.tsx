'use client';

import { type ReactNode, useId, useState } from 'react';
import { cn } from '@/lib/cn';

export type CatalogTab = {
  key: string;
  label: string;
  /** How many items are chosen from this tab, already formatted, with its spoken form; omitted at «٠» (D-148). */
  badge?: { text: string; label: string };
};

/**
 * The category tabs above the catalog's tiles (D-148, «٤-١»). Only the switching runs in the browser: the panels arrive
 * already drawn (the shop page renders them on the server), so a visitor downloads no tile or price code. Arrow keys
 * move between tabs, following the reading direction.
 */
export function CatalogTabs({
  label,
  tabs,
  panels,
  initial,
}: {
  /** The tab list's accessible name. */
  label: string;
  tabs: CatalogTab[];
  /** Each tab's content, by key. */
  panels: Record<string, ReactNode>;
  /** The tab open at first (the one holding a chosen item, in the booking). */
  initial?: string;
}) {
  const base = useId();
  const [active, setActive] = useState(initial ?? tabs[0]?.key);
  const current = tabs.find((tab) => tab.key === active) ?? tabs[0];
  if (!current) return null;
  const many = tabs.length > 1;

  return (
    <div className="flex min-w-0 flex-col gap-3" data-testid="service-catalog">
      {many && (
        <div
          role="tablist"
          aria-label={label}
          className="flex [scrollbar-width:thin] gap-2 overflow-x-auto pb-1"
          onKeyDown={(event) => {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
            const index = tabs.findIndex((tab) => tab.key === current.key);
            const forward = (event.key === 'ArrowLeft') === (document.dir === 'rtl');
            const next = tabs[(index + (forward ? 1 : -1) + tabs.length) % tabs.length]!;
            setActive(next.key);
            document.getElementById(`${base}-tab-${next.key}`)?.focus();
          }}
        >
          {tabs.map((tab) => {
            const on = tab.key === current.key;
            return (
              <button
                key={tab.key}
                id={`${base}-tab-${tab.key}`}
                type="button"
                role="tab"
                aria-selected={on}
                aria-controls={`${base}-panel`}
                tabIndex={on ? 0 : -1}
                onClick={() => setActive(tab.key)}
                className={cn(
                  'inline-flex min-h-11 shrink-0 items-center gap-1.5 rounded-full border px-4 text-label font-bold whitespace-nowrap transition-colors focus-visible:shadow-[var(--focus-ring)]',
                  on
                    ? 'border-navy-900 bg-navy-900 text-on-navy'
                    : 'border-border-input bg-surface text-text-strong hover:border-brand-500',
                )}
              >
                {tab.label}
                {tab.badge && (
                  <>
                    <span
                      aria-hidden="true"
                      className={cn(
                        'inline-flex size-5 items-center justify-center rounded-full text-[0.6875rem] leading-none',
                        on ? 'bg-on-navy text-navy-900' : 'bg-navy-900 text-on-navy',
                      )}
                    >
                      {tab.badge.text}
                    </span>
                    <span className="sr-only"> — {tab.badge.label}</span>
                  </>
                )}
              </button>
            );
          })}
        </div>
      )}
      <div
        id={`${base}-panel`}
        role={many ? 'tabpanel' : undefined}
        aria-labelledby={many ? `${base}-tab-${current.key}` : undefined}
      >
        {panels[current.key]}
      </div>
    </div>
  );
}
