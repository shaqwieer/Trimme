import { useLocale, useTranslations } from 'next-intl';
import { Badge } from '@/components/ui/Badge';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatDate, formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { LoyaltyStamps } from './LoyaltyStamps';

type Program = components['schemas']['LoyaltyProgramResponse'];
type Reward = components['schemas']['MyLoyaltyRewardResponse'];

/** "Complete 6 visits and get a free haircut": the programme's offer in one line (D-132). */
export function LoyaltyOffer({ program }: { program: Program }) {
  const t = useTranslations('loyalty');
  const locale = useLocale() as AppLocale;
  return (
    <p className="text-label font-bold text-navy-900">
      {t('offer', {
        visits: program.requiredVisits,
        visitsText: formatNumber(program.requiredVisits, locale),
        reward: localizedName(locale, program.rewardTitleAr, program.rewardTitleEn),
      })}
    </p>
  );
}

/** The circles and what is left before the coupon. */
export function LoyaltyProgress({ program, stamps }: { program: Program; stamps: number }) {
  const t = useTranslations('loyalty');
  const locale = useLocale() as AppLocale;
  const left = Math.max(program.requiredVisits - stamps, 0);
  return (
    <div className="flex flex-col gap-2">
      <LoyaltyStamps required={program.requiredVisits} stamps={stamps} />
      {left > 0 && (
        <p className="text-helper text-text-secondary">
          {t('remaining', { left, leftText: formatNumber(left, locale) })}
        </p>
      )}
    </div>
  );
}

/** One earned coupon: what it gives, its code, until when, and the booking it is reserved for. */
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
          {' · '}
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
