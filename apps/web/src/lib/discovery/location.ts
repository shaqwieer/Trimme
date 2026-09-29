/**
 * The customer's chosen location for discovery (R-CUS-02, D-095). It lives only in a first-party cookie on the
 * customer's device, so server-rendered discovery pages can read it; the server never stores it. Coordinates are rounded
 * to three decimals (about 100 m) before they are kept or sent, which is enough for "nearest first" and reveals less.
 */
export const LOCATION_COOKIE = 'trimme-location';

/** How long the choice is remembered. */
export const LOCATION_MAX_AGE_SECONDS = 60 * 60 * 24 * 30;

export type LocationSource = 'device' | 'area';

export type ChosenLocation = {
  lat: number;
  lng: number;
  /** What the customer picked: a district name, or empty for "my current location". */
  label: string;
  source: LocationSource;
};

export function roundForPrivacy(value: number): number {
  return Math.round(value * 1000) / 1000;
}

export function serializeLocation(location: ChosenLocation): string {
  return encodeURIComponent(
    JSON.stringify({
      lat: roundForPrivacy(location.lat),
      lng: roundForPrivacy(location.lng),
      label: location.label.slice(0, 80),
      source: location.source,
    }),
  );
}

/** Parses the cookie value; anything malformed or out of range is ignored. */
export function parseLocation(raw: string | undefined | null): ChosenLocation | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(decodeURIComponent(raw)) as Partial<ChosenLocation>;
    const { lat, lng, label, source } = value;
    if (typeof lat !== 'number' || typeof lng !== 'number' || !Number.isFinite(lat) || !Number.isFinite(lng))
      return null;
    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;
    return {
      lat: roundForPrivacy(lat),
      lng: roundForPrivacy(lng),
      label: typeof label === 'string' ? label.slice(0, 80) : '',
      source: source === 'device' ? 'device' : 'area',
    };
  } catch {
    return null;
  }
}

/** Great-circle distance in kilometres (for the shop page, computed in the browser only). */
export function distanceKm(from: { lat: number; lng: number }, to: { lat: number; lng: number }): number {
  const radians = (degrees: number) => (degrees * Math.PI) / 180;
  const dLat = radians(to.lat - from.lat);
  const dLng = radians(to.lng - from.lng);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(radians(from.lat)) * Math.cos(radians(to.lat)) * Math.sin(dLng / 2) ** 2;
  return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}
