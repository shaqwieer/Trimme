'use client';

import { Check, ChevronDown, Languages } from 'lucide-react';
import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useRef, useState } from 'react';
import { Link, usePathname } from '@/i18n/navigation';
import { routing } from '@/i18n/routing';
import { cn } from '@/lib/cn';

/**
 * Header language switch: a compact button (icon and the current language's short name, "Language: العربية" to
 * assistive technology) that discloses one link per locale, each named in its own language, to the same page in that
 * language. Built like the theme menu (D-124): no menu library, and Escape, a click outside or tabbing away closes it,
 * with focus back on the button.
 */
export function LanguageSwitcher({
  className,
  tone = 'default',
  align = 'end',
}: {
  className?: string;
  tone?: 'default' | 'onDark';
  /** Which edge of the button the panel lines up with. */
  align?: 'start' | 'end';
}) {
  const t = useTranslations('common.languageSwitch');
  const locale = useLocale();
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const id = useId();

  useEffect(() => {
    if (!open) return;
    rootRef.current?.querySelector<HTMLAnchorElement>('a[aria-current]')?.focus();
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    // Escape closes only this panel; like the theme menu, it runs first, on the window in the capture phase.
    const onEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.preventDefault();
      setOpen(false);
      buttonRef.current?.focus();
    };
    document.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('keydown', onEscape, true);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      window.removeEventListener('keydown', onEscape, true);
    };
  }, [open]);

  return (
    <div
      ref={rootRef}
      className={cn('relative', className)}
      onBlur={(event) => {
        if (open && !event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false);
      }}
    >
      <button
        ref={buttonRef}
        type="button"
        aria-label={`${t('label')}: ${t(locale)}`}
        aria-expanded={open}
        aria-controls={open ? `${id}-panel` : undefined}
        data-testid="language-menu"
        onClick={() => setOpen((value) => !value)}
        className={cn(
          'group inline-flex min-h-11 items-center gap-1.5 rounded-field px-3 text-label font-bold transition-colors',
          tone === 'onDark'
            ? 'text-on-chrome-muted hover:bg-on-chrome-subtle hover:text-on-chrome aria-expanded:bg-on-chrome-subtle'
            : 'border border-border-input bg-surface text-text-strong hover:bg-brand-100 aria-expanded:bg-brand-100',
        )}
      >
        <Languages aria-hidden="true" className="size-4" strokeWidth={1.75} />
        <span>{t('short')}</span>
        <ChevronDown
          aria-hidden="true"
          className="size-3.5 transition-transform group-aria-expanded:rotate-180"
          strokeWidth={2}
        />
      </button>
      {open && (
        <div
          id={`${id}-panel`}
          className={cn(
            'absolute top-full z-50 mt-1.5 min-w-[180px] rounded-button border border-border bg-surface p-1.5 shadow-e3',
            align === 'end' ? 'end-0' : 'start-0',
          )}
        >
          <p id={`${id}-label`} className="px-3 pt-1.5 pb-1 text-helper font-bold text-text-secondary">
            {t('label')}
          </p>
          <ul aria-labelledby={`${id}-label`} className="flex flex-col">
            {routing.locales.map((option) => {
              const current = option === locale;
              return (
                <li key={option}>
                  <Link
                    href={pathname}
                    locale={option}
                    hrefLang={option}
                    lang={option}
                    aria-current={current ? 'true' : undefined}
                    // Keep focus where it is: Safari does not focus a pressed link, so the press would blur to <body>
                    // and the panel's blur handler would close it before the click navigates.
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => setOpen(false)}
                    className={cn(
                      'flex min-h-11 items-center gap-2.5 rounded-sm px-3 text-[0.875rem] text-text-primary hover:bg-bg-page',
                      current && 'font-bold',
                    )}
                  >
                    <span className="flex-1">{t(option)}</span>
                    {current && (
                      <Check aria-hidden="true" className="size-4 text-brand-700" strokeWidth={2.25} />
                    )}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
