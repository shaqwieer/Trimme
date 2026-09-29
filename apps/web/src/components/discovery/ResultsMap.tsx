'use client';

import 'maplibre-gl/dist/maplibre-gl.css';
import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { Map as MapLibreMap, Marker } from 'maplibre-gl';
import { useLocale, useTranslations } from 'next-intl';
import { ButtonLink } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { RatingStars } from '@/components/ui/Rating';
import { InlineAlert } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import type { ShopSearchItem } from '@/lib/api/public-types';
import { isTomorrow } from '@/lib/discovery/opening';
import { type AppLocale, formatDistanceKm, formatPrice, formatTime } from '@/lib/i18n/format';
import { DEFAULT_CENTER, DEFAULT_ZOOM, TILE_URL } from '@/lib/map/config';

/** Probed once per page load: each probe creates a WebGL context, and browsers keep only a few alive. */
let webGlSupport: boolean | undefined;

function webGlAvailable(): boolean {
  if (webGlSupport === undefined) {
    try {
      const canvas = document.createElement('canvas');
      const context = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
      webGlSupport = Boolean(context);
      context?.getExtension('WEBGL_lose_context')?.loseContext();
    } catch {
      webGlSupport = false;
    }
  }
  return webGlSupport;
}

const noSubscription = () => () => undefined;
const assumeWebGl = () => true;

type PinRegistry = Map<string, { marker: Marker; element: HTMLButtonElement }>;

/** Marks the selected pin (design: navy fill), keeps it on top and exposes the state to assistive technology. */
function highlightPins(registry: PinRegistry, selectedId: string | null) {
  for (const [id, { element }] of registry) {
    const selected = id === selectedId;
    element.className = `${PIN_CLASS} ${selected ? PIN_SELECTED : PIN_IDLE}`;
    element.setAttribute('aria-pressed', String(selected));
    element.style.zIndex = selected ? '2' : '1';
  }
}

const PIN_CLASS =
  'rounded-pill border px-2.5 py-1 font-latin text-[0.8125rem] font-bold whitespace-nowrap shadow-e2 outline-offset-2 focus-visible:outline-2 focus-visible:outline-brand-500';
const PIN_IDLE = 'border-border bg-surface text-navy-900';
const PIN_SELECTED = 'border-navy-900 bg-navy-900 text-on-navy';

/**
 * Discovery map (c-map "MAP VIEW" 1126–1164): one price pin per result (the matched service's price, else the shop's
 * lowest, mapRules #1), the customer's location dot, and a bottom card for the selected shop. Pins are buttons, so the
 * map works with the keyboard; the list view is always one tap away as the accessible alternative.
 */
