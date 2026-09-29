'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Icon } from '@/components/ui/icons';
import { Link, usePathname } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import { withReturnTo } from '@/lib/auth/paths';
import { cn } from '@/lib/cn';

type Saved = { shopIds: string[]; professionalIds: string[] };

/** What the visitor saved, `'signedOut'` (401) or `'notCustomer'` (403, staff). One request per page load. */
type Probe = Saved | 'signedOut' | 'notCustomer';
let probe: Promise<Probe> | null = null;

/**
 * A plain fetch, not the session client: an anonymous visitor's 401 must not start a session refresh or a redirect.
 * A signed-in customer whose access cookie just expired is shown the sign-in link, which returns them here.
 */
function savedFavorites(): Promise<Probe> {
  probe ??= fetch('/api/v1/me/favorites', { credentials: 'include', cache: 'no-store' })
    .then(async (response): Promise<Probe> => {
      if (response.ok) return (await response.json()) as Saved;
      return response.status === 403 ? 'notCustomer' : 'signedOut';
    })
    .catch((): Probe => 'signedOut');
  return probe;
}

type Target = { kind: 'shop'; id: string } | { kind: 'professional'; id: string; shopId: string };

type State = 'unknown' | 'signedOut' | 'saved' | 'notSaved';

/**
 * The heart on shop and professional pages and in the favorites list (R-CUS-10, D-098). Signed-out visitors get a
 * link to sign in (and come back here); customers toggle it optimistically, rolled back if the API refuses. Staff
 * accounts see nothing (they cannot save favorites).
 */
export function FavoriteButton({
  target,
  name,
  initiallySaved,
  className,
}: {
  target: Target;
  name: string;
  /** Known on the favorites page; public pages ask the API. */
  initiallySaved?: boolean;
  className?: string;
}) {
  const t = useTranslations('favorites');
  const pathname = usePathname();
  const [state, setState] = useState<State>(
    initiallySaved === undefined ? 'unknown' : initiallySaved ? 'saved' : 'notSaved',
  );
  const [hidden, setHidden] = useState(false);
  const [busy, setBusy] = useState(false);
  const { kind, id } = target;

  useEffect(() => {
    if (initiallySaved !== undefined) return;
    let live = true;
    void savedFavorites().then((saved) => {
      if (!live) return;
      if (saved === 'notCustomer') {
        setHidden(true);
      } else if (saved === 'signedOut') {
        setState('signedOut');
      } else {
        const ids = kind === 'shop' ? saved.shopIds : saved.professionalIds;
        setState(ids.includes(id) ? 'saved' : 'notSaved');
      }
    });
    return () => {
      live = false;
    };
  }, [initiallySaved, kind, id]);

  const base = cn(
    'inline-flex size-11 shrink-0 items-center justify-center rounded-full border border-border bg-surface transition-colors hover:bg-bg-subtle',
    className,
  );

  if (hidden || state === 'unknown') {
    return <span aria-hidden="true" className={cn(base, 'invisible')} />;
  }

  if (state === 'signedOut') {
    return (
      <Link
        href={withReturnTo('/auth/sign-in', pathname)}
        className={base}
        aria-label={t('signInToSave', { name })}
      >
        <Icon name="heart" className="size-5 text-text-secondary" />
      </Link>
    );
  }

  const saved = state === 'saved';
  const toggle = async () => {
    setBusy(true);
    setState(saved ? 'notSaved' : 'saved');
    try {
      if (target.kind === 'shop') {
        const path = { params: { path: { shopId: target.id } } };
        ensureOk(
          saved
            ? await browserApi.DELETE('/api/v1/me/favorites/shops/{shopId}', path)
            : await browserApi.PUT('/api/v1/me/favorites/shops/{shopId}', path),
        );
      } else {
        const path = { params: { path: { professionalId: target.id } } };
        ensureOk(
          saved
            ? await browserApi.DELETE('/api/v1/me/favorites/professionals/{professionalId}', path)
            : await browserApi.PUT('/api/v1/me/favorites/professionals/{professionalId}', {
                ...path,
                body: { shopId: target.shopId },
              }),
        );
      }
      probe = null;
    } catch {
      setState(saved ? 'saved' : 'notSaved');
    } finally {
      setBusy(false);
    }
  };

  return (
    <button
      type="button"
      aria-pressed={saved}
      aria-label={t(saved ? 'remove' : 'add', { name })}
      disabled={busy}
      onClick={toggle}
      className={base}
      data-testid="favorite-button"
    >
      <Icon
        name="heart"
        className={cn('size-5', saved ? 'fill-danger-500 text-danger-500' : 'text-text-secondary')}
      />
    </button>
  );
}
