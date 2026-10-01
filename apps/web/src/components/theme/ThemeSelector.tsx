'use client';

import { Check, type LucideIcon, Monitor, Moon, Sun } from 'lucide-react';
import { type KeyboardEvent, useEffect, useId, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { THEME_PREFERENCES, type ThemePreference } from '@/lib/theme/theme';
import { useTheme } from './ThemeProvider';

export const THEME_ICONS: Record<ThemePreference, LucideIcon> = { system: Monitor, light: Sun, dark: Moon };

/**
 * Header theme switch (D-124): an icon button named after the current choice ("Theme: System") that discloses a native
 * radio group. Native radios give arrow keys and RTL order for free; Escape, a click outside or tabbing away closes it,
 * and focus returns to the button. No menu library, so the public pages stay inside their bundle budget. Fits the 390px
 * headers next to the language switch, so it is reachable on every page, signed in or not.
 */
export function ThemeMenu({ className }: { className?: string }) {
  const t = useTranslations('common.theme');
  const { preference, setPreference } = useTheme();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const pointerChoice = useRef(false);
  const id = useId();
  const Current = THEME_ICONS[preference];

  const close = (returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) buttonRef.current?.focus();
  };

  useEffect(() => {
    if (!open) return;
    rootRef.current?.querySelector<HTMLInputElement>('input:checked')?.focus();
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!open) return;
    if (event.key === 'Escape' || (event.key === 'Enter' && event.target instanceof HTMLInputElement)) {
      event.preventDefault();
      event.stopPropagation();
      close(true);
    }
  };

  return (
    <div
      ref={rootRef}
      className={cn('relative', className)}
      onKeyDown={onKeyDown}
      onBlur={(event) => {
        if (open && !event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false);
      }}
    >
      <button
        ref={buttonRef}
        type="button"
        aria-label={t('menuLabel', { value: t(preference) })}
        aria-expanded={open}
        aria-controls={open ? `${id}-panel` : undefined}
        data-testid="theme-menu"
        onClick={() => setOpen((value) => !value)}
        className="inline-flex size-11 shrink-0 items-center justify-center rounded-button text-text-strong transition-colors hover:bg-brand-100 aria-expanded:bg-brand-100"
      >
        <Current aria-hidden="true" className="size-[19px]" strokeWidth={1.75} />
      </button>
      {open && (
        <div
          id={`${id}-panel`}
          className="absolute end-0 top-full z-50 mt-1.5 min-w-[200px] rounded-button border border-border bg-surface p-1.5 shadow-e3"
        >
          <p id={`${id}-label`} className="px-3 pt-1.5 pb-1 text-helper font-bold text-text-secondary">
            {t('label')}
          </p>
          <div role="radiogroup" aria-labelledby={`${id}-label`} className="flex flex-col">
            {THEME_PREFERENCES.map((option) => {
              const OptionIcon = THEME_ICONS[option];
              const checked = preference === option;
              return (
                <label
                  key={option}
                  onPointerDown={() => {
                    pointerChoice.current = true;
                  }}
                  // Keep focus on the group: a label is not focusable, so the press would blur to <body> and the
                  // panel's blur handler would close it before the click selects the option.
                  onMouseDown={(event) => event.preventDefault()}
                  className="flex min-h-11 cursor-pointer items-center gap-2.5 rounded-sm px-3 text-[0.875rem] text-text-primary select-none hover:bg-bg-page has-checked:font-bold has-focus-visible:bg-bg-page has-focus-visible:shadow-[var(--focus-ring)]"
                >
                  <input
                    type="radio"
                    name={`${id}-theme`}
                    value={option}
                    checked={checked}
                    onChange={() => {
                      setPreference(option);
                      // A pointer choice is final; arrow keys preview and Enter or Escape closes.
                      if (pointerChoice.current) close(true);
                      pointerChoice.current = false;
                    }}
                    className="sr-only"
                  />
                  <OptionIcon
                    aria-hidden="true"
                    className="size-[17px] text-text-strong"
                    strokeWidth={1.75}
                  />
                  <span className="flex-1">{t(option)}</span>
                  {checked && (
                    <Check aria-hidden="true" className="size-4 text-brand-700" strokeWidth={2.25} />
                  )}
                </label>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
