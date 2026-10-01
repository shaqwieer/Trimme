/**
 * Content Security Policy for every page (D-117). Scripts need the per-request nonce: Next.js reads it from the
 * request's CSP header and stamps it on its own scripts, and `'strict-dynamic'` extends trust to the chunks those
 * scripts load (MapLibre included). Styles allow inline: React renders `style` attributes, Radix's scroll lock injects
 * a `<style>` element at runtime and MapLibre styles its markers, none of which can carry a nonce. A style injection
 * cannot run code, so the protection that matters (scripts) stays strict.
 */
export type CspOptions = {
  nonce: string;
  /** `next dev` needs `eval` for React's server error overlays; production never does. */
  development: boolean;
  /** The map tile template (D-007); its origin is allowed for images and fetches. */
  tileUrl: string;
  /** Upgrade subresource requests only when the site itself is served over HTTPS. */
  https: boolean;
};

/** The origin of a tile template such as `https://{s}.tile.example.org/{z}/{x}/{y}.png`; `{s}` becomes a wildcard. */
export function tileOrigin(template: string): string | null {
  const match = /^(https?):\/\/([^/?#]+)/i.exec(template.trim());
  if (!match) {
    return null;
  }
  const host = match[2]!.replace(/\{[a-z]+\}/gi, '*');
  return /^[a-z0-9*.:-]+$/i.test(host) ? `${match[1]!.toLowerCase()}://${host.toLowerCase()}` : null;
}

export function buildCsp({ nonce, development, tileUrl, https }: CspOptions): string {
  const tiles = tileOrigin(tileUrl);
  const directives: Array<[string, ...string[]]> = [
    ['default-src', "'self'"],
    [
      'script-src',
      "'self'",
      `'nonce-${nonce}'`,
      "'strict-dynamic'",
      ...(development ? ["'unsafe-eval'"] : []),
    ],
    ['style-src', "'self'", "'unsafe-inline'"],
    ['img-src', "'self'", 'data:', 'blob:', ...(tiles ? [tiles] : [])],
    ['font-src', "'self'", 'data:'],
    // 'self' covers same-origin WebSockets for the SignalR hubs (/hubs) in current browsers.
    ['connect-src', "'self'", ...(tiles ? [tiles] : []), ...(development ? ['ws:'] : [])],
    // MapLibre runs its tile parser in a worker created from a blob URL.
    ['worker-src', "'self'", 'blob:'],
    ['manifest-src', "'self'"],
    ['media-src', "'self'"],
    ['object-src', "'none'"],
    ['frame-src', "'none'"],
    ['base-uri', "'self'"],
    ['form-action', "'self'"],
    ['frame-ancestors', "'none'"],
  ];
  if (https) {
    directives.push(['upgrade-insecure-requests']);
  }
  return directives.map((parts) => parts.join(' ')).join('; ');
}

/** 128 random bits, base64. */
export function createNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary);
}
