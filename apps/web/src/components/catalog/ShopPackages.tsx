'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { CatalogStatusBadge } from './CatalogStatusBadge';
import { PackageForm } from './PackageForm';

type Package = components['schemas']['ShopPackageResponse'];
type Service = components['schemas']['ShopServiceResponse'];

/**
 * The owner's packages (D-147: «الخدمات والباقات» are the owner's again; D-130 had kept them the admin's): a package of
 * the shop's own services, with its own price and total length. The admin keeps editing them too.
 */
export function ShopPackageEditor({ pkg, services }: { pkg?: Package; services: Service[] }) {
  const router = useRouter();
  return (
    <PackageForm
      services={services}
      pkg={pkg}
      onSubmitValues={async (body) => {
        if (pkg) {
          ensureOk(
            await browserApi.PUT('/api/v1/shop/packages/{packageId}', {
              params: { path: { packageId: pkg.id } },
              body: { ...body, version: pkg.version },
            }),
          );
          return;
        }
        const created = ensureOk(await browserApi.POST('/api/v1/shop/packages', { body }));
        router.push(`/shop/packages/${created.id}`);
        return 'navigated';
      }}
    />
  );
}

/** The shop's packages on the services page: name, contents, price, length and state, each opening its own page. */
export function ShopPackageList({ items }: { items: Package[] }) {
  const t = useTranslations('shopServices.packages');
  const locale = useLocale() as AppLocale;
  if (items.length === 0) return <p className="py-4 text-caption text-text-secondary">{t('empty')}</p>;
  return (
    <ul className="flex flex-col divide-y divide-border-row" data-testid="shop-package-list">
      {items.map((p) => (
        <li key={p.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
          <Link href={`/shop/packages/${p.id}`} className="flex min-w-0 flex-col hover:underline">
            <span className="text-label font-bold text-text-primary">
              {localizedName(locale, p.nameAr, p.nameEn)}
            </span>
            <span className="text-helper text-text-secondary">
              {p.items.map((i) => localizedName(locale, i.nameAr, i.nameEn ?? null)).join(' + ')}
            </span>
          </Link>
          <span className="flex items-center gap-3">
            <span className="text-helper text-text-secondary">
              {formatDurationMinutes(p.durationMinutes, locale)} ·{' '}
              <span className="font-latin font-bold text-navy-900">
                {formatPrice(p.price, locale, p.currency)}
              </span>
            </span>
            <CatalogStatusBadge item={p} />
          </span>
        </li>
      ))}
    </ul>
  );
}

/** Turn a package on or off, or retire it for good (after a confirmation). */
export function ShopPackageStateControls({ pkg }: { pkg: Package }) {
  const t = useTranslations('shopServices.packages');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [busy, setBusy] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const [error, setError] = useState<string>();
  if (pkg.isArchived) return null;

  const change = async (action: 'activate' | 'deactivate' | 'archive') => {
    setBusy(true);
    setError(undefined);
    try {
      const options = { params: { path: { packageId: pkg.id } } };
      ensureOk(
        action === 'activate'
          ? await browserApi.POST('/api/v1/shop/packages/{packageId}/activate', options)
          : action === 'deactivate'
            ? await browserApi.POST('/api/v1/shop/packages/{packageId}/deactivate', options)
            : await browserApi.POST('/api/v1/shop/packages/{packageId}/archive', options),
      );
      setArchiving(false);
      if (action === 'archive') router.push('/shop/services');
      router.refresh();
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      {error && <InlineAlert tone="danger" title={error} />}
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          variant="outline"
          loading={busy && !archiving}
          disabled={busy}
          onClick={() => void change(pkg.isActive ? 'deactivate' : 'activate')}
        >
          {pkg.isActive ? t('deactivate') : t('activate')}
        </Button>
        <Button size="sm" variant="danger" disabled={busy} onClick={() => setArchiving(true)}>
          {t('archive')}
        </Button>
      </div>
      <ConfirmDialog
        open={archiving}
        onOpenChange={(open) => !busy && setArchiving(open)}
        tone="danger"
        title={t('archiveTitle')}
        body={t('archiveBody')}
        confirmLabel={t('archiveConfirm')}
        cancelLabel={t('keep')}
        loading={busy}
        onConfirm={() => void change('archive')}
      />
    </div>
  );
}
