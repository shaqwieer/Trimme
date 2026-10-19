import { useLocale, useTranslations } from 'next-intl';
import { Badge } from '@/components/ui/Badge';
import type { components } from '@/lib/api/schema';
import { type AppLocale } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Reward = components['schemas']['BookingLoyaltyRewardResponse'];

/** «كوبون» on an appointment row: the customer chose a loyalty coupon for this visit (D-132); the title says which. */
export function LoyaltyChip({ reward }: { reward: Reward }) {
  const t = useTranslations('loyalty');
  const locale = useLocale() as AppLocale;
  const title = t('applied', { reward: localizedName(locale, reward.titleAr, reward.titleEn) });
  return (
    <Badge tone="success" size="sm" className="shrink-0">
      <span title={title} aria-label={title} data-testid="loyalty-chip">
        {t('chip')}
      </span>
    </Badge>
  );
}
