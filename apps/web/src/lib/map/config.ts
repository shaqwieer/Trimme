/** A WGS 84 point. The API names these `latitude` / `longitude`; PostGIS stores X = longitude, Y = latitude. */
export type LatLng = { lat: number; lng: number };

/** Map defaults for Riyadh, the demo operating city (spec §6). Platform settings take these over in Phase 08/14. */
export const DEFAULT_CENTER: LatLng = { lat: 24.7136, lng: 46.6753 };
export const DEFAULT_ZOOM = 11;
export const PIN_ZOOM = 16;

/**
 * Raster tile template (D-007). OpenStreetMap's public tiles are for light development use only and need
 * attribution; production points NEXT_PUBLIC_MAP_TILE_URL at a self-hosted or commercial OSM-based host.
 */
export const TILE_URL =
  process.env.NEXT_PUBLIC_MAP_TILE_URL || 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

export function isValidLatLng(lat: number, lng: number): boolean {
  return Number.isFinite(lat) && Number.isFinite(lng) && Math.abs(lat) <= 90 && Math.abs(lng) <= 180;
}

/** Six decimal places (about 11 cm), the precision the API stores. */
export function roundCoordinate(value: number): number {
  return Math.round(value * 1e6) / 1e6;
}

/** "24.812300, 46.601100" — always Latin digits and left-to-right (render inside an LTR span). */
export function formatCoordinates(point: LatLng): string {
  return `${point.lat.toFixed(6)}, ${point.lng.toFixed(6)}`;
}
