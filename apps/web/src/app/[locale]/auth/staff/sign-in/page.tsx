import { StaffSignInForm } from '@/components/auth/StaffForms';
import { optionalReturnTo } from '@/lib/auth/paths';
import { redirectIfSignedIn } from '@/lib/auth/server';

export default async function StaffSignInPage({
  params,
  searchParams,
}: PageProps<'/[locale]/auth/staff/sign-in'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const returnTo = optionalReturnTo(query.returnTo);
  await redirectIfSignedIn(locale, returnTo);
  return <StaffSignInForm returnTo={returnTo} />;
}
