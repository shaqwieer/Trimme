'use client';

import 'maplibre-gl/dist/maplibre-gl.css';
import { useTranslations } from 'next-intl';
import { useEffect, useRef } from 'react';
import type { Map as MapLibreMap, Marker } from 'maplibre-gl';
import { DEFAULT_ZOOM, mapLibreLocale, PIN_ZOOM, TILE_URL } from '@/lib/map/config';
import type { MapViewProps } from './MapView';

const PIN_SVG =
  '<svg viewBox="0 0 24 24" width="40" height="40" aria-hidden="true"><path class="stroke-surface" fill="currentColor" stroke-width="1.5" d="M12 22s7-6.2 7-12a7 7 0 1 0-14 0c0 5.8 7 12 7 12Z"/><circle class="fill-surface" cx="12" cy="10" r="2.6"/></svg>';

function webGlAvailable(): boolean {
  try {
    const canvas = document.createElement('canvas');
    return Boolean(canvas.getContext('webgl2') ?? canvas.getContext('webgl'));
  } catch {
    return false;
  }
}

/**
 * MapLibre GL adapter with OpenStreetMap raster tiles (D-007). Loaded only in the browser and only when the picker
 * mounts, so pages without a map never download it. The pin is a DOM marker: draggable with the mouse or touch, and
 * the picker's typed coordinate fields are its keyboard alternative.
 */
export function MapLibreView({
  center,
  pin,
  onPinChange,
  label,
  pinLabel,
  attribution,
  onUnavailable,
  onReady,
}: MapViewProps) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const markerRef = useRef<Marker | null>(null);
  const onPinChangeRef = useRef(onPinChange);
  // The latest pin: one chosen while MapLibre is still loading must be used when the map is created.
  const pinRef = useRef(pin);
  const tPicker = useTranslations('locationPicker');
  const initial = useRef({ center, label, pinLabel, attribution, onUnavailable, onReady, tPicker });

  useEffect(() => {
    onPinChangeRef.current = onPinChange;
  }, [onPinChange]);

  useEffect(() => {
    pinRef.current = pin;
  }, [pin]);

  useEffect(() => {
    const {
      center: start,
      label: mapLabel,
      tPicker: picker,
      pinLabel: markerLabel,
      attribution: credit,
      onUnavailable: fail,
      onReady: ready,
    } = initial.current;
    let cancelled = false;

    if (!webGlAvailable()) {
      fail();
      return;
    }

    void import('maplibre-gl')
      .then(({ Map, Marker, AttributionControl, NavigationControl }) => {
        if (cancelled || !container.current) return;
        const startPin = pinRef.current;
        const origin = startPin ?? start;
        const map = new Map({
          container: container.current,
          style: {
            version: 8,
            sources: {
              osm: { type: 'raster', tiles: [TILE_URL], tileSize: 256, maxzoom: 19, attribution: credit },
            },
            layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
          },
          center: [origin.lng, origin.lat],
          zoom: startPin ? PIN_ZOOM : DEFAULT_ZOOM,
          locale: mapLibreLocale(picker, mapLabel),
          attributionControl: false,
          dragRotate: false,
          pitchWithRotate: false,
        });
        // Top: the selected-shop card and the picker's hint sit at the bottom and must never hide the map credits.
        map.addControl(new AttributionControl({ compact: false }), 'top-right');
        map.addControl(new NavigationControl({ showCompass: false }), 'top-left');
        map.touchZoomRotate.disableRotation();

        const element = document.createElement('div');
        element.className = 'cursor-grab text-navy-900 drop-shadow-md active:cursor-grabbing';
        element.innerHTML = PIN_SVG;
        element.setAttribute('role', 'img');
        element.setAttribute('aria-label', markerLabel);
        element.dataset.testid = 'map-pin';

        const marker = new Marker({ element, draggable: true, anchor: 'bottom' });
        if (startPin) marker.setLngLat([startPin.lng, startPin.lat]).addTo(map);
        marker.on('dragend', () => {
          const { lng, lat } = marker.getLngLat();
          onPinChangeRef.current({ lat, lng });
        });
        map.on('click', (event) => {
          marker.setLngLat(event.lngLat).addTo(map);
          onPinChangeRef.current({ lat: event.lngLat.lat, lng: event.lngLat.lng });
        });
        map.on('load', () => ready?.());

        mapRef.current = map;
        markerRef.current = marker;
      })
      .catch(() => {
        if (!cancelled) fail();
      });

    return () => {
      cancelled = true;
      markerRef.current?.remove();
      mapRef.current?.remove();
      mapRef.current = null;
      markerRef.current = null;
    };
  }, []);

  // A pin set from outside (search result, device position, typed coordinates) moves the marker and the view.
  useEffect(() => {
    const map = mapRef.current;
    const marker = markerRef.current;
    if (!map || !marker || !pin) return;
    const current = marker.getLngLat?.();
    if (current && Math.abs(current.lat - pin.lat) < 1e-7 && Math.abs(current.lng - pin.lng) < 1e-7) return;
    marker.setLngLat([pin.lng, pin.lat]).addTo(map);
    map.easeTo({ center: [pin.lng, pin.lat], zoom: Math.max(map.getZoom(), PIN_ZOOM) });
  }, [pin]);

  return (
    <div
      ref={container}
      role="region"
      aria-label={label}
      data-testid="location-map"
      className="h-[60vh] max-h-[420px] min-h-[280px] w-full overflow-hidden rounded-card border border-border bg-bg-muted md:aspect-video md:h-auto"
    />
  );
}
