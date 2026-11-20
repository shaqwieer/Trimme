'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import { rebookDismissedCookie } from '@/lib/booking/rebookNotice';

/**
 * «أعجبك حجزك في …؟ أعد الحجز من هنا» (D-156, the client's change to D-155's «احجز مثل آخر مرة» card): a small notice
 * pinned under the header in the logo's corner, so it takes no room from the page. Pressing it opens the booking at
 * the time step with the same shop, barber and services. Its ✕ closes it and remembers the visit (see
 * `lib/booking/rebookNotice.ts`). It never takes focus or hides by itself, and it is not in the notifications.
 */
export function RebookNotice({ bookingId, shop, href }: { bookingId: string; shop: string; href: string }) {
  const t = useTranslations('discover.lastVisit');
  const [open, setOpen] = useState(true);
  if (!open) return null;

  return (
    <div className="pointer-events-none fixed inset-x-0 top-18 z-30">
      <div className="mx-auto max-w-[1280px] px-4 md:px-6 xl:px-10">
        <aside
          aria-label={t('label')}
          data-testid="rebook-notice"
          className="pointer-events-auto flex w-full max-w-[17.5rem] items-start rounded-card border border-border bg-surface shadow-e3 motion-safe:animate-notice-in"
        >
          <Link
            href={href}
            className="flex min-w-0 flex-1 items-center gap-2.5 rounded-card py-2.5 ps-3 pe-1 hover:bg-bg-page"
          >
            <span
              aria-hidden="true"
              className="flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700"
            >
              <Icon name="refresh" className="size-4" />
            </span>
            <span className="flex min-w-0 flex-col">
              <span className="text-helper font-bold text-text-primary">{t('title', { shop })}</span>
              <span className="text-helper font-bold text-text-link">{t('book')}</span>
            </span>
          </Link>
          <button
            type="button"
            aria-label={t('close')}
            onClick={() => {
              document.cookie = rebookDismissedCookie(bookingId, window.location.protocol === 'https:');
              setOpen(false);
            }}
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-card text-text-secondary transition-colors hover:bg-bg-page hover:text-text-primary"
          >
            <Icon name="x" className="size-4" />
          </button>
        </aside>
      </div>
    </div>
  );
}
