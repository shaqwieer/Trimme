'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { PROFILE_FIELDS, type ShopProfileField } from '@/components/shops/shopApi';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

/**
 * Which profile fields the shop may edit itself (DV-S16, D-065). Enforced by the API on every shop write; this form
 * only sets the policy. Verification and the page link are never shop-editable, so they are not listed.
 */
export function EditablePolicyForm({
  shopId,
  editableFields,
}: {
  shopId: string;
  editableFields: ShopProfileField[];
}) {
  const t = useTranslations('adminShops.policy');
  const tField = useTranslations('profileField');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [selected, setSelected] = useState<ShopProfileField[]>(editableFields);
  const [pending, setPending] = useState(false);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string } | null>(null);

  const save = async () => {
    setPending(true);
    setNotice(null);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/shops/{shopId}/editable-policy', {
          params: { path: { shopId } },
          body: { editableFields: selected },
        }),
      );
      setNotice({ tone: 'success', text: t('saved') });
      router.refresh();
    } catch (error) {
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      });
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-4" data-testid="editable-policy">
      <p className="text-caption text-text-secondary">{t('body')}</p>
      <fieldset className="grid min-w-0 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <legend className="sr-only">{t('title')}</legend>
        {PROFILE_FIELDS.map((field) => (
          <Checkbox
            key={field}
            label={tField(field)}
            name={`editable-${field}`}
            checked={selected.includes(field)}
            onChange={(event) =>
              setSelected((current) =>
                event.target.checked ? [...current, field] : current.filter((value) => value !== field),
              )
            }
          />
        ))}
      </fieldset>
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      <Button
        type="button"
        variant="secondary"
        size="md"
        icon="shield"
        loading={pending}
        onClick={() => void save()}
        className="self-start"
      >
        {t('save')}
      </Button>
    </div>
  );
}
