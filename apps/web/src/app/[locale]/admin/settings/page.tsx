import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { PlatformSettingsForm } from '@/components/admin/PlatformSettingsForm';
import { Card } from '@/components/ui/cards';
import { Timeline } from '@/components/ui/data';
import { ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatDate, formatTime } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const RECENT = 5;

/** The audit summary of a settings change is "Changed: a, b" (D-076); the field names become their labels. */
function changedFields(summary: string | null | undefined): string[] {
  const match = /^Changed: (.+)$/.exec(summary ?? '');
  return match
    ? match[1]!
        .split(',')
        .map((f) => f.trim())
        .filter(Boolean)
    : [];
}

/**
 * Platform settings (DV-A17, R-AD-13, D-076): every section the platform reads — booking policy, reminders,
 * subscriptions and their enforcement, discovery, map defaults and the fixed region — with a save bar, the accepted
 * ranges and, for admins who can read the activity log, the latest changes with who made them.
 */
export default async function PlatformSettingsPage({ params }: PageProps<'/[locale]/admin/settings'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'platformSettings' });

  return (
    <AdminFrame locale={locale} path="/admin/settings" title={t('title')} permission="Admin.Settings.View">
      {async (me) => {
        const api = await getServerApi();
        const canAudit = me.permissions.includes('Admin.Audit.View');
        const [{ data: settings }, recent] = await Promise.all([
          api.GET('/api/v1/admin/settings'),
          canAudit
            ? api.GET('/api/v1/admin/audit', {
                params: { query: { action: 'platform_settings.updated', pageSize: RECENT } },
              })
            : Promise.resolve(undefined),
        ]);
        if (!settings) return <ErrorState />;
        return (
          <div className="grid max-w-[1200px] gap-5 xl:grid-cols-[1fr_320px]">
            <div className="flex min-w-0 flex-col gap-5">
              <p className="text-body text-text-secondary">{t('intro')}</p>
              <PlatformSettingsForm
                settings={settings}
                canEdit={me.permissions.includes('Admin.Settings.Edit')}
              />
            </div>
            {recent?.data && (
              <Card as="section" className="flex h-fit flex-col gap-3 p-5">
                <div className="flex items-baseline justify-between gap-2">
                  <h2 className="text-h3 font-bold text-navy-900">{t('recent.title')}</h2>
                  <Link
                    href="/admin/audit?action=platform_settings.updated"
                    className="text-helper font-bold text-brand-700 hover:underline"
                  >
                    {t('recent.all')}
                  </Link>
                </div>
                {recent.data.items.length === 0 ? (
                  <p className="text-caption text-text-secondary">{t('recent.empty')}</p>
                ) : (
                  <Timeline
                    items={recent.data.items.map((entry) => ({
                      id: entry.id,
                      tone: 'warning',
                      title:
                        changedFields(entry.summary)
                          .map((field) =>
                            t.has(`fields.${field}` as 'fields.currency')
                              ? t(`fields.${field}` as 'fields.currency')
                              : field,
                          )
                          .join(lang === 'ar' ? '، ' : ', ') || t('recent.changed'),
                      meta: (
                        <>
                          <bdi>{entry.actorName ?? '—'}</bdi> ·{' '}
                          {formatDate(entry.occurredAt, lang, { withWeekday: false })}{' '}
                          {formatTime(entry.occurredAt, lang)}
                        </>
                      ),
                    }))}
                  />
                )}
              </Card>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
