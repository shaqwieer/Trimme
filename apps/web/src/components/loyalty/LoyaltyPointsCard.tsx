import { useTranslations } from 'next-intl';
import { Icon } from '@/components/ui/icons';
import type { components } from '@/lib/api/schema';
import { useLoyaltyRewardName } from './LoyaltyParts';
import { LoyaltyStamps } from './LoyaltyStamps';

type Rules = components['schemas']['LoyaltyRulesResponse'];
type Card = components['schemas']['MyShopLoyaltyResponse'];

/** Up to this many points a card is drawn as circles; a longer one as a bar. */
const CIRCLES_MAX = 12;

/**
 * The customer's card at one shop (D-149). A full card does not stop: with a reward ready the card is drawn full and the
 * points beyond it show as «+٢», the start of the next card. Then what is left to the next reward and any reward held for
 * an upcoming booking. A newcomer's empty card says nothing about what is left: the first visit gives its own points.
 */
export function LoyaltyPointsCard({
  card,
  rules,
}: {
  card: Pick<Card, 'points' | 'rewardsReady' | 'currentCard' | 'pointsToNext' | 'rewardsHeld' | 'history'>;
  rules: Rules;
}) {
  const t = useTranslations('loyalty');
  const rewardName = useLoyaltyRewardName();
  const needed = rules.pointsForReward;
  const full = card.rewardsReady > 0;
  const shown = full ? needed : Math.min(card.currentCard, needed);
  const newcomer = card.points === 0 && card.rewardsHeld === 0 && card.history.length === 0;
  const extraLabel = t('card.extraLabel', { points: t('points', { count: card.currentCard }) });

  return (
    <div
      className="flex flex-col gap-2"
      data-testid="loyalty-points"
      data-points={card.points}
      data-ready={card.rewardsReady}
    >
      <div className="flex flex-wrap items-center gap-2">
        {needed <= CIRCLES_MAX ? (
          <LoyaltyStamps required={needed} stamps={shown} />
        ) : (
          <div className="flex min-w-[180px] flex-1 flex-col gap-1">
            <div
              role="progressbar"
              aria-label={t('card.progress', { current: shown, needed })}
              aria-valuemin={0}
              aria-valuemax={needed}
              aria-valuenow={shown}
              className="h-2.5 overflow-hidden rounded-full bg-bg-subtle"
            >
              <div
                className="h-full rounded-full bg-success-500"
                style={{ width: `${Math.round((100 * shown) / needed)}%` }}
              />
            </div>
            <span className="text-helper font-semibold text-text-secondary">
              {t('card.progress', { current: shown, needed })}
            </span>
          </div>
        )}
        {full && card.currentCard > 0 && (
          <span
            className="inline-flex min-h-8 items-center rounded-full bg-success-500 px-3 text-label font-extrabold text-on-navy"
            title={extraLabel}
            data-testid="loyalty-extra"
          >
            <span aria-hidden="true" dir="ltr">
              {t('card.extra', { count: card.currentCard })}
            </span>
            <span className="sr-only">{extraLabel}</span>
          </span>
        )}
      </div>
      {full && (
        <p
          className="flex items-center gap-1.5 text-label font-bold text-success-700"
          data-testid="loyalty-ready"
        >
          <Icon name="tag" className="size-4 shrink-0" />
          {t('card.ready', { count: card.rewardsReady })} — {rewardName(rules)}
        </p>
      )}
      {!newcomer && (
        <p className="text-helper text-text-secondary">
          {t(full ? 'card.toNextMore' : 'card.toNext', { points: t('points', { count: card.pointsToNext }) })}
        </p>
      )}
      {card.rewardsHeld > 0 && (
        <p className="text-helper font-semibold text-text-secondary">
          {t('card.held', { count: card.rewardsHeld })}
        </p>
      )}
    </div>
  );
}

/** The default export, for the shop page's lazy load (only a signed-in customer's visit needs it). */
export default LoyaltyPointsCard;
