'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { EmptyState, InlineAlert } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import {
  type Notice,
  type NoticeAudience,
  noticeHref,
  noticeKey,
  noticeValues,
} from '@/lib/notifications/notices';
import { useNotificationSignal } from './useNotificationSignal';

type Props = {
  audience: Extract<NoticeAudience, 'shop' | 'customer'>;
  items: Notice[];
  unread: number;
};

/**
 * A notification centre (R-CUS-11, R-SD-08, D-112): newest first, unread ones marked, each linking to what it is about.
 * Marking read is optimistic and rolled back if the API refuses; the page re-renders when the server signals new ones.
 */
export function NotificationsPanel({ audience, items, unread }: Props) {
  const t = useTranslations('notifications');
  const tk = useTranslations('notifications.kinds') as unknown as (
    key: string,
    values?: Record<string, string | number>,
  ) => string;
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const [readIds, setReadIds] = useState<ReadonlySet<string>>(new Set());
  const [allRead, setAllRead] = useState(false);
  const [failed, setFailed] = useState(false);
  useNotificationSignal(() => router.refresh());

  const base = audience === 'shop' ? '/api/v1/shop/notifications' : '/api/v1/me/notifications';
  const isRead = (notice: Notice) => allRead || Boolean(notice.readAt) || readIds.has(notice.id);
  const remaining = allRead
    ? 0
    : Math.max(0, unread - items.filter((n) => !n.readAt && readIds.has(n.id)).length);

  const markRead = async (notice: Notice) => {
    if (isRead(notice)) return;
    setReadIds((current) => new Set(current).add(notice.id));
    const path = { params: { path: { notificationId: notice.id } } };
    const { response } =
      audience === 'shop'
        ? await browserApi.POST('/api/v1/shop/notifications/{notificationId}/read', path)
        : await browserApi.POST('/api/v1/me/notifications/{notificationId}/read', path);
    if (!response.ok) {
      setReadIds((current) => {
        const next = new Set(current);
        next.delete(notice.id);
        return next;
      });
      setFailed(true);
    }
  };

  const markAll = async () => {
    setAllRead(true);
    const { response } =
      audience === 'shop'
        ? await browserApi.POST('/api/v1/shop/notifications/read-all')
        : await browserApi.POST('/api/v1/me/notifications/read-all');
    if (!response.ok) {
      setAllRead(false);
      setFailed(true);
      return;
    }
    router.refresh();
  };

  if (items.length === 0) {
    return <EmptyState icon="bell" title={t('empty.title')} body={t('empty.body')} />;
  }

  return (
    <div className="flex flex-col gap-3" data-testid="notifications" data-base={base}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-helper text-text-secondary" aria-live="polite">
          {t('unreadCount', { count: remaining })}
        </p>
        <Button variant="secondary" size="sm" onClick={() => void markAll()} disabled={remaining === 0}>
          {t('markAllRead')}
        </Button>
      </div>
      {failed && <InlineAlert tone="danger" title={t('failed')} />}
      <ul className="flex flex-col gap-2">
        {items.map((notice) => {
          const read = isRead(notice);
          const href = noticeHref(audience, notice);
          const text = tk(noticeKey(audience, notice.kind), noticeValues(notice, locale));
          return (
            <li
              key={notice.id}
              data-testid="notification"
              data-kind={notice.kind}
              data-read={read ? 'true' : 'false'}
              className={cn(
                'flex items-start gap-3 rounded-card border p-3.5',
                read ? 'border-border-subtle bg-surface' : 'border-brand-200 bg-brand-50',
              )}
            >
              <span
                aria-hidden="true"
                className={cn(
                  'mt-0.5 inline-flex size-9 shrink-0 items-center justify-center rounded-full',
                  read ? 'bg-bg-muted text-text-secondary' : 'bg-brand-100 text-brand-700',
                )}
              >
                <Icon
                  name={notice.kind.startsWith('subscription.') ? 'card' : 'calendar'}
                  className="size-[18px]"
                />
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <p className={cn('text-body', read ? 'text-text-strong' : 'font-bold text-text-primary')}>
                  {!read && <span className="sr-only">{t('unread')} · </span>}
                  {text}
                </p>
                <p className="text-helper text-text-secondary">
                  {formatDate(notice.createdAt, locale, { withWeekday: false })} ·{' '}
                  {formatTime(notice.createdAt, locale)}
                </p>
                <div className="flex flex-wrap gap-3">
                  {href && (
                    <Link
                      href={href}
                      onClick={() => void markRead(notice)}
                      className="inline-flex min-h-11 items-center text-label font-bold text-brand-700 underline-offset-4 hover:underline"
                    >
                      {t('open')}
                    </Link>
                  )}
                  {!read && (
                    <button
                      type="button"
                      onClick={() => void markRead(notice)}
                      className="inline-flex min-h-11 items-center text-label font-bold text-text-strong underline-offset-4 hover:underline"
                    >
                      {t('markRead')}
                    </button>
                  )}
                </div>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
