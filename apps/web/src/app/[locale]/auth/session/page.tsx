import { SessionRestore } from '@/components/auth/SessionClient';
import { safeReturnTo } from '@/lib/auth/paths';

/** Where server guards send a request whose access cookie is missing or expired (see requireUser). */
export default async function SessionPage({ searchParams }: PageProps<'/[locale]/auth/session'>) {
  const query = await searchParams;
  const raw = Array.isArray(query.returnTo) ? query.returnTo[0] : query.returnTo;
  return <SessionRestore returnTo={safeReturnTo(raw, '/discover')} />;
}
