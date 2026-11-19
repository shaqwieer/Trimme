'use client';

import { useTranslations } from 'next-intl';
import { lazy, type ReactNode, Suspense, useEffect, useState } from 'react';
import { Link, usePathname } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';
import { withReturnTo } from '@/lib/auth/paths';
import { LoyaltyRewardRow } from './LoyaltyParts';

// Only a signed-in customer's visit shows their points: loaded with their card, not with every shop page.
const LoyaltyPointsCard = lazy(() => import('./LoyaltyPointsCard'));

type Rules = components['schemas']['LoyaltyRulesResponse'];
type Card = components['schemas']['MyShopLoyaltyResponse'];

/** The visitor's own card at this shop, `'signedOut'` (401), `'notCustomer'` (403, staff) or still loading. */
type Probe = Card | 'signedOut' | 'notCustomer' | 'loading';

/**
 * The loyalty card on a shop page (D-149): the shop's points rules for everyone, and for a signed-in customer their
 * points (counted on their mobile number), a ready reward with any points beyond it («+٢»), and D-132 coupons still
 * usable. Shown only while the shop's loyalty add-on is on. The heading and the rules come from the server page as
 * `children` (no script for static text); this part fetches the visitor's own card. A plain fetch, as for the favourite
 * heart: an anonymous visitor's 401 must not start a session refresh or a redirect.
 */
export function ShopLoyaltyCard({
  slug,
  rules,
  mayBeSignedIn,
  children,
}: {
  slug: string;
  rules: Rules;
  /** False when the page saw no session cookie: no request that could only answer 401. */
  mayBeSignedIn: boolean;
  /** The heading and the rules in words, rendered on the server. */
  children: ReactNode;
}) {
  const t = useTranslations('loyalty');
  const pathname = usePathname();
  const [probe, setProbe] = useState<Probe>(mayBeSignedIn ? 'loading' : 'signedOut');

  useEffect(() => {
    if (!mayBeSignedIn) return;
    let live = true;
    fetch(`/api/v1/me/loyalty/shops/${encodeURIComponent(slug)}`, {
      credentials: 'include',
      cache: 'no-store',
    })
      .then(async (response): Promise<Probe> => {
        if (response.ok) return (await response.json()) as Card;
        return response.status === 403 ? 'notCustomer' : 'signedOut';
      })
      .catch((): Probe => 'signedOut')
      .then((result) => {
        if (live) setProbe(result);
      });
    return () => {
      live = false;
    };
  }, [mayBeSignedIn, slug]);

  const card = typeof probe === 'object' ? probe : null;
  const coupons = card?.coupons.filter((r) => r.status === 'Available') ?? [];
  const canUse = (card?.rewardsReady ?? 0) > 0 || coupons.length > 0;

  return (
    <section
      aria-labelledby="loyalty-heading"
      className="flex flex-col gap-3 rounded-card border border-success-500/40 bg-surface p-4 shadow-e1"
      data-testid="shop-loyalty"
    >
      {children}
      {card && (
        <Suspense fallback={null}>
          <LoyaltyPointsCard card={card} rules={card.rules ?? rules} />
        </Suspense>
      )}
      {coupons.map((reward) => (
        <LoyaltyRewardRow key={reward.id} reward={reward} />
      ))}
      {canUse && <p className="text-helper text-text-secondary">{t('card.useWhenBooking')}</p>}
      {probe === 'signedOut' && (
        <Link
          href={withReturnTo('/auth/sign-in', pathname)}
          className="inline-flex min-h-11 items-center self-start text-label font-bold text-text-link hover:underline"
        >
          {t('signIn')}
        </Link>
      )}
    </section>
  );
}
