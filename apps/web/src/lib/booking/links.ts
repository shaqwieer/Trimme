/** Links shared by public pages, the wizard and the account pages (no server-only imports). */

/** A booking link for the wizard (Phase 12, D-028): the step state lives in the URL; a list repeats its key. */
export function bookHref(slug: string, query: Record<string, string | string[] | undefined> = {}): string {
  const params = new URLSearchParams(
    Object.entries(query).flatMap(([key, value]) =>
      [value ?? []]
        .flat()
        .filter(Boolean)
        .map((item): [string, string] => [key, item]),
    ),
  );
  const text = params.toString();
  return text ? `/shops/${slug}/book?${text}` : `/shops/${slug}/book`;
}

/**
 * Google Maps directions to the shop (D-144, replacing D-007's OpenStreetMap link for directions only): with no origin,
 * Google Maps starts from the visitor's own location and shows the route's time and distance; on a phone it opens the
 * Google Maps app. The maps on our pages stay OpenStreetMap.
 */
export function directionsUrl(latitude: number, longitude: number): string {
  return `https://www.google.com/maps/dir/?api=1&destination=${latitude.toFixed(6)}%2C${longitude.toFixed(6)}`;
}
