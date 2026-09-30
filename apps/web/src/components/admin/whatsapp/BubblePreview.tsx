import { Icon } from '@/components/ui/icons';

/**
 * A WhatsApp-style message bubble in TRIMME's neutral styling (DV-T06: no WhatsApp brand colour): the rendered text with
 * its line breaks, and the buttons as full-width rows under it. The text direction follows the template's locale.
 */
export function BubblePreview({
  locale,
  body,
  buttons,
}: {
  locale: string;
  body: string;
  buttons: ReadonlyArray<{ label: string; url: string }>;
}) {
  return (
    <div className="rounded-card bg-bg-subtle p-4" data-testid="bubble-preview">
      <div
        dir={locale === 'ar' ? 'rtl' : 'ltr'}
        lang={locale}
        className="ms-0 me-auto flex max-w-[340px] flex-col overflow-hidden rounded-card rounded-ss-sm border border-border-subtle bg-surface shadow-e1"
      >
        <p className="px-3.5 py-3 text-caption leading-relaxed whitespace-pre-line text-text-primary">
          {body || '…'}
        </p>
        {buttons.map((button) => (
          <span
            key={button.label}
            className="flex min-h-11 items-center justify-center gap-1.5 border-t border-border-subtle px-3 text-label font-bold text-brand-700"
            title={button.url}
          >
            <Icon name="chevL" className="size-4 ltr:rotate-180 rtl:rotate-0" />
            {button.label}
          </span>
        ))}
      </div>
    </div>
  );
}
