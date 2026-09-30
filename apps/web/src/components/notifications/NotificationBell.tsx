'use client';

import { useLocale, useTranslations } from 'next-intl';
import { DropdownMenu as RadixMenu } from 'radix-ui';
import { useCallback, useEffect, useState } from 'react';
import { Icon } from '@/components/ui/icons';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { cn } from '@/lib/cn';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';
import { type Notice, noticeHref, noticeKey, noticeValues } from '@/lib/notifications/notices';
import { useNotificationSignal } from './useNotificationSignal';

const bellClasses =
  'relative inline-flex size-11 shrink-0 items-center justify-center rounded-button text-text-strong transition-colors hover:bg-brand-100';

/** The unread count as a small badge (9+ above nine), hidden at zero. */
function UnreadBadge({ count }: { count: number }) {
  const locale = useLocale() as AppLocale;
  if (count <= 0) return null;
  return (
    <span
      aria-hidden="true"
      data-testid="unread-badge"
      className="absolute -end-0.5 -top-0.5 inline-flex min-w-5 items-center justify-center rounded-pill bg-danger-700 px-1 font-latin text-[0.6875rem] leading-5 font-bold text-on-navy"
    >
      {count > 9 ? `${formatNumber(9, locale)}+` : formatNumber(count, locale)}
    </span>
  );
}

/**
 * The unread count through a plain `fetch`: a 401 (the session just ended, for example right after signing out) must not
 * start the client's refresh-or-redirect protocol from a header widget; the count simply stays as it was.
 */
async function fetchUnread(scope: 'shop' | 'me'): Promise<number | undefined> {
  try {
    const response = await fetch(`/api/v1/${scope === 'shop' ? 'shop' : 'me'}/notifications/unread-count`, {
      credentials: 'include',
      cache: 'no-store',
    });
    if (!response.ok) return undefined;
    return ((await response.json()) as { unread: number }).unread;
  } catch {
    return undefined;
  }
}

function useUnreadCount(scope: 'shop' | 'me') {
  const [count, setCount] = useState(0);
  const reload = useCallback(() => {
    void fetchUnread(scope).then((unread) => {
      if (unread !== undefined) setCount(unread);
    });
  }, [scope]);

  useEffect(() => {
    let live = true;
    const refresh = () =>
      void fetchUnread(scope).then((unread) => {
        if (live && unread !== undefined) setCount(unread);
      });
    refresh();
    window.addEventListener('focus', refresh);
    return () => {
      live = false;
      window.removeEventListener('focus', refresh);
    };
  }, [scope]);
  useNotificationSignal(reload);
  return { count, reload };
}

/**
 * The header bell for a shop or a customer (spec §12, §13; D-112): a link to the notifications page with the unread
 * count, updated live when the server signals new notifications and when the window regains focus.
 */
export function NotificationBell({ scope, href }: { scope: 'shop' | 'me'; href: string }) {
  const t = useTranslations('notifications');
  const { count } = useUnreadCount(scope);
  return (
    <Link
      href={href}
      className={bellClasses}
      aria-label={count > 0 ? t('bellUnread', { count }) : t('bell')}
      data-testid="notification-bell"
    >
      <Icon name="bell" className="size-[19px]" />
      <UnreadBadge count={count} />
    </Link>
  );
}

/**
 * The admin bell (DV-A05, D-112): a menu with the latest operational alerts (failed messages, expiring subscriptions).
 * Choosing one marks it read and opens what it is about; "mark all as read" clears the count.
 */
export function AdminNotificationBell() {
  const t = useTranslations('notifications');
  const tk = useTranslations('notifications.kinds') as unknown as (
    key: string,
    values?: Record<string, string | number>,
  ) => string;
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const { count, reload } = useUnreadCount('me');
  const [items, setItems] = useState<Notice[]>();

  const loadItems = useCallback(async () => {
    const { data } = await browserApi.GET('/api/v1/me/notifications', { params: { query: { pageSize: 6 } } });
    setItems(data?.items ?? []);
  }, []);

  const open = async (notice: Notice) => {
    if (!notice.readAt) {
      await browserApi.POST('/api/v1/me/notifications/{notificationId}/read', {
        params: { path: { notificationId: notice.id } },
      });
      reload();
    }
    const href = noticeHref('admin', notice);
    if (href) router.push(href);
  };

  const markAll = async () => {
    await browserApi.POST('/api/v1/me/notifications/read-all');
    setItems((current) => current?.map((n) => ({ ...n, readAt: n.readAt ?? new Date().toISOString() })));
    reload();
  };

  return (
    <RadixMenu.Root onOpenChange={(opened) => opened && void loadItems()}>
      <RadixMenu.Trigger
        className={bellClasses}
        aria-label={count > 0 ? t('bellUnread', { count }) : t('bell')}
        data-testid="notification-bell"
      >
        <Icon name="bell" className="size-[19px]" />
        <UnreadBadge count={count} />
      </RadixMenu.Trigger>
      <RadixMenu.Portal>
        <RadixMenu.Content
          align="end"
          sideOffset={6}
          className="z-50 flex w-[min(92vw,360px)] flex-col rounded-button border border-border bg-surface p-1.5 shadow-e3"
        >
          <RadixMenu.Label className="px-3 py-2 text-label font-bold text-text-primary">
            {t('title')}
          </RadixMenu.Label>
          {items === undefined ? (
            <p className="px-3 py-3 text-helper text-text-secondary">{t('loading')}</p>
          ) : items.length === 0 ? (
            <p className="px-3 py-3 text-helper text-text-secondary">{t('adminEmpty')}</p>
          ) : (
            items.map((notice) => (
              <RadixMenu.Item
                key={notice.id}
                onSelect={() => void open(notice)}
                className="flex min-h-11 cursor-pointer items-start gap-2.5 rounded-sm px-3 py-2 text-[0.875rem] outline-none select-none data-[highlighted]:bg-bg-page"
              >
                <span
                  aria-hidden="true"
                  className={cn(
                    'mt-2 size-2 shrink-0 rounded-full',
                    notice.readAt ? 'bg-transparent' : 'bg-brand-600',
                  )}
                />
                <span
                  className={cn(
                    'min-w-0 flex-1',
                    notice.readAt ? 'text-text-secondary' : 'font-bold text-text-primary',
                  )}
                >
                  {tk(noticeKey('admin', notice.kind), noticeValues(notice, locale))}
                </span>
              </RadixMenu.Item>
            ))
          )}
          <RadixMenu.Separator className="mx-2 my-1.5 h-px bg-border-subtle" />
          <RadixMenu.Item
            disabled={count === 0}
            onSelect={() => void markAll()}
            className="flex min-h-11 cursor-pointer items-center rounded-sm px-3 text-label font-bold text-brand-700 outline-none select-none data-[disabled]:cursor-not-allowed data-[disabled]:opacity-50 data-[highlighted]:bg-bg-page"
          >
            {t('markAllRead')}
          </RadixMenu.Item>
        </RadixMenu.Content>
      </RadixMenu.Portal>
    </RadixMenu.Root>
  );
}
