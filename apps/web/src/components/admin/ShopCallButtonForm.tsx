'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Switch, TextField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { codeToMessageKey } from '@/lib/forms/problem';

/**
 * The «اتصال» button on a shop's public page (D-155, the client's 5-1-7): the admin's only, off by default, with the
 * number the admin sets (not the shop's own public number, which the owner edits). The owner never sees this. Turning
 * it on needs a number; turning it off keeps the number for next time.
 */
export function ShopCallButtonForm({
  shopId,
  enabled,
  phone,
  canEdit,
}: {
  shopId: string;
  enabled: boolean;
  phone: string | null;
  canEdit: boolean;
}) {
  const t = useTranslations('adminShops.callButton');
  const tv = useTranslations('validation');
  const apiMessage = useApiErrorMessage();
  const router = useRouter();
  const [on, setOn] = useState(enabled);
  const [number, setNumber] = useState(phone ?? '');
  const [pending, setPending] = useState(false);
  const [fieldError, setFieldError] = useState<string>();
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string }>();

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setFieldError(undefined);
    setNotice(undefined);
    setPending(true);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/shops/{shopId}/call-button', {
          params: { path: { shopId } },
          body: { enabled: on, phone: number.trim() || null },
        }),
      );
      setNotice({ tone: 'success', text: on ? t('savedOn') : t('savedOff') });
      router.refresh();
    } catch (failure) {
      if (failure instanceof ApiError && failure.isValidation) {
        const code = Object.values(failure.fieldErrors)[0]?.[0];
        setFieldError(tv(codeToMessageKey(code) as 'generic'));
      } else {
        setNotice({
          tone: 'danger',
          text: apiMessage(failure instanceof ApiError ? failure : 'server.unexpected') ?? '',
        });
      }
    } finally {
      setPending(false);
    }
  };

  return (
    <form
      method="post"
      noValidate
      onSubmit={(e) => void save(e)}
      className="flex flex-col gap-4"
      data-testid="call-button-form"
    >
      <p className="text-caption text-text-secondary">{t('body')}</p>
      <Switch checked={on} onCheckedChange={setOn} label={t('enabled')} disabled={!canEdit || pending} />
      <TextField
        label={t('phone')}
        helper={t('phoneHelper')}
        type="tel"
        dir="ltr"
        inputMode="tel"
        autoComplete="off"
        value={number}
        onChange={(e) => setNumber(e.target.value)}
        error={fieldError}
        readOnly={!canEdit}
        optional={!on}
      />
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      {canEdit && (
        <Button type="submit" size="md" loading={pending} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
