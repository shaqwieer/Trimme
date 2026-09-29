import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { RoleEditor } from '@/components/admin/ops/RoleEditor';
import { roleLabel } from '@/components/admin/ops/roleNames';
import { Breadcrumb } from '@/components/ui/data';
import { ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { isSeedRole } from '@/lib/admin/admin';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** One admin role's permissions (DV-A15): editable roles for admins with `Admin.Roles.Manage`, read-only otherwise. */
export default async function AdminRolePage({ params }: PageProps<'/[locale]/admin/roles/[roleId]'>) {
  const { locale, roleId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminRoles' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/roles/${roleId}`}
      title={t('editor.title')}
      permission="Admin.Roles.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const [{ data: roles }, { data: catalogue }] = await Promise.all([
          api.GET('/api/v1/admin/roles'),
          api.GET('/api/v1/admin/permissions'),
        ]);
        const role = roles?.find((r) => r.id === roleId && r.userType === 'PlatformAdmin');
        if (!roles || !catalogue || !role)
          return <ErrorState title={roles && catalogue ? t('notFound') : undefined} />;
        const name = roleLabel(role.name, (key) => t(`roleNames.${key}`));

        return (
          <div className="flex flex-col gap-4">
            <Breadcrumb items={[{ label: t('title'), href: '/admin/roles' }, { label: name }]} />
            <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
            <RoleEditor
              role={{
                id: role.id,
                name: role.name,
                managed: role.managed,
                seed: isSeedRole(role.name),
                permissions: [...role.permissions],
              }}
              catalogue={catalogue.filter((p) => p.userType === 'PlatformAdmin').map((p) => p.code)}
              held={[...me.permissions]}
              canManage={me.permissions.includes('Admin.Roles.Manage')}
            />
          </div>
        );
      }}
    </AdminFrame>
  );
}
