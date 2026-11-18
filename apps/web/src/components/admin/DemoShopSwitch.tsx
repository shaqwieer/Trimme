'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Switch } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

/**
 * «محل تجريبي» (D-148, «٤-١٢»): the admin marks a demo shop, so its card, page and barbers say «تجريبي» to customers.
 * Nothing is deleted; at launch the admin turns it off or archives the shop. Audited by the API (`shop.demo_changed`).
 */
export function DemoShopSwitch({
  shopId,
  isDemo,
  canEdit,
}: {
  shopId: string;
  isDemo: boolean;
  canEdit: boolean;
}) {
  const t = useTranslations('adminShops.demo');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();

  const change = async (next: boolean) => {
    setPending(true);
    setError(undefined);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/shops/{shopId}/demo', {
          params: { path: { shopId } },
          body: { isDemo: next },
        }),
      );
      router.refresh();
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-1.5" data-testid="demo-shop">
      <Switch
        checked={isDemo}
        disabled={!canEdit || pending}
        onCheckedChange={(next) => void change(next)}
        label={t('label')}
      />
      <p className="text-helper text-text-secondary">{t('hint')}</p>
      {error && <InlineAlert tone="danger" title={error} />}
    </div>
  );
}
