'use client';

import { useTranslations } from 'next-intl';
import { SegmentedControl } from '@/components/ui/selection.client';
import { cn } from '@/lib/cn';
import { parseTheme, THEME_PREFERENCES } from '@/lib/theme/theme';
import { useTheme } from './ThemeProvider';
import { THEME_ICONS } from './ThemeSelector';

/** Settings form control (account page, D-124): a native radio group with visible labels. */
export function ThemeSettings({ className }: { className?: string }) {
  const t = useTranslations('common.theme');
  const { preference, setPreference } = useTheme();

  return (
    <div className={cn('flex flex-col gap-2', className)}>
      <SegmentedControl
        legend={t('label')}
        name="theme"
        value={preference}
        onValueChange={(value) => setPreference(parseTheme(value))}
        options={THEME_PREFERENCES.map((option) => {
          const OptionIcon = THEME_ICONS[option];
          return {
            value: option,
            label: (
              <>
                <OptionIcon aria-hidden="true" className="size-4" strokeWidth={1.75} />
                {t(option)}
              </>
            ),
          };
        })}
      />
      <p className="px-1 text-helper text-text-secondary">{t('systemHint')}</p>
    </div>
  );
}
