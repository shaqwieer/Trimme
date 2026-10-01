'use client';

import 'maplibre-gl/dist/maplibre-gl.css';
import { useEffect, useRef, useState } from 'react';
import type { Map as MapLibreMap } from 'maplibre-gl';
import { useTranslations } from 'next-intl';
import { TILE_URL } from '@/lib/map/config';

const PIN_SVG =
  '<svg viewBox="0 0 24 24" width="36" height="36" aria-hidden="true"><path class="stroke-surface" fill="currentColor" stroke-width="1.5" d="M12 22s7-6.2 7-12a7 7 0 1 0-14 0c0 5.8 7 12 7 12Z"/><circle class="fill-surface" cx="12" cy="10" r="2.6"/></svg>';

/**
 * The shop's small map (DV-A23): a fixed view of the entrance pin. MapLibre is downloaded only once the map scrolls into
 * view (the About tab), so the shop page stays light. Without WebGL it simply stays a placeholder; the address and the
 * directions link carry the same information.
 */
export function ShopMiniMap({ latitude, longitude, label }: { latitude: number; longitude: number; label: string }) {
  const t = useTranslations('locationPicker');
  const container = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        setVisible(true);
        observer.disconnect();
      }
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!visible || !container.current) return;
    let map: MapLibreMap | null = null;
    let cancelled = false;
    const canvas = document.createElement('canvas');
    const probe = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
    if (!probe) return;
    probe.getExtension('WEBGL_lose_context')?.loseContext();
    const credit = t('attribution');
    void import('maplibre-gl')
      .then(({ Map, Marker, AttributionControl }) => {
        if (cancelled || !container.current) return;
        map = new Map({
          container: container.current,
          style: {
            version: 8,
            sources: { osm: { type: 'raster', tiles: [TILE_URL], tileSize: 256, maxzoom: 19, attribution: credit } },
            layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
          },
          center: [longitude, latitude],
          zoom: 15,
          interactive: false,
          attributionControl: false,
        });
        map.addControl(new AttributionControl({ compact: true }), 'bottom-left');
        const pin = document.createElement('div');
        pin.className = 'text-chrome drop-shadow-md';
        pin.innerHTML = PIN_SVG;
        new Marker({ element: pin, anchor: 'bottom' }).setLngLat([longitude, latitude]).addTo(map);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
      map?.remove();
    };
  }, [visible, latitude, longitude, t]);

  return (
    <div
      ref={container}
      role="img"
      aria-label={label}
      data-testid="shop-mini-map"
      className="h-[180px] w-full overflow-hidden rounded-card border border-border bg-bg-muted"
    />
  );
}
