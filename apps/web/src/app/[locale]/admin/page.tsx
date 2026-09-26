import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { SessionExpiryRedirect, SignOutButton } from '@/components/auth/SessionClient';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { PermissionDenied } from '@/components/ui/states';
import { homeFor } from '@/lib/auth/paths';
import { requireUser } from '@/lib/auth/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Admin home. The overview KPIs arrive in Phase 14; this phase wires the shell to the signed-in admin's real
 * permissions (R-WEB-13): navigation shows only what the roles grant, and the API enforces the same rules.
 */
export default async function AdminHomePage({ params }: PageProps<'/[locale]/admin'>) {
  const { locale } = await params;
  const me = await requireUser(locale, '/admin');
  const t = await getTranslations({ locale: locale === 'en' ? 'en' : 'ar', namespace: 'adminHome' });

  if (me.userType !== 'PlatformAdmin') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  return (
    <DashboardShell
      variant="admin"
      title={t('title')}
      permissions={me.permissions}
      sidebarFooter={
        <div className="flex flex-col gap-3 rounded-card bg-on-navy-subtle p-3">
          <p className="truncate text-label font-bold text-on-navy">{me.displayName}</p>
          <SignOutButton staff className="w-full" />
        </div>
      }
    >
      <SessionExpiryRedirect />
      {me.permissions.includes('Admin.Dashboard.View') ? (
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
      ) : (
        <PermissionDenied homeHref="/account/security" />
      )}
    </DashboardShell>
  );
}
