import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { asLocale } from '@/i18n/routing';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Admin home. The overview KPIs arrive in Phase 14; the shell already shows only what the admin's roles grant.
 */
export default async function AdminHomePage({ params }: PageProps<'/[locale]/admin'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'adminHome' });

  return (
    <AdminFrame locale={locale} path="/admin" title={t('title')} permission="Admin.Dashboard.View">
      {(me) => (
        <section className="flex max-w-[720px] flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
          <h2 className="text-h3 font-bold text-navy-900">{t('welcome', { name: me.displayName ?? '' })}</h2>
          <p className="text-body text-text-secondary">{t('body')}</p>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-helper text-text-tertiary">{t('roles')}</span>
            {me.roles.map((role) => (
              <span
                key={role}
                className="rounded-badge bg-brand-100 px-2.5 py-1 font-latin text-badge font-bold text-brand-700"
              >
                {role}
              </span>
            ))}
          </div>
        </section>
      )}
    </AdminFrame>
  );
}
