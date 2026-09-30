'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, TextField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { SegmentedControl } from '@/components/ui/selection.client';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { type AppLocale } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Option = { id: string; name: string };

/** Longest label the API accepts. */
const MAX_LABEL = 80;

/**
 * «رمز جديد» (DV-A14, D-114): a code for a shop, or for one of its active barbers «for the chair mirror», with an optional
 * label for where it is used. The API checks the barber belongs to the shop and makes the code unique.
 */
export function CreateQrCodeDialog({ shops }: { shops: Option[] }) {
  const t = useTranslations('adminQr.create');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [shopId, setShopId] = useState('');
  const [target, setTarget] = useState<'Shop' | 'Professional'>('Shop');
  const [professionalId, setProfessionalId] = useState('');
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();
  const [created, setCreated] = useState<string>();

  // The chosen shop's active barbers, loaded when the shop changes (a platform-wide list would be cut at one page).
  const [loaded, setLoaded] = useState<{ shopId: string; items: Option[] } | null>(null);
  const barbers = shopId && loaded?.shopId === shopId ? loaded.items : null;
  useEffect(() => {
    if (!shopId) return;
    let current = true;
    const keep = (items: Option[]) => current && setLoaded({ shopId, items });
    void browserApi
      .GET('/api/v1/admin/professionals', {
        params: { query: { shopId, status: 'Active', page: 1, pageSize: 100 } },
      })
      .then(({ data }) =>
        keep((data?.items ?? []).map((p) => ({ id: p.id, name: localizedName(locale, p.nameAr, p.nameEn) }))),
      )
      .catch(() => keep([]));
    return () => {
      current = false;
    };
  }, [shopId, locale]);
  const reset = () => {
    setShopId('');
    setTarget('Shop');
    setProfessionalId('');
    setLabel('');
    setFailure(undefined);
  };

  const submit = async () => {
    if (!shopId || (target === 'Professional' && !professionalId)) {
      setFailure(t('required'));
      return;
    }
    setBusy(true);
    setFailure(undefined);
    try {
      const code = ensureOk(
        await browserApi.POST('/api/v1/admin/qr/codes', {
          body: {
            shopId,
            professionalId: target === 'Professional' ? professionalId : null,
            label: label.trim() || null,
          },
        }),
      );
      setCreated(t('created', { code: code.code }));
      setOpen(false);
      reset();
      router.refresh();
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col items-end gap-2">
      <Button
        icon="plus"
        size="md"
        onClick={() => {
          reset();
          setCreated(undefined);
          setOpen(true);
        }}
      >
        {t('open')}
      </Button>
      {created && (
        <p role="status" className="text-helper font-bold text-success-700">
          {created}
        </p>
      )}
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title={t('title')}
        footer={
          <Button onClick={submit} loading={busy} fullWidth>
            {t('submit')}
          </Button>
        }
      >
        <div className="flex flex-col gap-4">
          <SelectField
            label={t('shop')}
            value={shopId}
            onChange={(event) => {
              setShopId(event.target.value);
              setProfessionalId('');
            }}
          >
            <option value="">{t('choose')}</option>
            {shops.map((shop) => (
              <option key={shop.id} value={shop.id}>
                {shop.name}
              </option>
            ))}
          </SelectField>
          <SegmentedControl
            legend={t('target')}
            name="qr-target"
            value={target}
            onValueChange={(value) => setTarget(value as 'Shop' | 'Professional')}
            options={[
              { value: 'Shop', label: t('Shop'), icon: 'store' },
              { value: 'Professional', label: t('Professional'), icon: 'user' },
            ]}
          />
          {target === 'Professional' &&
            (shopId && barbers?.length === 0 ? (
              <InlineAlert tone="warning" title={t('noProfessionals')} />
            ) : (
              <SelectField
                label={t('professional')}
                value={professionalId}
                disabled={!shopId || barbers === null}
                onChange={(event) => setProfessionalId(event.target.value)}
              >
                <option value="">{t('choose')}</option>
                {(barbers ?? []).map((barber) => (
                  <option key={barber.id} value={barber.id}>
                    {barber.name}
                  </option>
                ))}
              </SelectField>
            ))}
          <TextField
            label={t('label')}
            helper={t('labelHelper')}
            value={label}
            maxLength={MAX_LABEL}
            onChange={(event) => setLabel(event.target.value)}
          />
          {failure && <InlineAlert tone="danger" title={failure} />}
        </div>
      </Dialog>
    </div>
  );
}

/** Switch a code off (confirmed: the printed code stops opening) or back on; version-checked and audited by the API. */
export function QrCodeToggle({
  codeId,
  code,
  version,
  active,
}: {
  codeId: string;
  code: string;
  version: number;
  active: boolean;
}) {
  const t = useTranslations('adminQr.toggle');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  const run = async () => {
    setBusy(true);
    try {
      const path = { params: { path: { codeId } }, body: { version } };
      ensureOk(
        active
          ? await browserApi.POST('/api/v1/admin/qr/codes/{codeId}/deactivate', path)
          : await browserApi.POST('/api/v1/admin/qr/codes/{codeId}/activate', path),
      );
      setFailure(undefined);
      setOpen(false);
      router.refresh();
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      setOpen(false);
      if (error instanceof ApiError && error.status === 409) router.refresh();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-1.5">
      <Button size="xs" variant={active ? 'danger' : 'secondary'} onClick={() => setOpen(true)}>
        {active ? t('deactivate') : t('activate')}
      </Button>
      {failure && <InlineAlert tone="danger" title={failure} />}
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        tone={active ? 'danger' : 'default'}
        title={active ? t('deactivateTitle', { code }) : t('activateTitle', { code })}
        body={active ? t('deactivateBody') : t('activateBody')}
        confirmLabel={active ? t('deactivate') : t('activate')}
        cancelLabel={t('cancel')}
        onConfirm={run}
        loading={busy}
      />
    </div>
  );
}
