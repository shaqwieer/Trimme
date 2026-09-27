'use client';

import { type FormEvent, useCallback, useRef, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { TextField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import {
  DEFAULT_CENTER,
  formatCoordinates,
  isValidLatLng,
  type LatLng,
  roundCoordinate,
} from '@/lib/map/config';
import { MapLibreView } from './MapLibreView';
import type { MapViewComponent } from './MapView';

type Place = components['schemas']['GeocodedPlace'];
export type LocationSource = 'Manual' | 'Geocoded' | 'Device';

/** What the picker saves: the confirmed point, its address and how it was chosen. */
export type PickedLocation = {
  latitude: number;
  longitude: number;
  addressLine: string | null;
  district: string | null;
  city: string | null;
  formattedAddress: string | null;
  source: LocationSource;
};

type LocationPickerProps = {
  initial: PickedLocation | null;
  /** Which geocoding endpoints to use: the admin's or the shop's (both proxy the same server-side provider). */
  scope: 'admin' | 'shop';
  onSave: (location: PickedLocation) => Promise<void>;
  /** The map adapter; tests pass a fake. */
  MapComponent?: MapViewComponent;
};

type Address = Pick<PickedLocation, 'addressLine' | 'district' | 'city' | 'formattedAddress'>;

const EMPTY_ADDRESS: Address = { addressLine: null, district: null, city: null, formattedAddress: null };

async function searchPlaces(scope: 'admin' | 'shop', q: string, lang: string): Promise<Place[]> {
  const query = { params: { query: { q, lang } } };
  const { data } =
    scope === 'admin'
      ? await browserApi.GET('/api/v1/admin/geo/search', query)
      : await browserApi.GET('/api/v1/shop/geo/search', query);
  return data ?? [];
}

async function reversePlace(scope: 'admin' | 'shop', point: LatLng, lang: string): Promise<Place | null> {
  const query = { params: { query: { lat: point.lat, lng: point.lng, lang } } };
  const { data } =
    scope === 'admin'
      ? await browserApi.GET('/api/v1/admin/geo/reverse', query)
      : await browserApi.GET('/api/v1/shop/geo/reverse', query);
  return data ?? null;
}

const toAddress = (place: Place): Address => ({
  addressLine: place.addressLine,
  district: place.district,
  city: place.city,
  formattedAddress: place.formattedAddress,
});

/**
 * Shop location picker (spec §8, DV-A02), in the TRIMME visual language: address search, "use my current location",
 * a map with a draggable pin, the resolved address with its coordinates, and confirm. The typed coordinate and
 * address fields are the keyboard and no-map alternative. Geolocation is requested only when the user asks.
 */
export function LocationPicker({ initial, scope, onSave, MapComponent = MapLibreView }: LocationPickerProps) {
  const t = useTranslations('locationPicker');
  const tv = useTranslations('validation');
  const apiMessage = useApiErrorMessage();
  const lang = useLocale();
  const [point, setPoint] = useState<LatLng | null>(
    initial ? { lat: initial.latitude, lng: initial.longitude } : null,
  );
  const [source, setSource] = useState<LocationSource>(initial?.source ?? 'Manual');
  const [address, setAddress] = useState<Address>(initial ?? EMPTY_ADDRESS);
  const [latText, setLatText] = useState(initial ? String(initial.latitude) : '');
  const [lngText, setLngText] = useState(initial ? String(initial.longitude) : '');
  const [coordinateError, setCoordinateError] = useState(false);
  const [resolving, setResolving] = useState(false);
  const [unresolved, setUnresolved] = useState(false);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<Place[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [geo, setGeo] = useState<'idle' | 'locating' | 'denied' | 'unavailable'>('idle');
  const [mapUnavailable, setMapUnavailable] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<string>();
  const reverseRequest = useRef(0);

  const moveTo = useCallback((next: LatLng, nextSource: LocationSource) => {
    const rounded = { lat: roundCoordinate(next.lat), lng: roundCoordinate(next.lng) };
    setPoint(rounded);
    setSource(nextSource);
    setLatText(String(rounded.lat));
    setLngText(String(rounded.lng));
    setCoordinateError(false);
    setSaved(false);
    return rounded;
  }, []);

  const resolve = useCallback(
    async (target: LatLng) => {
      const request = ++reverseRequest.current;
      setResolving(true);
      setUnresolved(false);
      try {
        const place = await reversePlace(scope, target, lang);
        if (request !== reverseRequest.current) return;
        if (place) setAddress(toAddress(place));
        else setUnresolved(true);
      } catch {
        if (request === reverseRequest.current) setUnresolved(true);
      } finally {
        if (request === reverseRequest.current) setResolving(false);
      }
    },
    [scope, lang],
  );

  const onPinChange = useCallback((next: LatLng) => void resolve(moveTo(next, 'Manual')), [moveTo, resolve]);

  const onSearch = async (event: FormEvent) => {
    event.preventDefault();
    if (query.trim().length < 2) return;
    setSearching(true);
    try {
      setResults(await searchPlaces(scope, query.trim(), lang));
    } catch {
      setResults([]);
    } finally {
      setSearching(false);
    }
  };

  const choose = (place: Place) => {
    moveTo({ lat: place.latitude, lng: place.longitude }, 'Geocoded');
    setAddress(toAddress(place));
    setUnresolved(false);
    setResults(null);
  };

  const useMyLocation = () => {
    if (!('geolocation' in navigator)) {
      setGeo('unavailable');
      return;
    }
    setGeo('locating');
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setGeo('idle');
        void resolve(moveTo({ lat: position.coords.latitude, lng: position.coords.longitude }, 'Device'));
      },
      (error) => setGeo(error.code === error.PERMISSION_DENIED ? 'denied' : 'unavailable'),
      { enableHighAccuracy: true, timeout: 10_000 },
    );
  };

  const applyTypedCoordinates = () => {
    const lat = Number(latText);
    const lng = Number(lngText);
    if (latText.trim() === '' || lngText.trim() === '' || !isValidLatLng(lat, lng)) {
      setCoordinateError(latText.trim() !== '' || lngText.trim() !== '');
      return;
    }
    if (point && point.lat === roundCoordinate(lat) && point.lng === roundCoordinate(lng)) return;
    void resolve(moveTo({ lat, lng }, 'Manual'));
  };

  const confirm = async () => {
    if (!point) {
      setCoordinateError(true);
      return;
    }
    setSaving(true);
    setFailure(undefined);
    try {
      await onSave({ latitude: point.lat, longitude: point.lng, source, ...trimAddress(address) });
      setSaved(true);
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    } finally {
      setSaving(false);
    }
  };

  const moved =
    initial !== null && point !== null && (point.lat !== initial.latitude || point.lng !== initial.longitude);
  const setField = (field: keyof Address) => (value: string) => {
    setSaved(false);
    setAddress((current) => ({ ...current, [field]: value }));
  };

  return (
    <div className="flex flex-col gap-4" data-testid="location-picker">
      <div className="flex flex-col gap-3 md:flex-row md:items-end">
        <form role="search" onSubmit={onSearch} className="flex min-w-0 flex-1 items-end gap-2">
          <TextField
            label={t('search')}
            hideLabel
            placeholder={t('search')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            className="min-w-0 flex-1"
            autoComplete="off"
          />
          <Button type="submit" variant="secondary" size="md" icon="search" loading={searching}>
            {t('searchSubmit')}
          </Button>
        </form>
        <Button
          type="button"
          variant="outline"
          size="md"
          icon="pin"
          loading={geo === 'locating'}
          onClick={useMyLocation}
        >
          {t('useMyLocation')}
        </Button>
      </div>

      {results && (
        <div aria-live="polite">
          {results.length === 0 ? (
            <p className="text-caption text-text-secondary">{t('noResults')}</p>
          ) : (
            <ul
              aria-label={t('results')}
              className="flex flex-col divide-y divide-border-row rounded-card border border-border bg-surface"
            >
              {results.map((place) => (
                <li key={`${place.latitude},${place.longitude}`}>
                  <button
                    type="button"
                    onClick={() => choose(place)}
                    className="flex min-h-11 w-full items-center gap-2 px-4 py-2 text-start text-caption text-text-primary hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
                  >
                    <Icon name="pin" className="size-4 shrink-0 text-brand-500" />
                    <span>{place.formattedAddress}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {geo === 'denied' && <InlineAlert tone="warning" title={t('locationDenied')} />}
      {geo === 'unavailable' && <InlineAlert tone="warning" title={t('locationUnavailable')} />}

      {mapUnavailable ? (
        <InlineAlert tone="info" title={t('mapUnavailable')} />
      ) : (
        <div className="relative">
          <MapComponent
            center={point ?? DEFAULT_CENTER}
            pin={point}
            onPinChange={onPinChange}
            label={t('mapLabel')}
            pinLabel={t('pin')}
            attribution={t('attribution')}
            onUnavailable={() => setMapUnavailable(true)}
          />
          <span className="pointer-events-none absolute start-1/2 top-3 -translate-x-1/2 rounded-pill bg-surface/95 px-3 py-1.5 text-helper font-bold text-navy-900 shadow-e1 rtl:translate-x-1/2">
            {t('dragHint')}
          </span>
        </div>
      )}

      <div className="flex items-start gap-3 rounded-card bg-bg-subtle p-4" aria-live="polite">
        <Icon name="pin" className="mt-0.5 size-5 shrink-0 text-brand-500" />
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-helper text-text-tertiary">{t('resolved')}</span>
          <span className="text-caption font-bold text-text-primary" data-testid="resolved-address">
            {resolving
              ? t('resolving')
              : unresolved && !address.formattedAddress
                ? t('unresolved')
                : address.formattedAddress || (point ? t('unresolved') : t('notSet'))}
          </span>
          {point && (
            <span className="text-helper text-text-primary">
              {t('coordinates')}:{' '}
              <span dir="ltr" className="font-latin" data-testid="coordinates">
                {formatCoordinates(point)}
              </span>
            </span>
          )}
        </div>
      </div>

      <fieldset className="flex min-w-0 flex-col gap-3">
        <legend className="pb-2 text-label font-bold text-text-strong">{t('manual')}</legend>
        <div className="grid gap-3 sm:grid-cols-2">
          <TextField
            label={t('latitude')}
            inputMode="decimal"
            dir="ltr"
            value={latText}
            onChange={(event) => setLatText(event.target.value)}
            onBlur={applyTypedCoordinates}
            error={coordinateError ? tv('coordinateInvalid') : undefined}
            name="latitude"
          />
          <TextField
            label={t('longitude')}
            inputMode="decimal"
            dir="ltr"
            value={lngText}
            onChange={(event) => setLngText(event.target.value)}
            onBlur={applyTypedCoordinates}
            error={coordinateError ? tv('coordinateInvalid') : undefined}
            name="longitude"
          />
          <TextField
            label={t('addressLine')}
            value={address.addressLine ?? ''}
            onChange={(event) => setField('addressLine')(event.target.value)}
            maxLength={200}
          />
          <TextField
            label={t('district')}
            value={address.district ?? ''}
            onChange={(event) => setField('district')(event.target.value)}
            maxLength={80}
          />
          <TextField
            label={t('city')}
            value={address.city ?? ''}
            onChange={(event) => setField('city')(event.target.value)}
            maxLength={80}
          />
          <TextField
            label={t('formattedAddress')}
            value={address.formattedAddress ?? ''}
            onChange={(event) => setField('formattedAddress')(event.target.value)}
            maxLength={300}
          />
        </div>
      </fieldset>

      {moved && <InlineAlert tone="warning" title={t('changeWarning')} />}
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}

      <Button
        type="button"
        icon="check"
        loading={saving}
        onClick={() => void confirm()}
        className="self-start"
      >
        {t('confirm')}
      </Button>
    </div>
  );
}

function trimAddress(address: Address): Address {
  const clean = (value: string | null) => (value && value.trim() ? value.trim() : null);
  return {
    addressLine: clean(address.addressLine),
    district: clean(address.district),
    city: clean(address.city),
    formattedAddress: clean(address.formattedAddress),
  };
}
