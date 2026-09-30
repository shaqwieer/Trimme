import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { BubblePreview } from '@/components/admin/whatsapp/BubblePreview';
import { RetryDispatchButton } from '@/components/admin/whatsapp/WhatsAppParts';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/cards';
import { Breadcrumb } from '@/components/ui/data';
import { Ltr } from '@/components/text/Ltr';
import { ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { dispatchTone } from '@/lib/admin/whatsapp';
import { getServerApi } from '@/lib/api/server';
import { formatDate, formatNumber, formatTime } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * One WhatsApp dispatch (DV-A13, R-NTF-07): the rendered text as sent (until the retention period ends), the template
 * version that rendered it, the content hash, attempts and the last error, and the retry for a failed one. The recipient
 * is shown masked only.
 */
export default async function WhatsAppDispatchPage({
  params,
}: PageProps<'/[locale]/admin/whatsapp/dispatches/[dispatchId]'>) {
  const { locale, dispatchId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminWhatsApp' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/whatsapp/dispatches/${dispatchId}`}
      title={t('title')}
      permission="Admin.WhatsApp.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/whatsapp/dispatches/{dispatchId}', {
          params: { path: { dispatchId } },
        });
        if (!data) return <ErrorState />;
        const d = data.dispatch;
        const at = (value?: string | null) =>
          value ? `${formatDate(value, lang, { withWeekday: false })} ${formatTime(value, lang)}` : '—';
        return (
          <div className="flex flex-col gap-4">
            <Breadcrumb
              items={[
                { label: t('dispatches.back'), href: '/admin/whatsapp/dispatches' },
                { label: t('dispatches.detailTitle') },
              ]}
            />
            <div className="grid gap-4 lg:grid-cols-2">
              <Card as="section" className="flex flex-col gap-3 p-5" aria-labelledby="dispatch-facts">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 id="dispatch-facts" className="text-h3 font-bold text-navy-900">
                    {t(`event.${d.event}`)} · {t(`audience.${d.audience}`)}
                  </h2>
                  <Badge tone={dispatchTone(d.status)} className="self-start">
                    {t(`dispatches.status.${d.status}`)}
                  </Badge>
                </div>
                <dl className="flex flex-col gap-2 text-caption">
                  <Row label={t('dispatches.columns.recipient')}>
                    <Ltr>{d.recipientMasked}</Ltr>
                  </Row>
                  <Row label={t('dispatches.columns.version')}>
                    {t('dispatches.template', {
                      event: t(`event.${d.event}`),
                      audience: t(`audience.${d.audience}`),
                      number: formatNumber(d.templateVersionNumber, lang),
                    })}
                  </Row>
                  <Row label={t('dispatches.columns.attempts')}>
                    {t('dispatches.attempts', { count: d.attempts })}
                  </Row>
                  <Row label={t('dispatches.columns.time')}>{at(d.createdAt)}</Row>
                  {d.lastError && (
                    <Row label={t('dispatches.error')}>
                      <span className="font-latin text-danger-700" dir="ltr">
                        {d.lastError}
                      </span>
                    </Row>
                  )}
                  {data.providerMessageId && (
                    <Row label={t('dispatches.providerId')}>
                      <span className="font-latin break-all" dir="ltr">
                        {data.providerMessageId}
                      </span>
                    </Row>
                  )}
                  <Row label={t('dispatches.hash')}>
                    <span className="font-latin text-helper break-all text-text-secondary" dir="ltr">
                      {data.contentHash}
                    </span>
                  </Row>
                </dl>
                {d.bookingId && (
                  <Link
                    href={`/admin/bookings/${d.bookingId}`}
                    className="text-label font-bold text-brand-700 underline-offset-4 hover:underline"
                  >
                    {t('dispatches.booking')}
                  </Link>
                )}
                {d.status === 'Failed' && me.permissions.includes('Admin.WhatsApp.Dispatches.Retry') && (
                  <RetryDispatchButton dispatchId={d.id} />
                )}
              </Card>
              <Card as="section" className="flex flex-col gap-3 p-5" aria-labelledby="dispatch-content">
                <h2 id="dispatch-content" className="text-h3 font-bold text-navy-900">
                  {t('dispatches.content')}
                </h2>
                {data.body ? (
                  <BubblePreview locale={d.locale} body={data.body} buttons={data.buttons} />
                ) : (
                  <p className="text-caption text-text-secondary">{t('dispatches.contentPurged')}</p>
                )}
              </Card>
            </div>
          </div>
        );
      }}
    </AdminFrame>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap gap-x-3 gap-y-0.5">
      <dt className="min-w-[120px] text-text-secondary">{label}</dt>
      <dd className="min-w-0 flex-1 text-text-primary">{children}</dd>
    </div>
  );
}
