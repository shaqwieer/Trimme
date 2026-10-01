import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/cards';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { localeKey, MESSAGE_EVENTS } from '@/lib/admin/whatsapp';
import { getServerApi } from '@/lib/api/server';
import { formatNumber } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * WhatsApp templates (DV-A12, R-AD-10, D-109): every event with its customer and professional templates in Arabic and
 * English, the active version and whether a draft waits. Editing opens the two-pane editor.
 */
export default async function WhatsAppTemplatesPage({
  params,
}: PageProps<'/[locale]/admin/whatsapp/templates'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminWhatsApp' });

  return (
    <AdminFrame
      locale={locale}
      path="/admin/whatsapp/templates"
      title={t('title')}
      permission="Admin.WhatsApp.View"
    >
      {async () => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/whatsapp/templates');
        if (!data) return <ErrorState />;
        return (
          <div className="flex flex-col gap-4">
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/admin/whatsapp/templates', label: t('tabs.templates'), active: true },
                { href: '/admin/whatsapp/dispatches', label: t('tabs.dispatches'), active: false },
              ]}
            />
            <p className="text-caption text-text-secondary">{t('templates.intro')}</p>
            <div className="grid gap-4 xl:grid-cols-2" data-testid="template-list">
              {MESSAGE_EVENTS.map((event) => {
                const slots = data.items.filter((item) => item.event === event);
                if (slots.length === 0) return null;
                return (
                  <Card
                    key={event}
                    as="section"
                    className="flex flex-col gap-3 p-5"
                    aria-labelledby={`event-${event}`}
                  >
                    <h2 id={`event-${event}`} className="text-h3 font-bold text-navy-900">
                      {t(`event.${event}`)}
                    </h2>
                    <ul className="flex flex-col gap-2">
                      {slots.map((slot) => (
                        <li
                          key={slot.id}
                          className="flex flex-wrap items-center gap-3 rounded-button bg-bg-subtle p-3"
                          data-audience={slot.audience}
                          data-locale={slot.locale}
                        >
                          <div className="flex min-w-0 flex-1 flex-col gap-1">
                            <span className="text-label font-bold text-text-primary">
                              {t('templates.slot', {
                                audience: t(`audience.${slot.audience}`),
                                locale: t(`locale.${localeKey(slot.locale)}`),
                              })}
                            </span>
                            <span
                              className="line-clamp-1 text-helper text-text-secondary"
                              dir={slot.locale === 'ar' ? 'rtl' : 'ltr'}
                              lang={slot.locale}
                            >
                              {slot.activeBody}
                            </span>
                          </div>
                          {slot.activeVersionNumber ? (
                            <Badge size="sm" tone="success">
                              {t('templates.active', {
                                number: formatNumber(slot.activeVersionNumber, lang),
                              })}
                            </Badge>
                          ) : (
                            <Badge size="sm" tone="danger">
                              {t('templates.noActive')}
                            </Badge>
                          )}
                          {slot.hasDraft && (
                            <Badge size="sm" tone="warning">
                              {t('templates.hasDraft')}
                            </Badge>
                          )}
                          <Link
                            href={`/admin/whatsapp/templates/${slot.id}`}
                            className="inline-flex min-h-11 items-center text-label font-bold text-brand-700 underline-offset-4 hover:underline"
                            aria-label={`${t('templates.edit')}: ${t(`event.${event}`)} · ${t(`audience.${slot.audience}`)} · ${t(`locale.${localeKey(slot.locale)}`)}`}
                          >
                            {t('templates.edit')}
                          </Link>
                        </li>
                      ))}
                    </ul>
                  </Card>
                );
              })}
            </div>
          </div>
        );
      }}
    </AdminFrame>
  );
}
