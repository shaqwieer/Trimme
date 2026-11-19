import { useLocale, useTranslations } from 'next-intl';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDate, formatNumber } from '@/lib/i18n/format';

type HistoryEntry = components['schemas']['LoyaltyHistoryEntryResponse'];
type Stats = components['schemas']['LoyaltyStatsResponse'];

// Server-rendered only (the customer's cards, the owner's and the admin's loyalty pages): kept out of LoyaltyParts,
// which client components import, so these never reach a client bundle.

/** The card's history, newest first (D-149): points earned with the barber who closed the visit, rewards used, points lost. */
export function LoyaltyHistory({ history }: { history: HistoryEntry[] }) {
  const t = useTranslations('loyalty.history');
  const locale = useLocale() as AppLocale;
  if (history.length === 0) return null;
  return (
    <details className="flex flex-col" data-testid="loyalty-history">
      <summary className="inline-flex min-h-11 cursor-pointer items-center text-label font-bold text-text-link">
        {t('title')}
      </summary>
      <ul className="flex flex-col divide-y divide-border-row">
        {history.map((entry, index) => (
          <li key={`${entry.at}-${index}`} className="flex items-center justify-between gap-3 py-2">
            <span className="flex min-w-0 flex-col">
              <span className="text-caption text-text-primary">
                {entry.kind === 'Earned'
                  ? entry.barberName
                    ? t('Earned', { barber: entry.barberName })
                    : t('EarnedNoBarber')
                  : t(entry.kind)}
              </span>
              <span className="text-helper text-text-tertiary">
                {formatDate(entry.at, locale, { withYear: true, withWeekday: false })}
              </span>
            </span>
            <span
              dir="ltr"
              className={cn(
                'shrink-0 text-label font-bold',
                entry.points > 0 ? 'text-success-700' : 'text-text-secondary',
              )}
            >
              {entry.points > 0 ? '+' : '−'}
              {formatNumber(Math.abs(entry.points), locale)}
            </span>
          </li>
        ))}
      </ul>
    </details>
  );
}

/** How the shop's programme is used (D-149), for the owner and the admin: members, points given, rewards by state. */
export function LoyaltyStatsGrid({ stats }: { stats: Stats }) {
  const t = useTranslations('loyalty.rulesForm.stats');
  const locale = useLocale() as AppLocale;
  return (
    <dl className="grid grid-cols-2 gap-3 text-caption md:grid-cols-5" data-testid="loyalty-stats">
      {(
        [
          ['members', stats.members],
          ['pointsEarned', stats.pointsEarned],
          ['rewardsReady', stats.rewardsReady],
          ['rewardsHeld', stats.rewardsHeld],
          ['rewardsRedeemed', stats.rewardsRedeemed],
        ] as const
      ).map(([key, value]) => (
        <div key={key}>
          <dt className="text-helper text-text-tertiary">{t(key)}</dt>
          <dd className="text-h3 font-bold text-text-primary">{formatNumber(value, locale)}</dd>
        </div>
      ))}
    </dl>
  );
}
