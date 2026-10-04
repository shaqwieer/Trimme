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

/** OpenStreetMap directions to the shop (D-007: OSM everywhere; opens the visitor's route in a new tab). */
export function directionsUrl(latitude: number, longitude: number): string {
  return `https://www.openstreetmap.org/directions?to=${latitude.toFixed(6)}%2C${longitude.toFixed(6)}`;
}
