import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { CreateRoleForm } from '@/components/admin/ops/RoleEditor';
import { roleLabel } from '@/components/admin/ops/roleNames';
import { Card } from '@/components/ui/cards';
import { Icon } from '@/components/ui/icons';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { permissionGroups, permissionKey } from '@/lib/admin/admin';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The permission matrix (a-roles 3074–3087, DV-A15): every admin permission against every platform-admin role, read
 * from the API's catalogue and grants. Each role opens its editor; managed roles are shown but locked.
 */
export default async function AdminRolesPage({ params }: PageProps<'/[locale]/admin/roles'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminRoles' });
  const tCodes = await getTranslations({ locale: lang, namespace: 'permissions.codes' });
  const tAreas = await getTranslations({ locale: lang, namespace: 'permissions.areas' });

  return (
    <AdminFrame locale={locale} path="/admin/roles" title={t('title')} permission="Admin.Roles.View">
      {async (me) => {
        const api = await getServerApi();
        const [{ data: roles }, { data: catalogue }] = await Promise.all([
          api.GET('/api/v1/admin/roles'),
          api.GET('/api/v1/admin/permissions'),
        ]);
        if (!roles || !catalogue) return <ErrorState />;
        const adminRoles = roles.filter((r) => r.userType === 'PlatformAdmin');
        const codes = catalogue.filter((p) => p.userType === 'PlatformAdmin').map((p) => p.code);
        const label = (name: string) => roleLabel(name, (key) => t(`roleNames.${key}`));

        return (
          <div className="flex flex-col gap-4">
            <LinkTabs
              label={t('sections')}
              tabs={[
                { href: '/admin/roles', label: t('tabs.roles'), active: true },
                { href: '/admin/roles/staff', label: t('tabs.staff'), active: false },
              ]}
            />
            <p className="text-body text-text-secondary">{t('intro')}</p>
            {me.permissions.includes('Admin.Roles.Manage') && (
              <Card as="section" className="flex flex-col gap-3 p-5">
                <h2 className="text-h3 font-bold text-navy-900">{t('create.title')}</h2>
                <CreateRoleForm />
              </Card>
            )}
            <Card as="section" className="flex min-w-0 flex-col gap-3 p-5">
              <h2 className="text-h3 font-bold text-navy-900">{t('matrix')}</h2>
              <div
                className="relative w-0 min-w-full overflow-x-auto"
                tabIndex={0}
                role="region"
                aria-label={t('matrix')}
              >
                <table
                  className="w-full min-w-[640px] border-collapse text-start text-caption"
                  data-testid="permission-matrix"
                >
                  <caption className="sr-only">{t('matrix')}</caption>
                  <thead>
                    <tr className="border-b border-border">
                      <th
                        scope="col"
                        className="px-3 py-2 text-start text-badge font-bold text-text-secondary"
                      >
                        {t('permission')}
                      </th>
                      {adminRoles.map((role) => (
                        <th
                          key={role.id}
                          scope="col"
                          className="px-3 py-2 text-center text-badge font-bold text-text-secondary"
                        >
                          <Link
                            href={`/admin/roles/${role.id}`}
                            className="inline-flex min-h-11 items-center gap-1 text-brand-700 hover:underline"
                          >
                            {label(role.name)}
                            {role.managed && (
                              <Icon name="shield" className="size-3.5" label={t('managedRole')} />
                            )}
                          </Link>
                        </th>
                      ))}
                    </tr>
                  </thead>
                  {permissionGroups(codes).map((group) => (
                    <tbody key={group.area}>
                      <tr className="bg-bg-page">
                        <th
                          scope="colgroup"
                          colSpan={adminRoles.length + 1}
                          className="px-3 py-1.5 text-start text-badge font-bold text-navy-900"
                        >
                          {tAreas(group.area as 'Shops')}
                        </th>
                      </tr>
                      {group.codes.map((code) => (
                        <tr key={code} className="border-b border-border-row">
                          <th scope="row" className="px-3 py-2 text-start font-normal text-text-strong">
                            {tCodes(permissionKey(code) as 'Admin_Shops_View')}
                          </th>
                          {adminRoles.map((role) => {
                            const granted = role.permissions.includes(code);
                            return (
                              <td key={role.id} className="px-3 py-2 text-center">
                                {granted ? (
                                  <Icon
                                    name="check"
                                    className="inline size-4 text-success-700"
                                    label={t('granted')}
                                  />
                                ) : (
                                  <span className="text-text-tertiary">
                                    <span aria-hidden="true">—</span>
                                    <span className="sr-only">{t('notGranted')}</span>
                                  </span>
                                )}
                              </td>
                            );
                          })}
                        </tr>
                      ))}
                    </tbody>
                  ))}
                </table>
              </div>
            </Card>
          </div>
        );
      }}
    </AdminFrame>
  );
}
