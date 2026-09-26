import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { CreateShopForm } from '@/components/admin/ShopForms';
import { asLocale } from '@/i18n/routing';

export const metadata: Metadata = { robots: { index: false, follow: false } };

export default async function AdminNewShopPage({ params }: PageProps<'/[locale]/admin/shops/new'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'adminShops.create' });

  return (
    <AdminFrame locale={locale} path="/admin/shops/new" title={t('title')} permission="Admin.Shops.Create">
      {() => <CreateShopForm />}
    </AdminFrame>
  );
}
