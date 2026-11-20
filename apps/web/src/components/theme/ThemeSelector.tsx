'use client';

import { type LucideIcon, Monitor, Moon, Sun } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import type { ThemePreference } from '@/lib/theme/theme';
import { useTheme } from './ThemeProvider';

/** The account setting's icons (D-124); the header switch shows only the sun and the moon. */
export const THEME_ICONS: Record<ThemePreference, LucideIcon> = { system: Monitor, light: Sun, dark: Moon };

/** What the page shows now: the explicit choice, or the device's under «تلقائي». */
function shownTheme(preference: ThemePreference): 'light' | 'dark' {
  if (preference !== 'system') return preference;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/**
 * The header's day/night switch (D-156, the client: the D-124 menu showed a monitor under «تلقائي», which nobody read as
 * the theme). A sun by day and a moon by night; one press flips what the page shows and saves it as an explicit choice.
 * «تلقائي» stays in the account settings. Both icons are drawn and the `dark:` variant shows one, so a visitor on
 * «تلقائي» with a dark device sees the moon on the first paint, with no script. The name is fixed: the server cannot
 * know the device's theme under «تلقائي», so a name or pressed state that followed it could contradict the HTML.
 * It sits next to the language switch in the public and customer headers; on dashboards it moves into the navy
 * navigation drawer below 768px (`tone="onChrome"`), where the header has no room left for it.
 */
export function ThemeToggle({
  tone = 'default',
}: {
  /** `onChrome` for the navy sidebar/drawer. */
  tone?: 'default' | 'onChrome';
}) {
  const t = useTranslations('common.theme');
  const { preference, setPreference } = useTheme();
  const label = t('toggle');

  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      data-testid="theme-toggle"
      onClick={() => setPreference(shownTheme(preference) === 'dark' ? 'light' : 'dark')}
      className={cn(
        'inline-flex size-11 shrink-0 items-center justify-center rounded-button transition-colors',
        tone === 'onChrome'
          ? 'text-on-chrome-muted hover:bg-on-chrome-subtle hover:text-on-chrome'
          : 'text-text-strong hover:bg-brand-100',
      )}
    >
      <Sun aria-hidden="true" data-icon="sun" className="size-[19px] dark:hidden" strokeWidth={1.75} />
      <Moon
        aria-hidden="true"
        data-icon="moon"
        className="hidden size-[19px] dark:block"
        strokeWidth={1.75}
      />
    </button>
  );
}
