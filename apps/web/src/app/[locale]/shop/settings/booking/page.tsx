import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { BookingSettingsForm } from '@/components/shops/BookingSettingsForm';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Shop settings — booking (D-149, the owner's item 5): confirmation, start-time interval, minimum notice, furthest
 * bookable day and cleaning time, the shop's own instead of one set for every shop. Barbers read them; only
 * `Shop.Profile.Edit` saves (the API enforces the same).
 */
export default async function ShopBookingSettingsPage({
  params,
}: PageProps<'/[locale]/shop/settings/booking'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopSettings' });

  return (
    <ShopFrame locale={locale} path="/shop/settings/booking" title={t('title')}>
      {async (me) => {
        const api = await getServerApi();
        const { data: settings } = await api.GET('/api/v1/shop/booking-settings');
        if (!settings) {
          return <EmptyState icon="shield" title={t('unavailableTitle')} body={t('unavailableBody')} />;
        }

        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/shop/settings', label: t('tabs.profile'), active: false },
                { href: '/shop/settings/location', label: t('tabs.location'), active: false },
                { href: '/shop/settings/booking', label: t('tabs.booking'), active: true },
              ]}
            />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <h2 className="text-h3 font-bold text-navy-900">{t('booking')}</h2>
              <BookingSettingsForm
                scope={{ kind: 'shop' }}
                settings={settings}
                canEdit={me.permissions.includes('Shop.Profile.Edit')}
              />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
