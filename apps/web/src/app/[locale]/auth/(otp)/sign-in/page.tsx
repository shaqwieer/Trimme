import { PhoneStep } from '@/components/auth/PhoneStep';
import { SignedInGate } from '@/components/auth/SignedInGate';
import { otpRequired, redirectIfSignedIn } from '@/lib/auth/server';
import { optionalReturnTo } from '@/lib/auth/paths';

export default async function CustomerSignInPage({
  params,
  searchParams,
}: PageProps<'/[locale]/auth/sign-in'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const returnTo = optionalReturnTo(query.returnTo);
  await redirectIfSignedIn(locale, returnTo);
  return (
    <>
      <SignedInGate returnTo={returnTo} />
      <PhoneStep mode="signIn" returnTo={returnTo} otpRequired={await otpRequired()} />
    </>
  );
}
