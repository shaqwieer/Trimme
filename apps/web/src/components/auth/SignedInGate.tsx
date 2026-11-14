'use client';

import { useLocale } from 'next-intl';
import { useEffect } from 'react';
import { readSignedInHint, signedInDestination } from '@/lib/auth/signedIn';

/**
 * The landing and sign-in pages again in the browser (D-144): the proxy sends a signed-in user away when the page is
 * requested, but the Back button can restore it from the router or back-forward cache without a request. On mount and
 * on a restore from that cache, a signed-in browser is sent through the session page to `returnTo` or its home.
 */
export function SignedInGate({ returnTo }: { returnTo?: string }) {
  const locale = useLocale();

  useEffect(() => {
    const check = () => {
      const userType = readSignedInHint(document.cookie);
      if (userType) window.location.replace(`/${locale}${signedInDestination(userType, returnTo)}`);
    };
    const onShow = (event: PageTransitionEvent) => {
      if (event.persisted) check();
    };
    check();
    window.addEventListener('pageshow', onShow);
    return () => window.removeEventListener('pageshow', onShow);
  }, [locale, returnTo]);

  return null;
}
