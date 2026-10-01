import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { ProfessionalStatusBadge } from '@/components/admin/ProfessionalStatusBadge';
import { Avatar } from '@/components/ui/Avatar';
import { ButtonLink } from '@/components/ui/Button';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;
const STATUSES = ['Active', 'Disabled'] as const;

/**
 * Admin professionals list (a-pros, corrected per DV-S01: each professional stays in one shop). Search and status filter, masked
 * WhatsApp numbers, each row links to the professional. Server-rendered.
 */
export default async function AdminProfessionalsPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/professionals'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminProfessionals' });
  const search = firstParam(query.q)?.trim() || undefined;
  const statusParam = firstParam(query.status);
  const status = STATUSES.find((value) => value === statusParam);
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame
      locale={locale}
      path="/admin/professionals"
      title={t('title')}
      permission="Admin.Professionals.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/professionals', {
          params: { query: { page, pageSize: PAGE_SIZE, search, status } },
        });
        const hrefFor = (target: number) =>
          `/admin/professionals?page=${target}${search ? `&q=${encodeURIComponent(search)}` : ''}${status ? `&status=${status}` : ''}`;

        return (
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <form
                method="get"
                role="search"
                className="flex min-w-0 flex-1 flex-wrap gap-2 md:max-w-[560px]"
              >
                <label htmlFor="pro-search" className="sr-only">
                  {t('search')}
                </label>
                <input
                  id="pro-search"
                  name="q"
                  defaultValue={search}
                  placeholder={t('search')}
                  className="h-11 min-w-0 flex-1 rounded-field border border-border-input bg-surface px-3 text-body focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
                />
                <label htmlFor="pro-status" className="sr-only">
                  {t('statusFilter')}
                </label>
                <select
                  id="pro-status"
                  name="status"
                  defaultValue={status ?? ''}
                  className="h-11 rounded-field border border-border-input bg-surface px-3 text-body focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
                >
                  <option value="">{t('allStatuses')}</option>
                  {STATUSES.map((value) => (
                    <option key={value} value={value}>
                      {t(`status.${value}`)}
                    </option>
                  ))}
                </select>
                <button
                  type="submit"
                  className="min-h-11 rounded-button border-[1.5px] border-border-strong bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
                >
                  {t('searchSubmit')}
                </button>
              </form>
              {me.permissions.includes('Admin.Professionals.Create') && (
                <ButtonLink href="/admin/professionals/new" size="md" icon="plus">
                  {t('add')}
                </ButtonLink>
              )}
            </div>

            {!data ? (
              <ErrorState />
            ) : data.items.length === 0 ? (
              <EmptyState icon="users" title={t('empty.title')} body={t('empty.body')} />
            ) : (
              <>
                <ResponsiveTable
                  caption={t('caption')}
                  rows={data.items}
                  rowKey={(row) => row.id}
                  columns={[
                    {
                      key: 'name',
                      header: t('columns.name'),
                      cell: (row) => {
                        const name = lang === 'ar' ? row.nameAr : row.nameEn;
                        const specialty = lang === 'ar' ? row.specialtyAr : row.specialtyEn;
                        return (
                          <span className="flex items-center gap-3">
                            <Avatar name={name} src={row.avatarUrl} size="sm" />
                            <span className="flex flex-col">
                              <Link
                                href={`/admin/professionals/${row.id}`}
                                className="font-bold text-text-link hover:underline"
                              >
                                {name}
                              </Link>
                              {specialty && (
                                <span className="text-helper text-text-tertiary">{specialty}</span>
                              )}
                            </span>
                          </span>
                        );
                      },
                    },
                    {
                      key: 'shop',
                      header: t('columns.shop'),
                      cell: (row) => (
                        <Link
                          href={`/admin/shops/${row.shopId}`}
                          className="text-text-primary hover:underline"
                        >
                          {lang === 'ar' ? row.shopNameAr : row.shopNameEn}
                        </Link>
                      ),
                    },
                    {
                      key: 'whatsapp',
                      header: t('columns.whatsapp'),
                      cell: (row) => (
                        <span className="flex flex-col">
                          <span dir="ltr" className="text-start font-latin">
                            {row.whatsApp.masked ?? t('noNumber')}
                          </span>
                          {row.whatsApp.masked && !row.whatsApp.notificationsEnabled && (
                            <span className="text-helper text-text-tertiary">{t('notificationsOff')}</span>
                          )}
                        </span>
                      ),
                    },
                    {
                      key: 'status',
                      header: t('columns.status'),
                      cell: (row) => <ProfessionalStatusBadge status={row.status} />,
                    },
                  ]}
                />
                <Pagination
                  page={data.page}
                  pageSize={data.pageSize}
                  total={data.total}
                  hrefForPage={hrefFor}
                />
              </>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
