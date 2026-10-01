import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { Badge } from '@/components/ui/Badge';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatNumber } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;

/**
 * The customers directory (DV-A08, R-AD-06): newest first, searchable by name only, with booking figures. No contact
 * data appears in the list; a customer's profile shows the number masked with an audited reveal.
 */
export default async function AdminCustomersPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/customers'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminCustomers' });
  const search = firstParam(query.q)?.trim() || undefined;
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame locale={locale} path="/admin/customers" title={t('title')} permission="Admin.Customers.View">
      {async () => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/customers', {
          params: { query: { search, page, pageSize: PAGE_SIZE } },
        });
        if (!data) return <ErrorState />;
        const date = (value: string | null | undefined) =>
          value ? formatDate(value, lang, { withWeekday: false, withYear: true }) : '—';

        return (
          <div className="flex flex-col gap-4">
            <form method="get" role="search" className="flex min-w-0 gap-2 md:max-w-[460px]">
              <label htmlFor="customer-search" className="sr-only">
                {t('search')}
              </label>
              <input
                id="customer-search"
                name="q"
                type="search"
                defaultValue={search}
                placeholder={t('searchPlaceholder')}
                className="min-h-11 min-w-0 flex-1 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input text-text-primary placeholder:text-text-placeholder"
              />
              <button
                type="submit"
                className="min-h-11 rounded-button border-[1.5px] border-border-strong bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              >
                {t('searchSubmit')}
              </button>
            </form>
            <p className="text-helper text-text-secondary">{t('privacy')}</p>
            <ResponsiveTable
              caption={t('caption')}
              rows={[...data.items]}
              rowKey={(c) => c.id}
              empty={<EmptyState icon="users" title={t('empty.title')} body={t('empty.body')} />}
              columns={[
                {
                  key: 'name',
                  header: t('columns.name'),
                  mobile: 'primary',
                  width: '2fr',
                  cell: (c) => (
                    <span className="inline-flex flex-wrap items-center gap-2">
                      <Link
                        href={`/admin/customers/${c.id}`}
                        className="font-bold text-brand-700 hover:underline"
                      >
                        {c.displayName ?? t('noName')}
                      </Link>
                      {c.isDisabled && (
                        <Badge tone="neutral" size="sm">
                          {t('disabled')}
                        </Badge>
                      )}
                    </span>
                  ),
                },
                {
                  key: 'bookings',
                  header: t('columns.bookings'),
                  cell: (c) => <span className="font-latin">{formatNumber(c.bookings, lang)}</span>,
                },
                {
                  key: 'upcoming',
                  header: t('columns.upcoming'),
                  cell: (c) => <span className="font-latin">{formatNumber(c.upcoming, lang)}</span>,
                },
                { key: 'last', header: t('columns.last'), cell: (c) => date(c.lastBookingAt) },
                { key: 'registered', header: t('columns.registered'), cell: (c) => date(c.registeredAt) },
              ]}
            />
            {data.total > data.pageSize && (
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                total={data.total}
                hrefForPage={(target) =>
                  `/admin/customers?page=${target}${search ? `&q=${encodeURIComponent(search)}` : ''}`
                }
              />
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
