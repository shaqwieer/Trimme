'use client';

import { useMemo, useSyncExternalStore } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { distanceKm, parseLocation } from '@/lib/discovery/location';
import { readLocationCookieRaw } from '@/lib/discovery/location.client';
import { type AppLocale, formatDistanceKm } from '@/lib/i18n/format';

const noSubscription = () => () => undefined;
const serverSnapshot = () => null;

/**
 * "2.4 كم" on the shop page, computed in the browser from the location chosen on this device, so the shared (cached)
 * page never depends on who is looking at it. Renders nothing without a location.
 */
export function ShopDistance({ latitude, longitude }: { latitude: number; longitude: number }) {
  const t = useTranslations('shopPage');
  const locale = useLocale() as AppLocale;
  // The server snapshot is null, so the shared HTML never shows a distance; the browser fills it in after hydration.
  const raw = useSyncExternalStore(noSubscription, readLocationCookieRaw, serverSnapshot);
  const km = useMemo(() => {
    const location = parseLocation(raw);
    return location ? distanceKm(location, { lat: latitude, lng: longitude }) : null;
  }, [raw, latitude, longitude]);

  if (km === null) return null;
  return (
    <>
      <span aria-hidden="true" className="text-border-strong">
        ·
      </span>
      <span className="font-latin font-semibold text-text-strong">{t('distance', { km: formatDistanceKm(km, locale) })}</span>
    </>
  );
}
