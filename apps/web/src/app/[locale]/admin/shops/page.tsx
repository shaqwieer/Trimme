import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { ShopStatusBadge } from '@/components/admin/ShopStatusBadge';
import { ButtonLink } from '@/components/ui/Button';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;

/** Admin shops list (design a-shops): search + paged table that becomes cards on phones. Server-rendered. */
export default async function AdminShopsPage({ params, searchParams }: PageProps<'/[locale]/admin/shops'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminShops' });
  const search = firstParam(query.q)?.trim() || undefined;
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame locale={locale} path="/admin/shops" title={t('title')} permission="Admin.Shops.View">
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/shops', {
          params: { query: { page, pageSize: PAGE_SIZE, search } },
        });
        const hrefFor = (target: number) =>
          `/admin/shops?page=${target}${search ? `&q=${encodeURIComponent(search)}` : ''}`;

        return (
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <form method="get" role="search" className="flex min-w-0 flex-1 gap-2 md:max-w-[420px]">
                <label htmlFor="shop-search" className="sr-only">
                  {t('search')}
                </label>
                <input
                  id="shop-search"
                  name="q"
                  type="search"
                  defaultValue={search}
                  placeholder={t('search')}
                  className="min-h-11 min-w-0 flex-1 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input text-text-primary placeholder:text-text-placeholder"
                />
                <button
                  type="submit"
                  className="min-h-11 rounded-button border-[1.5px] border-border-strong bg-surface px-4 text-label font-bold text-text-strong"
                >
                  {t('searchSubmit')}
                </button>
              </form>
              {me.permissions.includes('Admin.Shops.Create') && (
                <ButtonLink href="/admin/shops/new" size="md" icon="plus">
                  {t('add')}
                </ButtonLink>
              )}
            </div>

            {!data ? (
              <ErrorState />
            ) : (
              <>
                <ResponsiveTable
                  caption={t('caption')}
                  rows={[...data.items]}
                  rowKey={(shop) => shop.id}
                  empty={<EmptyState icon="store" title={t('empty.title')} body={t('empty.body')} />}
                  columns={[
                    {
                      key: 'name',
                      header: t('columns.name'),
                      mobile: 'primary',
                      width: '2fr',
                      cell: (shop) => (
                        <Link
                          href={`/admin/shops/${shop.id}`}
                          className="font-bold text-brand-700 hover:underline"
                        >
                          {lang === 'ar' ? shop.nameAr : shop.nameEn}
                        </Link>
                      ),
                    },
                    {
                      key: 'slug',
                      header: t('columns.slug'),
                      cell: (shop) => (
                        <span className="font-latin" dir="ltr">
                          {shop.slug}
                        </span>
                      ),
                    },
                    {
                      key: 'status',
                      header: t('columns.status'),
                      cell: (shop) => <ShopStatusBadge status={shop.status} />,
                    },
                    {
                      key: 'created',
                      header: t('columns.created'),
                      cell: (shop) => formatDate(shop.createdAt, lang),
                    },
                  ]}
                />
                {data.total > data.pageSize && (
                  <Pagination
                    page={data.page}
                    pageSize={data.pageSize}
                    total={data.total}
                    hrefForPage={hrefFor}
                  />
                )}
              </>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
