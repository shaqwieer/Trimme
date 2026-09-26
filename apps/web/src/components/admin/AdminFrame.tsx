import type { ReactNode } from 'react';
import { SessionExpiryRedirect, SignOutButton } from '@/components/auth/SessionClient';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { PermissionDenied } from '@/components/ui/states';
import { homeFor } from '@/lib/auth/paths';
import { type Me, requireUser } from '@/lib/auth/server';

type AdminFrameProps = {
  locale: string;
  /** This page's locale-less path, used as `returnTo` when the session must be restored. */
  path: string;
  title: string;
  /** Permission the page needs; the API enforces the same rule on every call. */
  permission: string;
  children: (me: Me) => ReactNode;
};

/**
 * Server guard + admin shell for every admin page: signed-in platform admin, the page's permission, and navigation
 * filtered by the admin's real permissions (R-WEB-13).
 */
export async function AdminFrame({ locale, path, title, permission, children }: AdminFrameProps) {
  const me = await requireUser(locale, path);
  if (me.userType !== 'PlatformAdmin') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  return (
    <DashboardShell
      variant="admin"
      title={title}
      permissions={me.permissions}
      sidebarFooter={
        <div className="flex flex-col gap-3 rounded-card bg-on-navy-subtle p-3">
          <p className="truncate text-label font-bold text-on-navy">{me.displayName}</p>
          <SignOutButton staff className="w-full" />
        </div>
      }
    >
      <SessionExpiryRedirect />
      {me.permissions.includes(permission) ? children(me) : <PermissionDenied homeHref="/admin" />}
    </DashboardShell>
  );
}
