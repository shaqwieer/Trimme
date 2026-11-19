import { useLocale, useTranslations } from 'next-intl';
import { Badge } from '@/components/ui/Badge';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatDate, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Rules = components['schemas']['LoyaltyRulesResponse'];
type Reward = components['schemas']['MyLoyaltyRewardResponse'];
export type LoyaltyRewardTerms = Pick<
  Rules,
  'rewardType' | 'rewardServiceNameAr' | 'rewardServiceNameEn' | 'rewardAmount'
>;

/** The reward in the reader's language (D-149): «حلاقة مجاناً», or «خصم ٥٠ ⃁» written as the app writes prices. */
export function useLoyaltyRewardName() {
  const t = useTranslations('loyalty.reward');
  const locale = useLocale() as AppLocale;
  return (reward: LoyaltyRewardTerms) =>
    reward.rewardType === 'FreeService'
      ? t('FreeService', {
          service: localizedName(locale, reward.rewardServiceNameAr ?? '', reward.rewardServiceNameEn),
        })
      : t('AmountOff', { amount: formatPrice(reward.rewardAmount ?? 0, locale) });
}

/**
 * The shop's points rules in words (D-149): «اجمع ١٠ نقاط واحصل على حلاقة مجاناً», then the points per visit and what
 * happens without visits. The same text everywhere the rules are shown, and in the owner's preview.
 */
export function LoyaltyRulesText({ rules }: { rules: Rules }) {
  const t = useTranslations('loyalty');
  const rewardName = useLoyaltyRewardName();
  const points = (count: number) => t('points', { count });
  return (
    <div className="flex flex-col gap-1" data-testid="loyalty-rules">
      <p className="text-label font-bold text-navy-900">
        {t('rules.reward', { count: rules.pointsForReward, reward: rewardName(rules) })}
      </p>
      <p className="text-helper text-text-secondary">
        {rules.firstVisitPoints === rules.visitPoints
          ? t('rules.earnSame', { visit: points(rules.visitPoints) })
          : t('rules.earn', { first: points(rules.firstVisitPoints), visit: points(rules.visitPoints) })}{' '}
        {rules.inactivityRule === 'None' || !rules.inactivityDays
          ? t('rules.None')
          : t(`rules.${rules.inactivityRule}`, { days: t('days', { count: rules.inactivityDays }) })}
      </p>
    </div>
  );
}

/** One D-132 coupon still usable (earned before the points programme): what it gives, its code, until when, its booking. */
export function LoyaltyRewardRow({ reward }: { reward: Reward }) {
  const t = useTranslations('loyalty');
  const locale = useLocale() as AppLocale;
  const held = reward.status === 'Held';
  return (
    <div
      className="flex flex-wrap items-center gap-3 rounded-card border border-success-500/40 bg-success-50 p-3"
      data-testid="loyalty-reward"
      data-status={reward.status}
    >
      <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-success-500 text-on-navy">
        <Icon name="tag" className="size-5" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <p className="text-label font-bold text-success-700">
          {localizedName(locale, reward.titleAr, reward.titleEn)}
        </p>
        <p className="text-helper text-text-secondary">
          {t('code')}{' '}
          <bdi dir="ltr" className="font-mono font-bold tracking-wider text-text-primary">
            {reward.code}
          </bdi>
          {' — '}
          {reward.expiresAt
            ? t('expires', {
                date: formatDate(reward.expiresAt, locale, { withYear: true, withWeekday: false }),
              })
            : t('noExpiry')}
        </p>
      </div>
      {held ? (
        <div className="flex items-center gap-2">
          <Badge tone="info">{t('held')}</Badge>
          {reward.bookingId && (
            <Link
              href={`/account/bookings/${reward.bookingId}`}
              className="inline-flex min-h-11 items-center text-helper font-bold text-text-link hover:underline"
            >
              {t('viewBooking')}
            </Link>
          )}
        </div>
      ) : (
        <Badge tone="success">{t('ready')}</Badge>
      )}
    </div>
  );
}
