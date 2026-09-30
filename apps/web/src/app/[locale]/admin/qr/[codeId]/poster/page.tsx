import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { QrPoster } from '@/components/qr/QrPoster';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** The A5 poster of one code for an admin (c-qr «ملصق A5», D-114); printed from the browser. */
export default async function AdminQrPosterPage({ params }: PageProps<'/[locale]/admin/qr/[codeId]/poster'>) {
  const { locale, codeId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'qrPoster' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/qr/${codeId}/poster`}
      title={t('title')}
      permission="Admin.Qr.View"
    >
      {async () => {
        const api = await getServerApi();
        const { data: code } = await api.GET('/api/v1/admin/qr/codes/{codeId}', {
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
            scope="admin"
            codeId={code.id}
            name={professional ?? shopName}
            subtitle={professional ? shopName : undefined}
            url={code.url}
            backHref="/admin/qr"
          />
        );
      }}
    </AdminFrame>
  );
}