export function ResultsMap({
  items,
  origin,
}: {
  items: ShopSearchItem[];
  origin: { lat: number; lng: number } | null;
}) {
  const t = useTranslations('search');
  const tPicker = useTranslations('locationPicker');
  const locale = useLocale() as AppLocale;
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const markers = useRef<PinRegistry>(new Map());
  const [selectedId, setSelectedId] = useState<string | null>(items[0]?.id ?? null);
  // WebGL is known only in the browser; the server render assumes it and the map fails over after hydration if not.
  const webGl = useSyncExternalStore(noSubscription, webGlAvailable, assumeWebGl);
  const [failed, setFailed] = useState(false);
  const unavailable = !webGl || failed;
  const initial = useRef({ items, origin, t, tPicker, locale });

  useEffect(() => {
    const { items: shops, origin: from, t: messages, tPicker: picker, locale: lang } = initial.current;
    const registry = markers.current;
    let cancelled = false;
    if (!webGl) return;

    void import('maplibre-gl')
      .then(({ Map, Marker, AttributionControl, NavigationControl, LngLatBounds }) => {
        if (cancelled || !container.current) return;
        const center =
          from ?? (shops[0] ? { lat: shops[0].latitude, lng: shops[0].longitude } : DEFAULT_CENTER);
        const map = new Map({
          container: container.current,
          style: {
            version: 8,
            sources: {
              osm: {
                type: 'raster',
                tiles: [TILE_URL],
                tileSize: 256,
                maxzoom: 19,
                attribution: picker('attribution'),
              },
            },
            layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
          },
          center: [center.lng, center.lat],
          zoom: DEFAULT_ZOOM,
          attributionControl: false,
          dragRotate: false,
          pitchWithRotate: false,
        });
        map.addControl(new AttributionControl({ compact: false }), 'bottom-left');
        map.addControl(new NavigationControl({ showCompass: false }), 'top-left');
        map.touchZoomRotate.disableRotation();

        const bounds = new LngLatBounds();
        for (const shop of shops) {
          const element = document.createElement('button');
          element.type = 'button';
          element.className = `${PIN_CLASS} ${PIN_IDLE}`;
          element.textContent = formatPrice(shop.pinPrice, lang, shop.currency);
          element.setAttribute(
            'aria-label',
            messages('map.pin', {
              shop: lang === 'en' ? shop.nameEn : shop.nameAr,
              price: formatPrice(shop.pinPrice, lang, shop.currency),
            }),
          );
          element.dataset.testid = 'price-pin';
          element.addEventListener('click', (event) => {
            event.stopPropagation();
            setSelectedId(shop.id);
          });
          const marker = new Marker({ element, anchor: 'bottom' })
            .setLngLat([shop.longitude, shop.latitude])
            .addTo(map);
          registry.set(shop.id, { marker, element });
          bounds.extend([shop.longitude, shop.latitude]);
        }

        if (from) {
          const dot = document.createElement('div');
          dot.className =
            'size-4 rounded-full border-[3px] border-surface bg-brand-600 shadow-[0_0_0_6px_rgb(74_127_181/.25)]';
          dot.setAttribute('role', 'img');
          dot.setAttribute('aria-label', messages('map.you'));
          new Marker({ element: dot }).setLngLat([from.lng, from.lat]).addTo(map);
          bounds.extend([from.lng, from.lat]);
        }

        if (!bounds.isEmpty()) {
          // Keep every pin above the selected-shop card at the bottom of the map.
          map.fitBounds(bounds, {
            padding: { top: 64, left: 56, right: 56, bottom: 176 },
            maxZoom: 15,
            duration: 0,
          });
        }
        mapRef.current = map;
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      for (const { marker } of registry.values()) marker.remove();
      registry.clear();
      mapRef.current?.remove();
      mapRef.current = null;
    };
  }, [webGl]);

  useEffect(() => {
    highlightPins(markers.current, selectedId);
  }, [selectedId]);

  const selected = items.find((item) => item.id === selectedId) ?? null;
  const name = selected ? (locale === 'en' ? selected.nameEn : selected.nameAr) : '';

  if (unavailable) {
    return <InlineAlert tone="info" title={tPicker('mapUnavailable')} />;
  }

  return (
    <div className="relative">
      <div
        ref={container}
        role="region"
        aria-label={t('map.label')}
        data-testid="results-map"
        className="h-[62dvh] min-h-[360px] w-full overflow-hidden rounded-card border border-border bg-bg-muted"
      />
      {selected && (
        <section
          aria-label={t('map.selected')}
          className="absolute inset-x-3 bottom-3 z-10 flex items-center gap-3 rounded-card border border-border bg-surface p-3 shadow-e3"
        >
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <h3 className="truncate text-label font-bold text-text-primary">
              <Link href={`/shops/${selected.slug}`} className="hover:underline">
                {name}
              </Link>
            </h3>
            <p className="flex flex-wrap items-center gap-2 text-helper text-text-secondary">
              {selected.reviewCount > 0 && (
                <RatingStars value={selected.rating} count={selected.reviewCount} />
              )}
              {selected.distanceKm != null && (
                <span className="font-latin font-semibold">
                  {formatDistanceKm(selected.distanceKm, locale)}
                </span>
              )}
            </p>
            {selected.earliestSlotAt && (
              <span className="inline-flex w-fit items-center gap-1 rounded-badge bg-status-completed-bg px-2 py-0.5 text-badge font-bold text-status-completed-fg">
                <Icon name="clock" className="size-3.5" />
                {isTomorrow(selected.earliestSlotAt, selected.timeZone)
                  ? t('tomorrow', { time: formatTime(selected.earliestSlotAt, locale, selected.timeZone) })
                  : t('earliest', { time: formatTime(selected.earliestSlotAt, locale, selected.timeZone) })}
              </span>
            )}
          </div>
          <ButtonLink href={`/shops/${selected.slug}`} variant="primary" size="sm" className="shrink-0">
            {t('map.open')}
          </ButtonLink>
        </section>
      )}
    </div>
  );
}
