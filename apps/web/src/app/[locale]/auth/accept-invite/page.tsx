import { AcceptInviteForm } from '@/components/auth/StaffForms';
import { firstParam } from '@/lib/auth/paths';

/** Opened from the emailed invitation (`?token=…`). */
export default async function AcceptInvitePage({ searchParams }: PageProps<'/[locale]/auth/accept-invite'>) {
  const query = await searchParams;
  return <AcceptInviteForm token={firstParam(query.token)} />;
}
