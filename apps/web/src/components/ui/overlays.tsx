'use client';

import { Dialog as RadixDialog, DropdownMenu as RadixMenu, Tooltip as RadixTooltip } from 'radix-ui';
import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { Button } from './Button';
import { type DesignIconName, Icon } from './icons';

/* ---------------------------------------------------------------- Dialog */

type DialogProps = {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  trigger?: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  size?: 'sm' | 'md' | 'lg';
};

const dialogSizes = { sm: 'max-w-[400px]', md: 'max-w-[520px]', lg: 'max-w-[720px]' } as const;

/**
 * Modal dialog on Radix: focus trap, Escape to close, focus returns to the trigger, background inert,
 * title/description wired to aria-labelledby/-describedby (design 704–713).
 */
export function Dialog({
  open,
  onOpenChange,
  trigger,
  title,
  description,
  children,
  footer,
  size = 'md',
}: DialogProps) {
  const t = useTranslations('ui');
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      {trigger && <RadixDialog.Trigger asChild>{trigger}</RadixDialog.Trigger>}
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-50 bg-overlay" />
        <RadixDialog.Content
          className={cn(
            'fixed start-1/2 top-1/2 z-50 flex max-h-[calc(100dvh-2rem)] w-[calc(100vw-2rem)] -translate-y-1/2 flex-col gap-4 overflow-y-auto rounded-card border border-border bg-surface p-5 shadow-e3 ltr:-translate-x-1/2 rtl:translate-x-1/2',
            dialogSizes[size],
          )}
        >
          <div className="flex items-start justify-between gap-4">
            <div className="flex flex-col gap-1.5">
              <RadixDialog.Title className="text-[1.03125rem] font-bold text-text-primary">
                {title}
              </RadixDialog.Title>
              {description ? (
                <RadixDialog.Description className="text-caption text-text-secondary">
                  {description}
                </RadixDialog.Description>
              ) : (
                <RadixDialog.Description className="sr-only">{title}</RadixDialog.Description>
              )}
            </div>
            <RadixDialog.Close
              aria-label={t('close')}
              className="-me-2 -mt-2 inline-flex size-11 shrink-0 items-center justify-center rounded-button text-text-tertiary hover:bg-bg-subtle"
            >
              <Icon name="x" className="size-5" />
            </RadixDialog.Close>
          </div>
          {children}
          {footer && <div className="flex flex-wrap gap-2.5">{footer}</div>}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}

type ConfirmDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  body: ReactNode;
  confirmLabel: ReactNode;
  cancelLabel?: ReactNode;
  onConfirm: () => void;
  tone?: 'danger' | 'default';
  loading?: boolean;
};

/**
 * Confirmation for consequential actions (design 704–713: icon tile, title, body, two equal buttons).
 * The safe action ("go back") receives initial focus for destructive confirmations.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  body,
  confirmLabel,
  cancelLabel,
  onConfirm,
  tone = 'danger',
  loading = false,
}: ConfirmDialogProps) {
  const t = useTranslations('ui');
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-50 bg-overlay" />
        <RadixDialog.Content
          role="alertdialog"
          className="fixed start-1/2 top-1/2 z-50 flex w-[calc(100vw-2rem)] max-w-[400px] -translate-y-1/2 flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e3 ltr:-translate-x-1/2 rtl:translate-x-1/2"
        >
          <span
            aria-hidden="true"
            className={cn(
              'flex size-11 items-center justify-center rounded-button',
              tone === 'danger' ? 'bg-danger-50 text-danger-500' : 'bg-brand-100 text-brand-700',
            )}
          >
            <Icon name={tone === 'danger' ? 'alert' : 'info'} className="size-[21px]" />
          </span>
          <RadixDialog.Title className="text-[1.03125rem] font-bold text-text-primary">
            {title}
          </RadixDialog.Title>
          <RadixDialog.Description className="text-caption leading-[1.8] text-text-secondary">
            {body}
          </RadixDialog.Description>
          <div className="mt-1 flex gap-2.5">
            <Button
              variant={tone === 'danger' ? 'dangerSolid' : 'primary'}
              size="md"
              fullWidth
              loading={loading}
              onClick={onConfirm}
            >
              {confirmLabel}
            </Button>
            <RadixDialog.Close asChild>
              <Button variant="outline" size="md" fullWidth autoFocus={tone === 'danger'}>
                {cancelLabel ?? t('cancel')}
              </Button>
            </RadixDialog.Close>
          </div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}

/* ---------------------------------------------------------------- Sheet (drawer) */

type SheetProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** `start` = inline-start edge (right in Arabic); `bottom` = mobile bottom sheet. */
  side?: 'start' | 'end' | 'bottom';
  title: string;
  /** Visually hide the title (it still names the dialog for assistive technology). */
  hideTitle?: boolean;
  tone?: 'light' | 'navy';
  children: ReactNode;
  className?: string;
  id?: string;
  /** Accessible name of the close button (defaults to "Close"). */
  closeLabel?: string;
  /** Element that opens the sheet. Radix returns focus to it on close, so always pass the opener here. */
  trigger?: ReactNode;
};

