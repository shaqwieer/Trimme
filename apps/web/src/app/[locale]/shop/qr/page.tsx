import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { QrCodeCard, QrMaterials } from '@/components/qr/QrParts';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { KpiTile } from '@/components/ui/cards';
import { EmptyState, ErrorState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatDate, formatNumber } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The shop's own QR codes (open question 8, D-114): read-only for the owner — each code with its image, target, URL and
 * last 30 days' scans and bookings, the PNG, SVG and PDF files and the A5 poster. Codes are created by the TRIMME team.
 */
export default async function ShopQrPage({ params }: PageProps<'/[locale]/shop/qr'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopQr' });

  return (
    <ShopFrame locale={locale} path="/shop/qr" title={t('title')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Qr.View')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/shop/qr/codes');
        if (!data) return <ErrorState />;
        const day = (date: string) => formatDate(`${date}T12:00:00Z`, lang, { timeZone: 'UTC' });
        const period = t('period', { from: day(data.from), to: day(data.to) });
        return (
          <div className="flex flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            <section aria-label={period} className="flex flex-col gap-2">
              <p className="text-helper text-text-secondary">{period}</p>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-3" data-testid="shop-qr-kpis">
                <KpiTile label={t('kpi.visits')} value={formatNumber(data.totals.visits, lang)} icon="qr" />
                <KpiTile
                  label={t('kpi.bookings')}
                  value={formatNumber(data.totals.bookings, lang)}
                  icon="calendar"
                />
                <KpiTile
                  label={t('kpi.conversion')}
                  value={`${formatNumber(data.totals.conversionRate, lang, 1)}%`}
                  icon="chart"
                  delta={{
                    text: t('kpi.window', { days: formatNumber(data.attributionDays, lang) }),
                    tone: 'neutral',
                  }}
                />
              </div>
            </section>
            <section aria-label={t('list')} className="flex flex-col gap-3">
              {data.items.length === 0 ? (
                <EmptyState icon="qr" title={t('empty')} />
              ) : (
                <ul className="grid gap-3 xl:grid-cols-2">
                  {data.items.map((code) => (
                    <li key={code.id}>
                      <QrCodeCard scope="shop" code={code} posterHref={`/shop/qr/${code.id}/poster`} />
                    </li>
                  ))}
                </ul>
              )}
            </section>
            <QrMaterials namespace="shopQr" />
          </div>
        );
      }}
    </ShopFrame>
  );
}
