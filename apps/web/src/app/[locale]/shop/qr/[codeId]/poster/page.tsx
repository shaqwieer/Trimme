import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { QrPoster } from '@/components/qr/QrPoster';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** The A5 poster of one of the shop's own codes (D-114); another shop's code is 404 from the API. */
export default async function ShopQrPosterPage({ params }: PageProps<'/[locale]/shop/qr/[codeId]/poster'>) {
  const { locale, codeId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'qrPoster' });

  return (
    <ShopFrame locale={locale} path={`/shop/qr/${codeId}/poster`} title={t('title')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Qr.View')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const { data: code } = await api.GET('/api/v1/shop/qr/codes/{codeId}', {
          params: { path: { codeId } },
        });
        if (!code) notFound();
        const shopName = localizedName(lang, code.shopNameAr, code.shopNameEn);
        const professional =
          code.targetType === 'Professional'
            ? localizedName(lang, code.professionalNameAr ?? '', code.professionalNameEn)
            : null;
        return (
          <QrPoster
            scope="shop"
            codeId={code.id}
            name={professional ?? shopName}
            subtitle={professional ? shopName : undefined}
            url={code.url}
            backHref="/shop/qr"
          />
        );
      }}
    </ShopFrame>
  );
}
