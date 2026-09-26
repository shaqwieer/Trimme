import { CompleteProfileForm } from '@/components/auth/CompleteProfileForm';
import { redirect } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { homeFor, optionalReturnTo, withReturnTo } from '@/lib/auth/paths';
import { requireUser } from '@/lib/auth/server';

export default async function CompleteProfilePage({
  params,
  searchParams,
}: PageProps<'/[locale]/auth/complete-profile'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const returnTo = optionalReturnTo(query.returnTo);
  const me = await requireUser(locale, withReturnTo('/auth/complete-profile', returnTo));
  if (me.userType !== 'Customer') {
    redirect({ href: homeFor(me.userType), locale: asLocale(locale) });
  }

  return (
    <CompleteProfileForm
      returnTo={returnTo ?? homeFor(me.userType)}
      initialName={me.displayName ?? undefined}
    />
  );
}
