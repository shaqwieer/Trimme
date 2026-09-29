import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { roleLabel } from '@/components/admin/ops/roleNames';
import { InviteStaffForm, StaffActions } from '@/components/admin/ops/StaffActions';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/cards';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;

/** Platform staff with their roles (DV-A15, R-AD-11): assignment, access and invitations for `Admin.Staff.Manage`. */
export default async function AdminStaffPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/roles/staff'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminRoles' });
  const search = firstParam(query.q)?.trim() || undefined;
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame
      locale={locale}
      path="/admin/roles/staff"
      title={t('staff.title')}
      permission="Admin.Roles.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const [{ data: staff }, { data: roles }] = await Promise.all([
          api.GET('/api/v1/admin/staff', { params: { query: { search, page, pageSize: PAGE_SIZE } } }),
          api.GET('/api/v1/admin/roles'),
        ]);
        if (!staff || !roles) return <ErrorState />;
        const label = (name: string) => roleLabel(name, (key) => t(`roleNames.${key}`));
        const adminRoles = roles.filter((r) => r.userType === 'PlatformAdmin');
        const options = adminRoles.map((r) => ({ name: r.name, label: label(r.name) }));
        // Invitations offer only the roles this admin could assign (D-106): the API refuses the others.
        const invitable = adminRoles
          .filter((r) => r.name !== 'SuperAdmin' || me.roles.includes('SuperAdmin'))
          .filter((r) => r.permissions.every((code) => me.permissions.includes(code)))
          .map((r) => ({ name: r.name, label: label(r.name) }));
        const canManage = me.permissions.includes('Admin.Staff.Manage');

        return (
          <div className="flex flex-col gap-4">
            <LinkTabs
              label={t('sections')}
              tabs={[
                { href: '/admin/roles', label: t('tabs.roles'), active: false },
                { href: '/admin/roles/staff', label: t('tabs.staff'), active: true },
              ]}
            />
            {canManage && (
              <Card as="section" className="flex flex-col gap-3 p-5">
                <h2 className="text-h3 font-bold text-navy-900">{t('invite.title')}</h2>
                <InviteStaffForm roles={invitable} />
              </Card>
            )}
            <form method="get" role="search" className="flex min-w-0 gap-2 md:max-w-[460px]">
              <label htmlFor="staff-search" className="sr-only">
                {t('staff.search')}
              </label>
              <input
                id="staff-search"
                name="q"
                type="search"
                defaultValue={search}
                placeholder={t('staff.search')}
                className="min-h-11 min-w-0 flex-1 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input text-text-primary placeholder:text-text-placeholder"
              />
              <button
                type="submit"
                className="min-h-11 rounded-button border-[1.5px] border-border-strong bg-surface px-4 text-label font-bold text-text-strong"
              >
                {t('staff.searchSubmit')}
              </button>
            </form>
            <ResponsiveTable
              caption={t('staff.caption')}
              rows={[...staff.items]}
              rowKey={(s) => s.id}
              empty={<EmptyState icon="users" title={t('staff.empty')} />}
              actions={
                canManage
                  ? (s) => (
                      <StaffActions
                        userId={s.id}
                        name={s.displayName ?? s.email ?? ''}
                        current={s.roles.map((r) => r.name)}
                        roles={options}
                        disabled={s.isDisabled}
                        self={s.id === me.id}
                      />
                    )
                  : undefined
              }
              columns={[
                {
                  key: 'name',
                  header: t('staff.columns.name'),
                  mobile: 'primary',
                  width: '1.6fr',
                  cell: (s) => (
                    <span>
                      <span className="font-bold text-text-primary">{s.displayName ?? '—'}</span>
                      <span dir="ltr" className="block text-start font-latin text-helper text-text-secondary">
                        {s.email}
                      </span>
                    </span>
                  ),
                },
                {
                  key: 'roles',
                  header: t('staff.columns.roles'),
                  width: '1.6fr',
                  cell: (s) => (
                    <span className="flex flex-wrap gap-1.5">
                      {s.roles.map((r) => (
                        <Badge key={r.id} tone="brand" size="sm" dot={false}>
                          {label(r.name)}
                        </Badge>
                      ))}
                    </span>
                  ),
                },
                {
                  key: 'status',
                  header: t('staff.columns.status'),
                  cell: (s) => (
                    <Badge tone={s.isDisabled ? 'neutral' : 'success'} size="sm">
                      {s.isDisabled ? t('staff.disabled') : t('staff.active')}
                    </Badge>
                  ),
                },
                {
                  key: 'since',
                  header: t('staff.columns.since'),
                  cell: (s) => formatDate(s.createdAt, lang, { withWeekday: false, withYear: true }),
                },
              ]}
            />
            {staff.total > staff.pageSize && (
              <Pagination
                page={staff.page}
                pageSize={staff.pageSize}
                total={staff.total}
                hrefForPage={(target) =>
                  `/admin/roles/staff?page=${target}${search ? `&q=${encodeURIComponent(search)}` : ''}`
                }
              />
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
