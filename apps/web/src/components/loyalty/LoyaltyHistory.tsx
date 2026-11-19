import { useLocale, useTranslations } from 'next-intl';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDate, formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type HistoryEntry = components['schemas']['LoyaltyHistoryEntryResponse'];
type Stats = components['schemas']['LoyaltyStatsResponse'];

// Server-rendered only (the customer's cards, the owner's and the admin's loyalty pages): kept out of LoyaltyParts,
// which client components import, so these never reach a client bundle.

/**
 * The card's history, newest first (D-149): points earned with the barber who closed the visit, rewards used, points lost.
 * D-152 (the client's 2-5): a used reward is named («استخدمت: حلاقة شعر مجانية»), a D-132 coupon too, and the last one
 * stays in sight above the disclosure, so a card emptied by its reward still says what happened.
 */
export function LoyaltyHistory({ history }: { history: HistoryEntry[] }) {
  const t = useTranslations('loyalty.history');
  const locale = useLocale() as AppLocale;
  if (history.length === 0) return null;
  const title = (entry: HistoryEntry) =>
    entry.rewardTitleAr ? localizedName(locale, entry.rewardTitleAr, entry.rewardTitleEn) : undefined;
  const label = (entry: HistoryEntry) => {
    if (entry.kind === 'Earned')
      return entry.barberName ? t('Earned', { barber: entry.barberName }) : t('EarnedNoBarber');
    const reward = entry.kind === 'Redeemed' ? title(entry) : undefined;
    return reward ? t('used', { reward }) : t(entry.kind);
  };
  const date = (entry: HistoryEntry) => formatDate(entry.at, locale, { withYear: false, withWeekday: false });
  const lastUsed = history.find((entry) => entry.kind === 'Redeemed' && title(entry));
  return (
    <div className="flex flex-col gap-1">
      {lastUsed && (
        <p className="text-helper font-semibold text-text-secondary" data-testid="loyalty-last-used">
          {label(lastUsed)} — {date(lastUsed)}
        </p>
      )}
      <details className="flex flex-col" data-testid="loyalty-history">
        <summary className="inline-flex min-h-11 cursor-pointer items-center text-label font-bold text-text-link">
          {t('title')}
        </summary>
        <ul className="flex flex-col divide-y divide-border-row">
          {history.map((entry, index) => (
            <li key={`${entry.at}-${index}`} className="flex items-center justify-between gap-3 py-2">
              <span className="flex min-w-0 flex-col">
                <span className="text-caption text-text-primary">{label(entry)}</span>
                <span className="text-helper text-text-tertiary">
                  {formatDate(entry.at, locale, { withYear: true, withWeekday: false })}
                </span>
              </span>
              {/* A D-132 coupon used costs no points: no figure. */}
              {entry.points !== 0 && (
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
              )}
            </li>
          ))}
        </ul>
      </details>
    </div>
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
