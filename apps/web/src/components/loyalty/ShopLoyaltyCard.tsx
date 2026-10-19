'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Icon } from '@/components/ui/icons';
import { Link, usePathname } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';
import { withReturnTo } from '@/lib/auth/paths';
import { LoyaltyOffer, LoyaltyProgress, LoyaltyRewardRow } from './LoyaltyParts';

type Program = components['schemas']['LoyaltyProgramResponse'];
type Card = components['schemas']['MyShopLoyaltyResponse'];

/** The visitor's own card at this shop, `'signedOut'` (401), `'notCustomer'` (403, staff) or still loading. */
type Probe = Card | 'signedOut' | 'notCustomer' | 'loading';

/**
 * The loyalty card on a shop page (D-132): the offer for everyone, and for a signed-in customer their circles and any
 * coupon ready to use. A plain fetch, as for the favourite heart: an anonymous visitor's 401 must not start a session
 * refresh or a redirect.
 */
export function ShopLoyaltyCard({
  slug,
  program,
  mayBeSignedIn,
}: {
  slug: string;
  program: Program;
  /** False when the page saw no session cookie: no request that could only answer 401. */
  mayBeSignedIn: boolean;
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
  const ready = card?.rewards.filter((r) => r.status === 'Available') ?? [];

  return (
    <section
      aria-labelledby="loyalty-heading"
      className="flex flex-col gap-3 rounded-card border border-success-500/40 bg-surface p-4 shadow-e1"
      data-testid="shop-loyalty"
    >
      <div className="flex items-center gap-2">
        <Icon name="tag" className="size-5 text-success-700" />
        <h2 id="loyalty-heading" className="text-h3 font-bold text-navy-900">
          {t('programTitle')}
        </h2>
      </div>
      <LoyaltyOffer program={program} />
      <p className="text-helper text-text-secondary">{t('anyProfessional')}</p>
      <LoyaltyProgress program={program} stamps={card?.stamps ?? 0} />
      {ready.map((reward) => (
        <LoyaltyRewardRow key={reward.id} reward={reward} />
      ))}
      {ready.length > 0 && <p className="text-helper text-text-secondary">{t('useWhenBooking')}</p>}
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
