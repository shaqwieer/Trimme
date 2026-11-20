import { cookies } from 'next/headers';
import { PhoneStep } from '@/components/auth/PhoneStep';
import { SignedInGate } from '@/components/auth/SignedInGate';
import { otpRequired, redirectIfSignedIn } from '@/lib/auth/server';
import { optionalReturnTo } from '@/lib/auth/paths';
import { TERMS_COOKIE, termsRemembered } from '@/lib/auth/terms';

export default async function CustomerSignUpPage({
  params,
  searchParams,
}: PageProps<'/[locale]/auth/sign-up'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const returnTo = optionalReturnTo(query.returnTo);
  await redirectIfSignedIn(locale, returnTo);
  return (
    <>
      <SignedInGate returnTo={returnTo} />
      <PhoneStep
        mode="signUp"
        returnTo={returnTo}
        otpRequired={await otpRequired()}
        termsRemembered={termsRemembered((await cookies()).get(TERMS_COOKIE)?.value)}
      />
    </>
  );
}
