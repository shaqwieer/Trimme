import { ResetPasswordForm } from '@/components/auth/StaffForms';
import { firstParam } from '@/lib/auth/paths';

/** Opened from the emailed link (`?uid=…&token=…`). The token is only ever posted back to the API. */
export default async function ResetPasswordPage({
  searchParams,
}: PageProps<'/[locale]/auth/reset-password'>) {
  const query = await searchParams;
  return <ResetPasswordForm userId={firstParam(query.uid)} token={firstParam(query.token)} />;
}
