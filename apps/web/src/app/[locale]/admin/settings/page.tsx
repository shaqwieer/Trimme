import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { PlatformSettingsForm } from '@/components/admin/PlatformSettingsForm';
import { ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Platform settings (D-076): the minimal Phase 08 page; the full sectioned settings screen arrives in Phase 14. */
export default async function PlatformSettingsPage({ params }: PageProps<'/[locale]/admin/settings'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'platformSettings' });

  return (
    <AdminFrame locale={locale} path="/admin/settings" title={t('title')} permission="Admin.Settings.View">
      {async (me) => {
        const api = await getServerApi();
        const { data: settings } = await api.GET('/api/v1/admin/settings');
        if (!settings) return <ErrorState />;
        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            <PlatformSettingsForm
              settings={settings}
              canEdit={me.permissions.includes('Admin.Settings.Edit')}
            />
          </div>
        );
      }}
    </AdminFrame>
  );
}