const sheetSides = {
  start: 'inset-y-0 start-0 h-full w-[min(var(--layout-sidebar-width),85vw)]',
  end: 'inset-y-0 end-0 h-full w-[min(420px,92vw)]',
  bottom: 'inset-x-0 bottom-0 max-h-[85dvh] rounded-t-section shadow-sheet',
} as const;

/** Side drawer / bottom sheet on Radix Dialog (dashboard navigation, appointment details, filters). */
export function Sheet({
  open,
  onOpenChange,
  side = 'end',
  title,
  hideTitle = false,
  tone = 'light',
  children,
  className,
  id,
  closeLabel,
  trigger,
}: SheetProps) {
  const t = useTranslations('ui');
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      {trigger && <RadixDialog.Trigger asChild>{trigger}</RadixDialog.Trigger>}
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-40 bg-overlay" />
        <RadixDialog.Content
          id={id}
          aria-describedby={undefined}
          className={cn(
            'fixed z-40 flex flex-col overflow-y-auto shadow-e3 outline-none',
            tone === 'navy' ? 'bg-navy-900' : 'bg-surface',
            sheetSides[side],
            className,
          )}
        >
          <RadixDialog.Title
            className={cn(
              'text-h3 font-bold',
              hideTitle ? 'sr-only' : 'px-5 pt-5',
              tone === 'navy' ? 'text-on-navy' : 'text-text-primary',
            )}
          >
            {title}
          </RadixDialog.Title>
          <RadixDialog.Close
            aria-label={closeLabel ?? t('close')}
            className={cn(
              'absolute end-2 top-3 z-10 inline-flex size-11 items-center justify-center rounded-button',
              tone === 'navy'
                ? 'text-on-navy-muted hover:bg-on-navy-subtle hover:text-on-navy'
                : 'text-text-tertiary hover:bg-bg-subtle',
            )}
          >
            <Icon name="x" className="size-5" />
          </RadixDialog.Close>
          {children}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}

/* ---------------------------------------------------------------- Tooltip */

/**
 * Supplementary hint on hover/focus (design 702). Never the only source of essential information:
 * touch users cannot hover, so critical text must also be visible or in the accessible name.
 */
export function Tooltip({ content, children }: { content: ReactNode; children: ReactNode }) {
  return (
    <RadixTooltip.Provider delayDuration={300}>
      <RadixTooltip.Root>
        <RadixTooltip.Trigger asChild>{children}</RadixTooltip.Trigger>
        <RadixTooltip.Portal>
          <RadixTooltip.Content
            sideOffset={6}
            className="z-60 max-w-[260px] rounded-sm bg-text-primary px-3 py-2 text-helper text-on-navy shadow-e2"
          >
            {content}
            <RadixTooltip.Arrow className="fill-text-primary" />
          </RadixTooltip.Content>
        </RadixTooltip.Portal>
      </RadixTooltip.Root>
    </RadixTooltip.Provider>
  );
}

/* ---------------------------------------------------------------- Dropdown menu */

export type MenuItem =
  | {
      type?: 'item';
      label: ReactNode;
      icon?: DesignIconName;
      onSelect: () => void;
      destructive?: boolean;
      disabled?: boolean;
    }
  | { type: 'separator' };

/**
 * Context/overflow menu (design 458–464). Arrow keys, typeahead and RTL placement via Radix; the menu
 * is named by its trigger (so icon-only triggers must carry a label, e.g. <IconButton label=…>).
 */
export function DropdownMenu({ trigger, items }: { trigger: ReactNode; items: MenuItem[] }) {
  return (
    <RadixMenu.Root>
      <RadixMenu.Trigger asChild>{trigger}</RadixMenu.Trigger>
      <RadixMenu.Portal>
        <RadixMenu.Content
          align="end"
          sideOffset={6}
          className="z-50 min-w-[212px] rounded-button border border-border bg-surface p-1.5 shadow-e3"
        >
          {items.map((item, index) =>
            item.type === 'separator' ? (
              <RadixMenu.Separator key={`separator-${index}`} className="mx-2 my-1.5 h-px bg-border-subtle" />
            ) : (
              <RadixMenu.Item
                key={index}
                disabled={item.disabled}
                onSelect={item.onSelect}
                className={cn(
                  'flex min-h-11 cursor-pointer items-center gap-2.5 rounded-sm px-3 text-[0.875rem] outline-none select-none',
                  'data-[disabled]:cursor-not-allowed data-[disabled]:opacity-50 data-[highlighted]:bg-bg-page',
                  item.destructive ? 'text-danger-700' : 'text-text-primary',
                )}
              >
                {item.icon && (
                  <Icon
                    name={item.icon}
                    className={cn('size-[17px]', !item.destructive && 'text-text-strong')}
                  />
                )}
                {item.label}
              </RadixMenu.Item>
            ),
          )}
        </RadixMenu.Content>
      </RadixMenu.Portal>
    </RadixMenu.Root>
  );
}
