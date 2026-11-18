import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { CreateBarberForm } from '@/components/admin/ProfessionalForms';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The owner adds a barber to their own shop (D-146): names, specialty, the mobile (their WhatsApp and their sign-in).
 * The login is made with the barber; its temporary password is shown once, then the barber's page opens.
 */
export default async function NewShopBarberPage({ params }: PageProps<'/[locale]/shop/barbers/new'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBarbers' });

  return (
    <ShopFrame locale={locale} path="/shop/barbers/new" title={t('create.title')}>
      {(me, shop) => {
        if (shop.status === 'Suspended') return <EmptyState icon="shield" title={t('create.title')} />;
        if (!me.permissions.includes('Shop.Barbers.Manage')) return <PermissionDenied homeHref="/shop" />;
        return (
          <div className="flex flex-col gap-5">
            <Breadcrumb
              items={[{ label: t('title'), href: '/shop/barbers' }, { label: t('create.title') }]}
            />
            <CreateBarberForm />
          </div>
        );
      }}
    </ShopFrame>
  );
}
