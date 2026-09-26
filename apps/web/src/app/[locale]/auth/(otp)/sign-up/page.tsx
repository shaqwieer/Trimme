import { PhoneStep } from '@/components/auth/PhoneStep';
import { redirectIfSignedIn } from '@/lib/auth/server';
import { optionalReturnTo } from '@/lib/auth/paths';

export default async function CustomerSignUpPage({
  params,
  searchParams,
}: PageProps<'/[locale]/auth/sign-up'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const returnTo = optionalReturnTo(query.returnTo);
  await redirectIfSignedIn(locale, returnTo);
  return <PhoneStep mode="signUp" returnTo={returnTo} />;
}
