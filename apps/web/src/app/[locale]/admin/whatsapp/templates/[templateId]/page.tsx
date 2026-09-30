import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { TemplateEditor } from '@/components/admin/whatsapp/TemplateEditor';
import { Breadcrumb } from '@/components/ui/data';
import { ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { localeKey } from '@/lib/admin/whatsapp';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** One template slot in the editor (DV-A12, R-NTF-02); the actions follow the admin's WhatsApp permissions. */
export default async function WhatsAppTemplatePage({
  params,
}: PageProps<'/[locale]/admin/whatsapp/templates/[templateId]'>) {
  const { locale, templateId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminWhatsApp' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/whatsapp/templates/${templateId}`}
      title={t('title')}
      permission="Admin.WhatsApp.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/whatsapp/templates/{templateId}', {
          params: { path: { templateId } },
        });
        if (!data) return <ErrorState />;
        const title = `${t('templates.editorTitle', { event: t(`event.${data.event}`) })} · ${t(`audience.${data.audience}`)} · ${t(`locale.${localeKey(data.locale)}`)}`;
        return (
          <div className="flex flex-col gap-4">
            <Breadcrumb
              items={[
                { label: t('templates.breadcrumb'), href: '/admin/whatsapp/templates' },
                { label: title },
              ]}
            />
            <h2 className="text-h2 font-bold text-navy-900">{title}</h2>
            <TemplateEditor
              initial={data}
              canEdit={me.permissions.includes('Admin.WhatsApp.Templates.Edit')}
              canActivate={me.permissions.includes('Admin.WhatsApp.Templates.Activate')}
              canTestSend={me.permissions.includes('Admin.WhatsApp.TestSend')}
            />
          </div>
        );
      }}
    </AdminFrame>
  );
}
