'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { TemporaryPasswordDialog } from '@/components/shop/TemporaryPasswordDialog';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';

type Owner = components['schemas']['ShopOwnerAccountResponse'];

/**
 * «المالك» on the admin's shop page (D-158, the client's request): who owns the shop's account, by name and sign-in
 * email, and «كلمة مرور مؤقتة جديدة» for one who cannot sign in. The server generates the password; it is shown once for
 * the admin to hand over, the owner's sessions end, and their next sign-in asks for a password of their own (D-139).
 */
export function ShopOwnerCard({
  shopId,
  owners,
  addHref,
}: {
  shopId: string;
  owners: Owner[];
  addHref: string;
}) {
  const t = useTranslations('adminShops.detail.owner');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [target, setTarget] = useState<Owner>();
  const [issued, setIssued] = useState<{ owner: Owner; password: string }>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [done, setDone] = useState<string>();

  const reset = async (owner: Owner) => {
    setPending(true);
    setError(undefined);
    setDone(undefined);
    try {
      const result = ensureOk(
        await browserApi.POST('/api/v1/admin/shops/{shopId}/users/{userId}/temporary-password', {
          params: { path: { shopId, userId: owner.userId } },
        }),
      );
      setIssued({ owner, password: result.temporaryPassword });
      setDone(t('saved', { name: owner.displayName }));
      router.refresh();
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setPending(false);
      setTarget(undefined);
    }
  };

  return (
    <div className="flex flex-col gap-3 border-t border-border-row pt-4" data-testid="shop-owner">
      <h3 className="text-label font-bold text-text-strong">{t('title')}</h3>
      {owners.length === 0 ? (
        <p className="text-caption text-text-secondary">
          {t('none')}{' '}
          <Link href={addHref} className="font-bold text-text-link hover:underline">
            {t('add')}
          </Link>
        </p>
      ) : (
        <ul className="flex flex-col divide-y divide-border-row">
          {owners.map((owner) => (
            <li key={owner.userId} className="flex flex-wrap items-center gap-x-3 gap-y-2 py-2.5">
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="font-bold text-text-primary">{owner.displayName || '—'}</span>
                <span
                  dir="ltr"
                  className="font-latin text-caption break-all text-text-secondary [unicode-bidi:plaintext]"
                >
                  {owner.email}
                </span>
              </div>
              {owner.isDisabled ? (
                <Badge tone="neutral">{t('disabled')}</Badge>
              ) : (
                owner.mustChangePassword && <Badge tone="warning">{t('temporary')}</Badge>
              )}
              <Button
                size="sm"
                variant="outline"
                icon="shield"
                disabled={pending || owner.isDisabled}
                onClick={() => setTarget(owner)}
              >
                {t('reset')}
              </Button>
            </li>
          ))}
        </ul>
      )}
      {done && <InlineAlert tone="success" title={done} />}
      {error && <InlineAlert tone="danger" title={error} />}

      <ConfirmDialog
        open={target !== undefined}
        onOpenChange={(open) => !open && !pending && setTarget(undefined)}
        tone="default"
        title={t('confirmTitle', { name: target?.displayName ?? '' })}
        body={t('confirmBody')}
        confirmLabel={t('reset')}
        cancelLabel={t('cancel')}
        loading={pending}
        onConfirm={() => target && void reset(target)}
      />
      <TemporaryPasswordDialog
        name={issued?.owner.displayName ?? ''}
        password={issued?.password}
        body={t('issuedBody', { email: issued?.owner.email ?? '' })}
        once={t('issuedOnce')}
        onClose={() => setIssued(undefined)}
      />
    </div>
  );
}
