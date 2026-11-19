import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopAccount } from '@/components/shop/ShopAccount';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * «حسابي» for the shop's people (D-151, the client's 2-2): the name, the mobile, the calendar, the shop screens'
 * language, the notifications, signing out and, for a barber, deleting the account. Every shop user has it, even while
 * the shop is suspended; the account comes from the session.
 */
export default async function ShopAccountPage({ params }: PageProps<'/[locale]/shop/account'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopAccount' });

  return (
    <ShopFrame locale={locale} path="/shop/account" title={t('title')}>
      {async () => {
        const { data } = await (await getServerApi()).GET('/api/v1/shop/account');
        if (!data) return <ErrorState />;
        return <ShopAccount account={data} />;
      }}
    </ShopFrame>
  );
}
