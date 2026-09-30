import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { Badge } from '@/components/ui/Badge';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { Ltr } from '@/components/text/Ltr';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import {
  DISPATCH_STATUSES,
  type Dispatch,
  type DispatchStatus,
  dispatchTone,
  MESSAGE_EVENTS,
  type MessageEvent,
} from '@/lib/admin/whatsapp';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatNumber, formatTime } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 25;
const AUDIENCES = ['Customer', 'Professional'] as const;

/**
 * The WhatsApp dispatch log (DV-A13, R-AD-10, R-NTF-07, D-110): newest first, with the masked recipient, the template
 * version that rendered each message, status, attempts and error; status chips with counts and the last 24 hours'
 * delivery rate. A message opens its details (text, hash, retry).
 */
export default async function WhatsAppDispatchesPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/whatsapp/dispatches'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminWhatsApp' });
  const statusParam = firstParam(query.status);
  const status = (DISPATCH_STATUSES as readonly string[]).includes(statusParam ?? '')
    ? (statusParam as DispatchStatus)
    : undefined;
  const audienceParam = firstParam(query.audience);
  const audience = (AUDIENCES as readonly string[]).includes(audienceParam ?? '')
    ? (audienceParam as (typeof AUDIENCES)[number])
    : undefined;
  const eventParam = firstParam(query.event);
  const event = (MESSAGE_EVENTS as readonly string[]).includes(eventParam ?? '')
    ? (eventParam as MessageEvent)
    : undefined;
  const bookingId = firstParam(query.bookingId) || undefined;
  const page = Math.max(1, Number(firstParam(query.page)) || 1);
  const hrefWith = (changes: Record<string, string | number | undefined>) => {
    const next = new URLSearchParams();
    for (const [key, value] of Object.entries({ status, audience, event, bookingId, ...changes })) {
      if (value !== undefined && value !== '') next.set(key, String(value));
    }
    const text = next.toString();
    return `/admin/whatsapp/dispatches${text ? `?${text}` : ''}`;
  };

  return (
    <AdminFrame
      locale={locale}
      path="/admin/whatsapp/dispatches"
      title={t('title')}
      permission="Admin.WhatsApp.View"
    >
      {async () => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/whatsapp/dispatches', {
          params: { query: { status, audience, event, bookingId, page, pageSize: PAGE_SIZE } },
        });
        if (!data) return <ErrorState />;
        const countOf = (value: DispatchStatus | undefined) =>
          value === undefined
            ? data.counts.all
            : {
                Queued: data.counts.queued,
                Sent: data.counts.sent,
                Delivered: data.counts.delivered,
                Read: data.counts.read,
                Failed: data.counts.failed,
              }[value];

        return (
          <div className="flex flex-col gap-4">
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/admin/whatsapp/templates', label: t('tabs.templates'), active: false },
                { href: '/admin/whatsapp/dispatches', label: t('tabs.dispatches'), active: true },
              ]}
            />
            <p className="text-caption text-text-secondary" data-testid="dispatch-stats">
              {t('dispatches.stats', {
                count: data.stats.last24Hours,
                rate: `${formatNumber(data.stats.deliveryRate, lang, 1)}%`,
              })}
            </p>
            <nav aria-label={t('dispatches.filters')}>
              <ul className="flex flex-wrap gap-2">
                {[undefined, ...DISPATCH_STATUSES].map((value) => (
                  <li key={value ?? 'all'}>
                    <Link
                      href={hrefWith({ status: value, page: undefined })}
                      aria-current={value === status ? 'page' : undefined}
                      className={`inline-flex min-h-11 items-center rounded-pill border-[1.5px] px-3.5 text-label font-bold ${
                        value === status
                          ? 'border-navy-900 bg-navy-900 text-on-navy'
                          : 'border-border-strong bg-surface text-text-strong'
                      }`}
                    >
                      {t(`dispatches.status.${value ?? 'all'}`)} · {formatNumber(countOf(value), lang)}
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
            <form method="get" className="flex flex-wrap items-end gap-2">
              {status && <input type="hidden" name="status" value={status} />}
              {bookingId && <input type="hidden" name="bookingId" value={bookingId} />}
              <label className="flex flex-col gap-1 text-label font-bold text-text-primary">
                {t('dispatches.columns.audience')}
                <select
                  name="audience"
                  defaultValue={audience ?? ''}
                  className="min-h-11 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input text-text-primary"
                >
                  <option value="">{t('dispatches.anyAudience')}</option>
                  {AUDIENCES.map((value) => (
                    <option key={value} value={value}>
                      {t(`audience.${value}`)}
                    </option>
                  ))}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-label font-bold text-text-primary">
                {t('dispatches.columns.event')}
                <select
                  name="event"
                  defaultValue={event ?? ''}
                  className="min-h-11 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input text-text-primary"
                >
                  <option value="">{t('dispatches.anyEvent')}</option>
                  {MESSAGE_EVENTS.map((value) => (
                    <option key={value} value={value}>
                      {t(`event.${value}`)}
                    </option>
                  ))}
                </select>
              </label>
              <button
                type="submit"
                className="inline-flex min-h-11 items-center rounded-button bg-navy-900 px-4 text-label font-bold text-on-navy"
              >
                {t('dispatches.apply')}
              </button>
            </form>
            <ResponsiveTable<Dispatch>
              caption={t('dispatches.list')}
              rows={data.items}
              rowKey={(row) => row.id}
              empty={
                <EmptyState
                  icon="msg"
                  title={t('dispatches.empty.title')}
                  body={t('dispatches.empty.body')}
                />
              }
              columns={[
                {
                  key: 'event',
                  header: t('dispatches.columns.event'),
                  mobile: 'primary',
                  width: '2fr',
                  cell: (row) => (
                    <Link
                      href={`/admin/whatsapp/dispatches/${row.id}`}
                      className="font-bold text-brand-700 underline-offset-4 hover:underline"
                    >
                      {t(`event.${row.event}`)}
                      {row.kind !== 'Lifecycle' && (
                        <span className="text-text-secondary"> · {t(`dispatches.kind.${row.kind}`)}</span>
                      )}
                    </Link>
                  ),
                },
                {
                  key: 'audience',
                  header: t('dispatches.columns.audience'),
                  cell: (row) => t(`audience.${row.audience}`),
                },
                {
                  key: 'recipient',
                  header: t('dispatches.columns.recipient'),
                  cell: (row) => <Ltr>{row.recipientMasked}</Ltr>,
                },
                {
                  key: 'version',
                  header: t('dispatches.columns.version'),
                  cell: (row) =>
                    t('dispatches.versionShort', { number: formatNumber(row.templateVersionNumber, lang) }),
                },
                {
                  key: 'status',
                  header: t('dispatches.columns.status'),
                  cell: (row) => (
                    <Badge size="sm" tone={dispatchTone(row.status)}>
                      {t(`dispatches.status.${row.status}`)}
                    </Badge>
                  ),
                },
                {
                  key: 'time',
                  header: t('dispatches.columns.time'),
                  cell: (row) =>
                    `${formatDate(row.createdAt, lang, { withWeekday: false })} ${formatTime(row.createdAt, lang)}`,
                },
              ]}
            />
            <Pagination
              page={page}
              pageSize={PAGE_SIZE}
              total={data.total}
              hrefForPage={(p) => hrefWith({ page: p })}
            />
          </div>
        );
      }}
    </AdminFrame>
  );
}
