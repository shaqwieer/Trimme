import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { BookingListItem, isActive, RebookCard } from '@/components/booking/BookingCards';
import { ButtonLink } from '@/components/ui/Button';
import { Pagination } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam, homeFor } from '@/lib/auth/paths';
import { getMe, requireCustomer } from '@/lib/auth/server';

const PAGE_SIZE = 10;

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/bookings'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'bookings' });
  return { title: t('title') };
}

/**
 * My appointments (c-appointments 1668–1740): upcoming (soonest first) and past (latest first) tabs, each card with the
 * actions that apply to it. The API returns only the customer's own bookings (D-085).
 */
export default async function MyBookingsPage({
  params,
  searchParams,
}: PageProps<'/[locale]/account/bookings'>) {
  const [{ locale: raw }, query] = await Promise.all([params, searchParams]);
  const locale = asLocale(raw);
  const tab = firstParam(query.tab) === 'past' ? 'past' : 'upcoming';
  const page = Math.max(1, Number(firstParam(query.page)) || 1);
  const here = tab === 'past' ? '/account/bookings?tab=past' : '/account/bookings';
  const me = await requireCustomer(locale, here);
  if (!me) {
    return <PermissionDenied homeHref={homeFor((await getMe())?.userType ?? 'Customer')} />;
  }

  const t = await getTranslations({ locale, namespace: 'bookings' });
  const api = await getServerApi();
  const { data, response } = await api.GET('/api/v1/me/bookings', {
    params: { query: { tab: tab === 'past' ? 'Past' : 'Upcoming', page, pageSize: PAGE_SIZE } },
  });
  if (!data) throw new Error(`GET /api/v1/me/bookings failed with status ${response.status}`);
  const nextUp = tab === 'upcoming' ? data.items.find(isActive) : undefined;

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5 px-4 py-6 md:px-6">
      <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
      <LinkTabs
        label={t('tabsLabel')}
        tabs={[
          { href: '/account/bookings', label: t('tabs.upcoming'), active: tab === 'upcoming' },
          { href: '/account/bookings?tab=past', label: t('tabs.past'), active: tab === 'past' },
        ]}
      />
      {data.items.length === 0 ? (
        <EmptyState
          icon="calendar"
          title={tab === 'upcoming' ? t('empty.title') : t('empty.pastTitle')}
          body={tab === 'upcoming' ? t('empty.body') : t('empty.pastBody')}
          action={
            <ButtonLink href="/search" variant="primary" size="md">
              {t('empty.cta')}
            </ButtonLink>
          }
        />
      ) : (
        <>
          <ul className="flex flex-col gap-3" aria-label={t(`tabs.${tab}`)}>
            {data.items.map((booking) => (
              <BookingListItem key={booking.id} booking={booking} />
            ))}
          </ul>
          {nextUp && <RebookCard booking={nextUp} />}
          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            total={data.total}
            hrefForPage={(p) => `${here}${here.includes('?') ? '&' : '?'}page=${p}`}
          />
        </>
      )}
    </div>
  );
}
