import { useLocale, useTranslations } from 'next-intl';
import { Timeline, type TimelineItem } from '@/components/ui/data';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatDate, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { longDate } from './periods';

type Subscription = components['schemas']['AdminShopSubscriptionResponse'];

/**
 * Complete commercial history for admins (R-SUB-03): every period with the plan name and price version it was
 * recorded with, and every override with its before/after values and reason, newest first.
 */
export function SubscriptionHistory({ subscription }: { subscription: Subscription }) {
  const t = useTranslations('adminSubscriptions.panel');
  const locale = useLocale() as AppLocale;
  const currency = subscription.currency ?? 'SAR';

  const periods = subscription.periods.map((p) => ({
    at: p.recordedAt,
    day: p.periodStart,
    item: {
      id: p.id,
      tone: p.kind === 'Renewed' ? 'success' : 'brand',
      title: t('periodTitle', {
        plan: localizedName(locale, p.planNameAr, p.planNameEn),
        start: longDate(p.periodStart, locale),
        end: longDate(p.periodEnd, locale),
      }),
      meta: [
        t('periodMeta', { kind: t(`kinds.${p.kind}`), price: formatPrice(p.amount, locale, p.currency) }),
        p.priceVersionNumber != null ? t('priceVersion', { version: p.priceVersionNumber }) : null,
        p.pricingReason
          ? t('customEntry', {
              reason: p.pricingReason,
              standard: p.standardAmount != null ? formatPrice(p.standardAmount, locale, p.currency) : '—',
            })
          : p.isOverridden
            ? t('overriddenTag')
            : null,
        p.notes,
      ]
        .filter(Boolean)
        .join(' · '),
    } satisfies TimelineItem,
  }));

  const overrides = subscription.overrides.map((o) => {
    const changes = [
      o.newAmount != null
        ? t('changePrice', {
            from: formatPrice(o.previousAmount, locale, currency),
            to: formatPrice(o.newAmount, locale, currency),
          })
        : null,
      o.newEnd != null
        ? t('changeEnd', { from: longDate(o.previousEnd, locale), to: longDate(o.newEnd, locale) })
        : null,
    ].filter(Boolean);
    return {
      at: o.overriddenAt,
      day: '',
      item: {
        id: o.id,
        tone: 'warning',
        title: t('overrideEntry', { changes: changes.join(locale === 'ar' ? '؛ ' : '; ') }),
        meta: t('overrideMeta', {
          reason: o.reason,
          date: formatDate(o.overriddenAt, locale, { withYear: true, withWeekday: false }),
        }),
      } satisfies TimelineItem,
    };
  });

  // Newest first; periods recorded together (e.g. seeded) fall back to their start date.
  const items = [...periods, ...overrides]
    .sort((a, b) => (a.at === b.at ? b.day.localeCompare(a.day) : a.at < b.at ? 1 : -1))
    .map((entry) => entry.item);
  return <Timeline items={items} />;
}
