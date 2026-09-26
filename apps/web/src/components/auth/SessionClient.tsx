'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SkeletonList } from '@/components/ui/states';
import { usePathname, useRouter } from '@/i18n/navigation';
import { browserApi, refreshSession, setSessionExpiredHandler } from '@/lib/api/client';
import { signInPathFor, withReturnTo } from '@/lib/auth/paths';

/**
 * Landing page of the server guards when the access cookie is missing or expired: tries one silent refresh (the
 * refresh cookie is only sent to `/api/v1/auth`), confirms the session with `/me`, then returns to `returnTo`.
 * Otherwise it sends the user to the right sign-in page, keeping `returnTo`.
 */
export function SessionRestore({ returnTo }: { returnTo: string }) {
  const t = useTranslations('auth.session');
  const router = useRouter();
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void (async () => {
      const restored = (await refreshSession()) && (await browserApi.GET('/api/v1/me')).response.ok;
      router.replace(restored ? returnTo : withReturnTo(signInPathFor(returnTo), returnTo));
    })();
  }, [returnTo, router]);

  return (
    <div className="mx-auto w-full max-w-[460px] px-4 py-12">
      <SkeletonList rows={2} label={t('restoring')} />
    </div>
  );
}

/**
 * Mounted by private pages: when a client-side API call fails with 401 and the silent refresh also fails, go to
 * sign-in and come back here afterwards (R-WEB-08 expired-session handling).
 */
export function SessionExpiryRedirect() {
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    setSessionExpiredHandler(() => router.replace(withReturnTo(signInPathFor(pathname), pathname)));
    return () => setSessionExpiredHandler(() => {});
  }, [pathname, router]);

  return null;
}

/** Ends the current session (API revokes it and clears the cookies), then returns to the matching sign-in page. */
export function SignOutButton({ staff = false, className }: { staff?: boolean; className?: string }) {
  const t = useTranslations('auth');
  const router = useRouter();
  const [pending, setPending] = useState(false);

  return (
    <Button
      variant="outline"
      size="sm"
      icon="logout"
      loading={pending}
      className={className}
      onClick={async () => {
        setPending(true);
        await browserApi.POST('/api/v1/auth/sign-out');
        router.replace(staff ? '/auth/staff/sign-in' : '/auth/sign-in');
        router.refresh();
      }}
    >
      {t('signOut')}
    </Button>
  );
}
