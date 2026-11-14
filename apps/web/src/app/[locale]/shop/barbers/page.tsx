import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { BarberLoginControls } from '@/components/shop/BarberLoginControls';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, ErrorState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * «الحلاقون» (D-145): the owner turns each barber's login on or off. A barber signs in with the WhatsApp number on their
 * profile and a temporary password chosen here, replaced at their first sign-in; then they see and run their own
 * appointments, day and walk-ins. The barbers themselves (names, numbers, services) are managed by the platform.
 */
export default async function ShopBarbersPage({ params }: PageProps<'/[locale]/shop/barbers'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'barberLogins' });

  return (
    <ShopFrame locale={locale} path="/shop/barbers" title={t('title')}>
      {async (me, shop) => {
        if (shop.status === 'Suspended') return <EmptyState icon="shield" title={t('title')} />;
        if (!me.permissions.includes('Shop.Barbers.ManageLogin'))
          return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const { data: professionals } = await api.GET('/api/v1/shop/professionals');
        if (!professionals) return <ErrorState />;
        const active = professionals.filter((p) => p.status === 'Active');
        // A handful of barbers per shop: one read each, side by side.
        const logins = await Promise.all(
          active.map((p) =>
            api
              .GET('/api/v1/shop/professionals/{professionalId}/login', {
                params: { path: { professionalId: p.id } },
              })
              .then((r) => r.data),
          ),
        );

        return (
          <div className="flex max-w-[860px] flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            {active.length === 0 ? (
              <EmptyState icon="users" title={t('empty')} />
            ) : (
              <ul className="flex flex-col gap-3" data-testid="barber-logins">
                {active.map((p, index) => {
                  const login = logins[index];
                  const name = localizedName(locale, p.nameAr, p.nameEn);
                  return (
                    <li
                      key={p.id}
                      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
                    >
                      <h2 className="text-h3 font-bold text-navy-900">{name}</h2>
                      {login ? (
                        <BarberLoginControls scope="shop" professionalId={p.id} name={name} login={login} />
                      ) : (
                        <ErrorState />
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        );
      }}
    </ShopFrame>
  );
}
