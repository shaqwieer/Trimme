import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { ContactShopButton } from '@/components/admin/ops/ContactShopButton';
import { ReviewActions } from '@/components/admin/ops/ReviewActions';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/cards';
import { Pagination } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { RatingStars } from '@/components/ui/Rating';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;
const QUEUES = ['NeedsReview', 'Published', 'Hidden', 'All'] as const;
const FLAGS = ['Reported', 'LowRating', 'ContainsPhone'] as const;
type Queue = (typeof QUEUES)[number];
type Flag = (typeof FLAGS)[number];

/**
 * Reviews moderation (a-reviews, R-AD-07, D-102). Reviews are published at once (D-017); the queue holds the published
 * ones with a report, a low rating or what looks like a phone number. Staff report, moderators hide with a reason or
 * publish again; the shop's and the professional's ratings follow.
 */
export default async function AdminReviewsPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/reviews'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminReviews' });
  const queueParam = firstParam(query.queue);
  const queue: Queue = (QUEUES as readonly string[]).includes(queueParam ?? '')
    ? (queueParam as Queue)
    : 'NeedsReview';
  const flagParam = firstParam(query.flag);
  const flag = (FLAGS as readonly string[]).includes(flagParam ?? '') ? (flagParam as Flag) : undefined;
  const search = firstParam(query.q)?.trim() || undefined;
  const page = Math.max(1, Number(firstParam(query.page)) || 1);
  const hrefWith = (changes: Record<string, string | number | undefined>) => {
    const next = new URLSearchParams();
    for (const [key, value] of Object.entries({
      queue: queue === 'NeedsReview' ? undefined : queue,
      flag,
      q: search,
      ...changes,
    })) {
      if (value !== undefined && value !== '') next.set(key, String(value));
    }
    const text = next.toString();
    return `/admin/reviews${text ? `?${text}` : ''}`;
  };

  return (
    <AdminFrame locale={locale} path="/admin/reviews" title={t('title')} permission="Admin.Reviews.View">
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/reviews', {
          params: { query: { queue, flag, search, page, pageSize: PAGE_SIZE } },
        });
        if (!data) return <ErrorState />;
        const canFlag = me.permissions.includes('Admin.Reviews.Flag');
        const canModerate = me.permissions.includes('Admin.Reviews.Moderate');
        const countOf: Record<Queue, number> = {
          NeedsReview: data.counts.needsReview,
          Published: data.counts.published,
          Hidden: data.counts.hidden,
          All: data.counts.all,
        };

        return (
          <div className="flex flex-col gap-4">
            <LinkTabs
              label={t('queues')}
              tabs={QUEUES.map((value) => ({
                href: hrefWith({ queue: value === 'NeedsReview' ? undefined : value, page: undefined }),
                label: `${t(`queue.${value}`)} · ${formatNumber(countOf[value], lang)}`,
                active: value === queue,
              }))}
            />
            <div className="flex flex-wrap items-center justify-between gap-3">
              <nav aria-label={t('flagFilter')}>
                <ul className="flex flex-wrap gap-2">
                  {[undefined, ...FLAGS].map((value) => (
                    <li key={value ?? 'any'}>
                      <Link
                        href={hrefWith({ flag: value, page: undefined })}
                        aria-current={value === flag ? 'page' : undefined}
                        className={`inline-flex min-h-11 items-center rounded-pill border-[1.5px] px-3.5 text-label font-bold ${
                          value === flag
                            ? 'border-navy-900 bg-navy-900 text-on-navy'
                            : 'border-border-strong bg-surface text-text-strong'
                        }`}
                      >
                        {value ? t(`flag.${value}`) : t('flag.any')}
                      </Link>
                    </li>
                  ))}
                </ul>
              </nav>
              <form method="get" role="search" className="flex min-w-0 gap-2">
                {queue !== 'NeedsReview' && <input type="hidden" name="queue" value={queue} />}
                {flag && <input type="hidden" name="flag" value={flag} />}
                <label htmlFor="review-search" className="sr-only">
                  {t('search')}
                </label>
                <input
                  id="review-search"
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
            </div>

            {data.items.length === 0 ? (
              <EmptyState
                icon="star"
                title={t('empty.title')}
                body={queue === 'NeedsReview' ? t('empty.queue') : t('empty.body')}
              />
            ) : (
              <ul aria-label={t('list')} className="flex flex-col gap-3">
                {data.items.map((review) => (
                  <Card as="li" key={review.id} className="flex flex-col gap-3 p-4">
                    <article
                      className="flex flex-col gap-3"
                      data-testid="admin-review"
                      aria-labelledby={`review-${review.id}`}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div className="min-w-0">
                          <h2 id={`review-${review.id}`} className="font-bold text-text-primary">
                            {review.authorName}
                          </h2>
                          <p className="text-helper text-text-secondary">
                            {localizedName(lang, review.shopNameAr, review.shopNameEn)} ·{' '}
                            {localizedName(lang, review.professionalNameAr, review.professionalNameEn)} ·{' '}
                            {formatDate(review.createdAt, lang, { withWeekday: false })}
                          </p>
                        </div>
                        <RatingStars value={review.rating} />
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {review.status === 'Hidden' && (
                          <Badge tone="neutral" size="sm">
                            {t('hidden')}
                          </Badge>
                        )}
                        {review.flags.map((f) => (
                          <Badge key={f} tone={f === 'LowRating' ? 'neutral' : 'danger'} size="sm">
                            {t(`flag.${f}`)}
                          </Badge>
                        ))}
                      </div>
                      {review.comment && (
                        <p className="text-caption leading-[1.8] text-text-strong">{review.comment}</p>
                      )}
                      <p className="text-helper text-text-tertiary">
                        {t('item', { name: localizedName(lang, review.itemNameAr, review.itemNameEn) })}
                        {me.permissions.includes('Admin.Bookings.View') && (
                          <>
                            {' · '}
                            <Link
                              href={`/admin/bookings/${review.bookingId}`}
                              className="font-bold text-brand-700 hover:underline"
                            >
                              {t('booking')}
                            </Link>
                          </>
                        )}
                      </p>
                      {review.flagReason && (
                        <p className="rounded-button bg-danger-50 p-2.5 text-helper text-danger-700">
                          {t('reportedFor', { reason: review.flagReason })}
                        </p>
                      )}
                      {review.status === 'Hidden' && review.moderationReason && (
                        <p className="rounded-button bg-bg-subtle p-2.5 text-helper text-text-secondary">
                          {t('hiddenFor', { reason: review.moderationReason })}
                        </p>
                      )}
                      <ReviewActions
                        reviewId={review.id}
                        version={review.version}
                        status={review.status}
                        reported={review.flagReason !== null && review.flagReason !== undefined}
                        canFlag={canFlag}
                        canModerate={canModerate}
                      />
                      {canModerate && <ContactShopButton reviewId={review.id} />}
                    </article>
                  </Card>
                ))}
              </ul>
            )}
            {data.total > data.pageSize && (
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                total={data.total}
                hrefForPage={(target) => hrefWith({ page: target })}
              />
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
