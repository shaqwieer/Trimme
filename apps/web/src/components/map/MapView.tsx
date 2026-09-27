import type { ComponentType } from 'react';
import type { LatLng } from '@/lib/map/config';

/**
 * The map provider contract (D-007). The location picker depends only on this, so the provider (MapLibre with OSM
 * tiles in development) can be swapped by configuration, and tests can render a fake map.
 */
export type MapViewProps = {
  /** Where the map opens when there is no pin yet. */
  center: LatLng;
  /** The draggable pin, or none yet. The map recentres when it changes from outside (search, device location). */
  pin: LatLng | null;
  /** Called when the user drags the pin or clicks the map. */
  onPinChange: (point: LatLng) => void;
  /** Accessible name of the map region. */
  label: string;
  /** Accessible name of the pin. */
  pinLabel: string;
  /** Text of the attribution the tile provider requires. */
  attribution: string;
  /** Called once if the map cannot render here (for example no WebGL); the picker then falls back to typed fields. */
  onUnavailable: () => void;
  /** Called once the map is interactive. */
  onReady?: () => void;
};

export type MapViewComponent = ComponentType<MapViewProps>;
