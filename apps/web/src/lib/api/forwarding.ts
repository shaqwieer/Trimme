/**
 * The visitor's address as the nearest trusted proxy saw it (D-094): the **last** entry of `X-Forwarded-For` (the one
 * Nginx appended), else `X-Real-IP`. Earlier entries are client-supplied and never trusted. The API accepts the header
 * only from configured proxy addresses (`ReverseProxy:KnownProxies/KnownNetworks`).
 */
export function clientIp(headers: Pick<Headers, 'get'>): string | undefined {
  const forwarded = headers.get('x-forwarded-for');
  if (forwarded) {
    const hops = forwarded
      .split(',')
      .map((hop) => hop.trim())
      .filter(Boolean);
    const last = hops.at(-1);
    if (last && isAddress(last)) return last;
  }
  const real = headers.get('x-real-ip')?.trim();
  return real && isAddress(real) ? real : undefined;
}

/** A plain IPv4 or IPv6 address (no ports, no names), so nothing else can be smuggled into the header. */
function isAddress(value: string): boolean {
  return /^(?:\d{1,3}\.){3}\d{1,3}$/.test(value) || /^[0-9a-fA-F:]{2,39}$/.test(value);
}
