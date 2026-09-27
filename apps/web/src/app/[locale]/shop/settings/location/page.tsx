import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { ShopLocationEditor } from '@/components/shops/ShopLocationEditor';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Shop settings — exact location. The pin picker when the admin policy opens the location to the shop and the user
 * holds Shop.Location.Edit; otherwise a read-only view that points to the admin (design analysis 03 §3.2).
 */
export default async function ShopLocationSettingsPage({
  params,
}: PageProps<'/[locale]/shop/settings/location'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopSettings' });

  return (
    <ShopFrame locale={locale} path="/shop/settings/location" title={t('title')}>
      {async (me) => {
        const api = await getServerApi();
        const { data: profile } = await api.GET('/api/v1/shop/profile');
        if (!profile) {
          return <EmptyState icon="shield" title={t('unavailableTitle')} body={t('unavailableBody')} />;
        }

        const canEdit =
          me.permissions.includes('Shop.Location.Edit') && profile.editableFields.includes('Location');
        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/shop/settings', label: t('tabs.profile'), active: false },
                { href: '/shop/settings/location', label: t('tabs.location'), active: true },
              ]}
            />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <h2 className="text-h3 font-bold text-navy-900">{t('location')}</h2>
              <ShopLocationEditor
                mode={{ kind: 'shop' }}
                location={profile.location}
                canEdit={canEdit}
                locale={lang}
              />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
