'use client';

import { useMemo, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { SearchField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import type { DiscoveryArea } from '@/lib/api/public-types';
import { saveLocation } from '@/lib/discovery/location.client';
import { roundForPrivacy } from '@/lib/discovery/location';
import { SearchText } from '@/lib/discovery/text';

type Status = 'idle' | 'locating' | 'denied' | 'unavailable';

/**
 * Location permission and manual district choice (c-auth 972–987, DV-A21). "Allow" asks the browser only when pressed;
 * the manual list is every district that has listed shops (no third-party geocoding). Either choice is kept in a
 * first-party cookie on this device only (D-095), then the page continues to `returnTo` (or refreshes in place).
 */
export function LocationChooser({
  areas,
  returnTo,
  onDone,
  startManual = false,
}: {
  areas: DiscoveryArea[];
  /** Where to go after choosing; omitted = refresh the current page. */
  returnTo?: string;
  onDone?: () => void;
  startManual?: boolean;
}) {
  const t = useTranslations('location');
  const router = useRouter();
  const [status, setStatus] = useState<Status>('idle');
  const [manual, setManual] = useState(startManual);
  const [filter, setFilter] = useState('');

  const visible = useMemo(() => {
    const query = SearchText.normalize(filter);
    return areas.filter(
      (area) => !query || SearchText.normalize(`${area.district ?? ''} ${area.city}`).includes(query),
    );
  }, [areas, filter]);

  function finish() {
    onDone?.();
    if (returnTo) router.push(returnTo);
    else router.refresh();
  }

  function useDevice() {
    if (!('geolocation' in navigator)) {
      setStatus('unavailable');
      setManual(true);
      return;
    }
    setStatus('locating');
    navigator.geolocation.getCurrentPosition(
      (position) => {
        saveLocation({
          lat: roundForPrivacy(position.coords.latitude),
          lng: roundForPrivacy(position.coords.longitude),
          label: '',
          source: 'device',
        });
        setStatus('idle');
        finish();
      },
      () => {
        setStatus('denied');
        setManual(true);
      },
      { enableHighAccuracy: false, maximumAge: 10 * 60 * 1000, timeout: 15000 },
    );
  }

  function chooseArea(area: DiscoveryArea) {
    saveLocation({
      lat: area.latitude,
      lng: area.longitude,
      label: area.district ?? area.city,
      source: 'area',
    });
    finish();
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-3">
        <Button
          type="button"
          variant="primary"
          size="lg"
          fullWidth
          icon="pin"
          onClick={useDevice}
          disabled={status === 'locating'}
        >
          {status === 'locating' ? t('locating') : t('allow')}
        </Button>
        {!manual && (
          <Button type="button" variant="ghost" size="lg" fullWidth onClick={() => setManual(true)}>
            {t('manual')}
          </Button>
        )}
      </div>
      {(status === 'denied' || status === 'unavailable') && (
        <InlineAlert tone="warning" title={status === 'denied' ? t('denied') : t('unavailable')} />
      )}
      {manual && (
        <section aria-labelledby="manual-area-title" className="flex flex-col gap-3">
          <h2 id="manual-area-title" className="text-label font-bold text-text-primary">
            {t('manualTitle')}
          </h2>
          <SearchField
            label={t('filter')}
            placeholder={t('filter')}
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            onClear={() => setFilter('')}
          />
          {visible.length === 0 ? (
            <p className="text-helper text-text-secondary">{t('noAreas')}</p>
          ) : (
            <ul aria-label={t('areasLabel')} className="flex max-h-[45dvh] flex-col gap-1 overflow-y-auto">
              {visible.map((area) => (
                <li key={`${area.city}|${area.district ?? ''}`}>
                  <button
                    type="button"
                    onClick={() => chooseArea(area)}
                    className="flex min-h-11 w-full items-center justify-between gap-3 rounded-field px-3 py-2 text-start hover:bg-bg-subtle"
                  >
                    <span className="flex items-center gap-2 text-label font-bold text-text-primary">
                      <Icon name="pin" className="size-4 text-brand-600" />
                      {area.district
                        ? t('areaLabel', { district: area.district, city: area.city })
                        : area.city}
                    </span>
                    <span className="text-helper text-text-secondary">
                      {t('areaShops', { count: area.shopCount })}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}
