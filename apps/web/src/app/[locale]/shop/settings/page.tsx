import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { ShopImagesEditor } from '@/components/shops/ShopImagesEditor';
import { ShopProfileEditor } from '@/components/shops/ShopProfileEditor';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Shop settings — profile (s-settings "ملف المحل"). Only the fields the admin policy opens are editable; locked ones
 * show a shield (R-SD-09, DV-S16). The API enforces the same policy.
 */
export default async function ShopSettingsPage({ params }: PageProps<'/[locale]/shop/settings'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopSettings' });

  return (
    <ShopFrame locale={locale} path="/shop/settings" title={t('title')}>
      {async (me) => {
        const api = await getServerApi();
        const { data: profile } = await api.GET('/api/v1/shop/profile');
        if (!profile) {
          return <EmptyState icon="shield" title={t('unavailableTitle')} body={t('unavailableBody')} />;
        }

        const canEdit = me.permissions.includes('Shop.Profile.Edit');
        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/shop/settings', label: t('tabs.profile'), active: true },
                { href: '/shop/settings/location', label: t('tabs.location'), active: false },
              ]}
            />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <h2 className="text-h3 font-bold text-navy-900">{t('profile')}</h2>
              <ShopProfileEditor mode={{ kind: 'shop' }} profile={profile} canEdit={canEdit} />
            </section>
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <h2 className="text-h3 font-bold text-navy-900">{t('images')}</h2>
              <ShopImagesEditor mode={{ kind: 'shop' }} profile={profile} canEdit={canEdit} />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
