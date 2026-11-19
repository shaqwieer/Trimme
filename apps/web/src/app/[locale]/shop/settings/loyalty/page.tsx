import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LoyaltyStatsGrid } from '@/components/loyalty/LoyaltyHistory';
import { LoyaltyRulesForm } from '@/components/loyalty/LoyaltyRulesForm';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Shop settings — loyalty (D-149, the client's 6-2): the owner's points rules (first visit, each later visit, the points
 * a reward needs, the reward, and what happens without visits) and how the programme is used. Loyalty comes with a
 * plan that includes it (D-155); the rules can be set before. Barbers read them; only `Shop.Profile.Edit` saves (the API
 * enforces the same).
 */
export default async function ShopLoyaltySettingsPage({
  params,
}: PageProps<'/[locale]/shop/settings/loyalty'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopSettings' });
  const tl = await getTranslations({ locale: lang, namespace: 'loyalty.rulesForm' });

  return (
    <ShopFrame locale={locale} path="/shop/settings/loyalty" title={t('title')}>
      {async (me) => {
        const api = await getServerApi();
        const [{ data: loyalty }, { data: services }] = await Promise.all([
          api.GET('/api/v1/shop/loyalty'),
          api.GET('/api/v1/shop/services'),
        ]);
        if (!loyalty) {
          return <EmptyState icon="shield" title={t('unavailableTitle')} body={t('unavailableBody')} />;
        }

        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/shop/settings', label: t('tabs.profile'), active: false },
                { href: '/shop/settings/location', label: t('tabs.location'), active: false },
                { href: '/shop/settings/booking', label: t('tabs.booking'), active: false },
                { href: '/shop/settings/loyalty', label: t('tabs.loyalty'), active: true },
              ]}
            />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <h2 className="text-h3 font-bold text-navy-900">{t('loyalty')}</h2>
              <LoyaltyRulesForm
                scope={{ kind: 'shop' }}
                rules={loyalty.rules}
                version={loyalty.version}
                included={loyalty.included}
                services={(services ?? [])
                  .filter((s) => !s.isArchived)
                  .map((s) => ({ id: s.id, nameAr: s.nameAr, nameEn: s.nameEn }))}
                canEdit={me.permissions.includes('Shop.Profile.Edit')}
              />
            </section>
            {loyalty.stats && (
              <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
                <h2 className="text-h3 font-bold text-navy-900">{tl('stats.title')}</h2>
                <LoyaltyStatsGrid stats={loyalty.stats} />
              </section>
            )}
          </div>
        );
      }}
    </ShopFrame>
  );
}
