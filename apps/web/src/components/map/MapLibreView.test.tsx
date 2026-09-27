import { render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MapLibreView } from './MapLibreView';

/** A stand-in for MapLibre that records where markers are placed. */
const placed = vi.hoisted(() => ({ markers: [] as Array<[number, number]> }));
vi.mock('maplibre-gl', () => {
  class Map {
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

beforeEach(() => {
  placed.markers = [];
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({} as RenderingContext);
});

afterEach(() => vi.restoreAllMocks());

describe('MapLibreView', () => {
  it('places a pin chosen while the map library is still loading (Phase 06 race)', async () => {
    const { rerender } = render(<MapLibreView {...common} pin={null} />);
    // The search result arrives before the lazily imported library has created the map.
    rerender(<MapLibreView {...common} pin={{ lat: 24.8123, lng: 46.6011 }} />);
    await waitFor(() => expect(placed.markers).toContainEqual([46.6011, 24.8123]));
  });

  it('places the starting pin', async () => {
    render(<MapLibreView {...common} pin={{ lat: 24.7, lng: 46.6 }} />);
    await waitFor(() => expect(placed.markers).toContainEqual([46.6, 24.7]));
  });
});
