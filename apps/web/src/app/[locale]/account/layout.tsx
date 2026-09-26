import type { Metadata } from 'next';
import { SessionExpiryRedirect } from '@/components/auth/SessionClient';
import { QueryProvider } from '@/components/providers/QueryProvider';
import { CustomerShell } from '@/components/shell/CustomerShell';

/** Private customer area (spec §6: never indexed). Each page checks the session with requireUser. */
export const metadata: Metadata = { robots: { index: false, follow: false } };

export default function AccountLayout({ children }: LayoutProps<'/[locale]/account'>) {
  return (
    <QueryProvider>
      <SessionExpiryRedirect />
      <CustomerShell>{children}</CustomerShell>
    </QueryProvider>
  );
}
