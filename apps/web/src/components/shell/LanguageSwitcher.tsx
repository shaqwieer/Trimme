'use client';

import { Languages } from 'lucide-react';
import { useLocale, useTranslations } from 'next-intl';
import { Link, usePathname } from '@/i18n/navigation';
import { cn } from '@/lib/cn';

/** Switches between Arabic (RTL) and English (LTR) while staying on the same page. */
export function LanguageSwitcher({
  className,
  tone = 'default',
}: {
  className?: string;
  tone?: 'default' | 'onDark';
}) {
  const t = useTranslations('common.languageSwitch');
  const locale = useLocale();
  const pathname = usePathname();
  const target = locale === 'ar' ? 'en' : 'ar';

  return (
    <Link
      href={pathname}
      locale={target}
      hrefLang={target}
      lang={target}
      aria-label={`${t('label')}: ${t(target)}`}
      className={cn(
        'inline-flex min-h-11 items-center gap-2 rounded-field px-3 text-label font-bold transition-colors',
        tone === 'onDark'
          ? 'text-on-navy-muted hover:bg-on-navy-subtle hover:text-on-navy'
          : 'border border-border-input bg-surface text-text-strong hover:bg-brand-100',
        className,
      )}
    >
      <Languages aria-hidden="true" className="size-4" strokeWidth={1.75} />
      <span>{t('switchTo')}</span>
    </Link>
  );
}
