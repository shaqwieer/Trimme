'use client';

import { useEffect, useRef } from 'react';
import { browserApi } from '@/lib/api/client';

/**
 * Counts the scan from the browser (R-QR-02, D-114): the API records the visit — no IP address, no tracking — and sets
 * the first-party HttpOnly attribution cookie on this response, which a Server Component could not hand to the browser.
 * A reload within the reload window reuses the visit. Nothing is shown and failures are ignored: the page works without it.
 */
export function QrVisitRecorder({ code, locale }: { code: string; locale: string }) {
  const sent = useRef(false);
  useEffect(() => {
    if (sent.current) return;
    sent.current = true;
    void browserApi
      .POST('/api/v1/public/qr/{code}/visits', { params: { path: { code } }, body: { locale } })
      .catch(() => undefined);
  }, [code, locale]);
  return null;
}
