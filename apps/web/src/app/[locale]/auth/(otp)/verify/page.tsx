import { VerifyStep } from '@/components/auth/VerifyStep';
import { optionalReturnTo } from '@/lib/auth/paths';

export default async function VerifyOtpPage({ searchParams }: PageProps<'/[locale]/auth/verify'>) {
  const query = await searchParams;
  return <VerifyStep returnTo={optionalReturnTo(query.returnTo)} />;
}
