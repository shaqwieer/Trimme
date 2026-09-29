import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/cards';
import { Timeline, type TimelineItem } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { auditEntityHref, auditTone, localDateParam, startOfLocalDay } from '@/lib/admin/admin';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatTime } from '@/lib/i18n/format';
import { addDays } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 50;
const selectClass =
  'min-h-11 min-w-0 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input font-normal text-text-primary';

/**
 * The activity log (a-roles timeline, DV-A16, R-AD-12, D-104): newest first with filters for action, entity, actor,
 * shop and dates, links to the entity's page, and "older entries" through a keyset cursor. Entries never hold personal
 * data such as phone numbers or emails; the log is read-only.
 */
export default async function AdminAuditPage({ params, searchParams }: PageProps<'/[locale]/admin/audit'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminAudit' });
  const tActions = await getTranslations({ locale: lang, namespace: 'adminAudit.actions' });
  const action = firstParam(query.action) || undefined;
  const entityType = firstParam(query.entityType) || undefined;
  const entityId = firstParam(query.entityId)?.trim() || undefined;
  const actorUserId = firstParam(query.actor) || undefined;
  const shopId = firstParam(query.shop) || undefined;
  const from = localDateParam(firstParam(query.from));
  const to = localDateParam(firstParam(query.to));
  const cursor = firstParam(query.cursor) || undefined;
  const filters = { action, entityType, entityId, actor: actorUserId, shop: shopId, from, to };
  const hrefWith = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams();
    for (const [key, value] of Object.entries({ ...filters, ...changes })) {
      if (value) next.set(key, value);
    }
    const text = next.toString();
    return `/admin/audit${text ? `?${text}` : ''}`;
  };
  const tEntities = await getTranslations({ locale: lang, namespace: 'adminAudit.entities' });
  const entityLabel = (type: string) => (tEntities.has(type as 'Shop') ? tEntities(type as 'Shop') : type);
  const actionLabel = (code: string) => {
    const key = code.replace(/\./g, '_');
    return tActions.has(key as 'shop_created') ? tActions(key as 'shop_created') : code;
  };

  return (
    <AdminFrame locale={locale} path="/admin/audit" title={t('title')} permission="Admin.Audit.View">
      {async () => {
        const api = await getServerApi();
        const [{ data }, { data: facets }] = await Promise.all([
          api.GET('/api/v1/admin/audit', {
            params: {
              query: {
                action,
                entityType,
                entityId,
                actorUserId,
                shopId,
                from: from ? startOfLocalDay(from) : undefined,
                to: to ? startOfLocalDay(addDays(to, 1)) : undefined,
                cursor,
                pageSize: PAGE_SIZE,
              },
            },
          }),
          api.GET('/api/v1/admin/audit/facets'),
        ]);
        if (!data || !facets) return <ErrorState />;

        const items: TimelineItem[] = data.items.map((entry) => {
          const href = auditEntityHref(entry.entityType, entry.entityId);
          const shop = entry.shopId
            ? localizedName(lang, entry.shopNameAr ?? '', entry.shopNameEn)
            : undefined;
          return {
            id: entry.id,
            tone: auditTone(entry.action),
            title: (
              <>
                {actionLabel(entry.action)}
                {entry.summary && <span className="block font-normal text-text-strong">{entry.summary}</span>}
              </>
            ),
            meta: (
              <>
                <bdi>{entry.actorName ?? t(`actorType.${entry.actorType as 'System'}`)}</bdi> ·{' '}
                {formatDate(entry.occurredAt, lang, { withWeekday: false, withYear: true })}{' '}
                {formatTime(entry.occurredAt, lang)}
                {shop && (
                  <>
                    {' · '}
                    <bdi>{shop}</bdi>
                  </>
                )}
                {entry.reason && (
                  <span className="block text-text-secondary">{t('reason', { reason: entry.reason })}</span>
                )}
                <span className="mt-0.5 flex flex-wrap gap-x-3">
                  {href && (
                    <Link href={href} className="font-bold text-brand-700 hover:underline">
                      {t('open', { type: entityLabel(entry.entityType) })}
                    </Link>
                  )}
                  <Link
                    href={hrefWith({
                      entityType: entry.entityType,
                      entityId: entry.entityId,
                      cursor: undefined,
                    })}
                    className="font-bold text-brand-700 hover:underline"
                  >
                    {t('history')}
                  </Link>
                  {entry.actorUserId && (
                    <Link
                      href={hrefWith({ actor: entry.actorUserId, cursor: undefined })}
                      className="font-bold text-brand-700 hover:underline"
                    >
                      {t('byActor')}
                    </Link>
                  )}
                </span>
              </>
            ),
          };
        });

        return (
          <div className="flex flex-col gap-4">
            <form
              method="get"
              aria-label={t('filters')}
              className="grid gap-3 rounded-card border border-border bg-surface p-4 shadow-e1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-[1.4fr_1fr_1fr_1fr_1fr_auto]"
            >
              {actorUserId && <input type="hidden" name="actor" value={actorUserId} />}
              {shopId && <input type="hidden" name="shop" value={shopId} />}
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('action')}
                <select name="action" defaultValue={action ?? ''} className={selectClass}>
                  <option value="">{t('allActions')}</option>
                  {facets.actions.map((code) => (
                    <option key={code} value={code}>
                      {actionLabel(code)}
                    </option>
                  ))}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('entityType')}
                <select name="entityType" defaultValue={entityType ?? ''} className={selectClass}>
                  <option value="">{t('allEntities')}</option>
                  {facets.entityTypes.map((type) => (
                    <option key={type} value={type}>
                      {entityLabel(type)}
                    </option>
                  ))}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('entityId')}
                <input
                  name="entityId"
                  defaultValue={entityId}
                  dir="ltr"
                  className={`${selectClass} font-latin`}
                />
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('from')}
                <input name="from" type="date" defaultValue={from} className={`${selectClass} font-latin`} />
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('to')}
                <input name="to" type="date" defaultValue={to} className={`${selectClass} font-latin`} />
              </label>
              <div className="flex items-end gap-2">
                <button
                  type="submit"
                  className="min-h-11 rounded-button bg-navy-900 px-4 text-label font-bold text-on-navy"
                >
                  {t('apply')}
                </button>
              </div>
            </form>
            {(actorUserId || shopId || action || entityType || entityId || from || to) && (
              <p className="text-caption text-text-secondary">
                {t('filtered')}{' '}
                <Link href="/admin/audit" className="font-bold text-brand-700 hover:underline">
                  {t('clear')}
                </Link>
              </p>
            )}
            <Card as="section" className="flex flex-col gap-4 p-5">
              <h2 className="sr-only">{t('entries')}</h2>
              <div data-testid="audit-log">
                {items.length === 0 ? (
                  <EmptyState icon="book" title={t('empty')} />
                ) : (
                  <Timeline items={items} />
                )}
              </div>
            </Card>
            <div className="flex flex-wrap gap-2">
              {cursor && (
                <ButtonLink href={hrefWith({ cursor: undefined })} variant="ghost" size="md">
                  {t('newest')}
                </ButtonLink>
              )}
              {data.nextCursor && (
                <ButtonLink href={hrefWith({ cursor: data.nextCursor })} variant="outline" size="md">
                  {t('older')}
                </ButtonLink>
              )}
            </div>
          </div>
        );
      }}
    </AdminFrame>
  );
}
