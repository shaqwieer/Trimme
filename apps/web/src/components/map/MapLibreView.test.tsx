import ar from '@messages/ar.json';
import { render, waitFor } from '@testing-library/react';
import { NextIntlClientProvider } from 'next-intl';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MapLibreView } from './MapLibreView';

/** A stand-in for MapLibre that records where markers are placed. */
const placed = vi.hoisted(() => ({
  markers: [] as Array<[number, number]>,
  options: [] as Array<Record<string, unknown>>,
}));
vi.mock('maplibre-gl', () => {
  class Map {
    constructor(options: Record<string, unknown>) {
      placed.options.push(options);
    }
    on() {}
    addControl() {}
    touchZoomRotate = { disableRotation() {} };
    getZoom() {
      return 11;
    }
    easeTo() {}
    remove() {}
  }
  class Marker {
    private at: [number, number] | undefined;
    setLngLat(at: [number, number]) {
      this.at = at;
      return this;
    }
    getLngLat() {
      return this.at ? { lng: this.at[0], lat: this.at[1] } : undefined;
    }
    addTo() {
      if (this.at) placed.markers.push(this.at);
      return this;
    }
    on() {}
    remove() {}
  }
  class Control {}
  return { Map, Marker, AttributionControl: Control, NavigationControl: Control };
});

const common = {
  center: { lat: 24.7136, lng: 46.6753 },
  onPinChange: () => {},
  label: 'Map',
  pinLabel: 'Pin',
  attribution: 'OSM',
  onUnavailable: () => {},
};

const wrapper = ({ children }: { children: ReactNode }) => (
  <NextIntlClientProvider locale="ar" messages={ar} timeZone="Asia/Riyadh">
    {children}
  </NextIntlClientProvider>
);

beforeEach(() => {
  placed.markers = [];
  placed.options = [];
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({} as RenderingContext);
});

afterEach(() => vi.restoreAllMocks());

describe('MapLibreView', () => {
  it('places a pin chosen while the map library is still loading (Phase 06 race)', async () => {
    const { rerender } = render(<MapLibreView {...common} pin={null} />, { wrapper });
    // The search result arrives before the lazily imported library has created the map.
    rerender(<MapLibreView {...common} pin={{ lat: 24.8123, lng: 46.6011 }} />);
    await waitFor(() => expect(placed.markers).toContainEqual([46.6011, 24.8123]));
  });

  it('places the starting pin', async () => {
    render(<MapLibreView {...common} pin={{ lat: 24.7, lng: 46.6 }} />, { wrapper });
    await waitFor(() => expect(placed.markers).toContainEqual([46.6, 24.7]));
  });

  it("speaks the page's language: MapLibre's own labels come from the catalogue (Phase 17)", async () => {
    render(<MapLibreView {...common} label="خريطة المحل" pin={null} />, { wrapper });
    await waitFor(() => expect(placed.options).toHaveLength(1));
    expect(placed.options[0]!.locale).toEqual({
      'Map.Title': 'خريطة المحل',
      'NavigationControl.ZoomIn': ar.locationPicker.zoomIn,
      'NavigationControl.ZoomOut': ar.locationPicker.zoomOut,
      'AttributionControl.ToggleAttribution': ar.locationPicker.toggleAttribution,
    });
  });
});
