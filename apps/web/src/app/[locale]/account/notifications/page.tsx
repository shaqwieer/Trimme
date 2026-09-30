import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { NotificationsPanel } from '@/components/notifications/NotificationsPanel';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { requireCustomer } from '@/lib/auth/server';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/notifications'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'notifications' });
  return { title: t('title') };
}

/**
 * The customer's notifications (c-profile, R-CUS-11, D-112): what the shop or the platform did with their bookings
 * (confirmed, moved, cancelled). WhatsApp messages go to the phone; this is the in-app copy.
 */
export default async function AccountNotificationsPage({
  params,
}: PageProps<'/[locale]/account/notifications'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const me = await requireCustomer(locale, '/account/notifications');
  if (!me) return <PermissionDenied homeHref="/" />;

  const api = await getServerApi();
  const { data, response } = await api.GET('/api/v1/me/notifications', {
    params: { query: { pageSize: 50 } },
  });
  if (!data) throw new Error(`GET /api/v1/me/notifications failed with status ${response.status}`);
  const t = await getTranslations({ locale, namespace: 'notifications' });

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5 px-4 py-6 md:px-6">
      <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
      <NotificationsPanel audience="customer" items={data.items} unread={data.unread} />
    </div>
  );
}
