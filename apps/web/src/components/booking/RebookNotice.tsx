'use client';

import { useTranslations } from 'next-intl';
import { useState, useSyncExternalStore } from 'react';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import { rebookDismissedCookie, rebookDismissedFromCookies } from '@/lib/booking/rebookNotice';

/** The cookie fires no event: it is read on each render, and only the ✕ (local state) changes it. */
const noSubscription = () => () => {};

/**
 * «أعجبتك حلاقتك الأخيرة؟ أعد الحجز هنا» (D-160, the client's change to D-156's corner notice): a small notice in the
 * header's logo corner. Below 1024 px it sits over the logo, inside the header's 64 px (48 px tall, its ✕ 44 px
 * wide), and stops short of the theme switch and the bell. From 1024 px the header's menu sits next to the logo, so it
 * stays just under the header there.
 * The shop's name is not shown (the client's shorter text); it stays in the notice's accessible name. Pressing it opens
 * the booking at the time step with the same shop, barber and services. Its ✕ closes it and remembers the visit (see
 * `lib/booking/rebookNotice.ts`). It never takes focus or hides by itself, and it is not in the notifications.
 * The cookie is also read here, not only by the server: going back to the home page replays the router's cached
 * page, which still holds the notice, and it must stay closed there too. The server snapshot is «not closed», the
 * HTML the server sent, so hydration matches and the notice goes away right after.
 */
export function RebookNotice({ bookingId, shop, href }: { bookingId: string; shop: string; href: string }) {
  const t = useTranslations('discover.lastVisit');
  const [open, setOpen] = useState(true);
  const closedBefore = useSyncExternalStore(
    noSubscription,
    () => rebookDismissedFromCookies(document.cookie) === bookingId,
    () => false,
  );
  if (!open || closedBefore) return null;

  return (
    <div className="pointer-events-none fixed inset-x-0 top-2 z-30 lg:top-18">
      <div className="mx-auto max-w-[1280px] px-4 md:px-6 xl:px-10">
        <section
          aria-label={t('label', { shop })}
          data-testid="rebook-notice"
          className="pointer-events-auto flex h-12 w-max max-w-[min(15rem,calc(100vw-8.5rem))] items-stretch rounded-card border border-border bg-surface shadow-e3 motion-safe:animate-notice-in"
        >
          <Link
            href={href}
            className="flex min-w-0 flex-1 items-center gap-2 rounded-card ps-2 pe-0.5 hover:bg-bg-page"
          >
            <span
              aria-hidden="true"
              className="flex size-6 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700"
            >
              <Icon name="refresh" className="size-3.5" />
            </span>
            <span className="flex min-w-0 flex-col">
              <span className="truncate text-badge font-bold text-text-primary">{t('title')}</span>
              <span className="truncate text-badge font-bold text-text-link">{t('book')}</span>
            </span>
          </Link>
          <button
            type="button"
            aria-label={t('close')}
            onClick={() => {
              document.cookie = rebookDismissedCookie(bookingId, window.location.protocol === 'https:');
              setOpen(false);
            }}
            className="inline-flex w-11 shrink-0 items-center justify-center rounded-card text-text-secondary transition-colors hover:bg-bg-page hover:text-text-primary"
          >
            <Icon name="x" className="size-3.5" />
          </button>
        </section>
      </div>
    </div>
  );
}
