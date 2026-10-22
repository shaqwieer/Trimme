'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, ButtonLink, IconButton } from '@/components/ui/Button';
import { Switch } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { CatalogStatusBadge } from './CatalogStatusBadge';
import { ServiceForm, type ServiceFormProfessional } from './ServiceForm';

type Service = components['schemas']['ShopServiceResponse'];
type Category = components['schemas']['ServiceCategoryResponse'];

async function send(id: string, action: 'activate' | 'deactivate' | 'archive') {
  const path = { params: { path: { serviceId: id } } };
  if (action === 'activate')
    return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/activate', path));
  if (action === 'deactivate')
    return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/deactivate', path));
  return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/archive', path));
}

/**
 * The owner's services (s-services, corrected per DV-S03; back with D-137): on/off switch, keyboard reorder (move up and
 * down buttons, announced), edit, archive and, for a service nothing uses, delete. Packages stay the admin's (D-130).
 */
export function ShopServiceList({ items }: { items: Service[] }) {
  const t = useTranslations('shopServices');
  const tCatalog = useTranslations('catalog');
  const locale = useLocale();
  const lang = asLocale(locale);
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  // Server order, unless an optimistic move over the same set of items is pending.
  const serverOrder = items.filter((i) => !i.isArchived).map((i) => i.id);
  const [optimistic, setOrder] = useState<string[] | null>(null);
  const order =
    optimistic &&
    optimistic.length === serverOrder.length &&
    optimistic.every((id) => serverOrder.includes(id))
      ? optimistic
      : serverOrder;
  const [announcement, setAnnouncement] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [failure, setFailure] = useState<string>();
  const [confirm, setConfirm] = useState<{ item: Service; action: 'archive' | 'delete' } | null>(null);

  const byId = new Map(items.map((item) => [item.id, item]));
  const live = order.map((id) => byId.get(id)).filter((item): item is Service => item !== undefined);
  const archived = items.filter((item) => item.isArchived);
  const nameOf = (item: Service) => localizedName(locale, item.nameAr, item.nameEn);

  const run = async (key: string, action: () => Promise<unknown>) => {
    setBusy(key);
    setFailure(undefined);
    try {
      await action();
      router.refresh();
      return true;
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      return false;
    } finally {
      setBusy(null);
    }
  };

  const move = async (index: number, delta: -1 | 1) => {
    const target = index + delta;
    if (target < 0 || target >= order.length) return;
    const next = [...order];
    [next[index], next[target]] = [next[target]!, next[index]!];
    const previous = order;
    setOrder(next);
    const moved = byId.get(next[target]!)!;
    const saved = await run(`move-${moved.id}`, async () =>
      ensureOk(await browserApi.PUT('/api/v1/shop/services/order', { body: { orderedIds: next } })),
    );
    if (saved) setAnnouncement(t('moved', { name: nameOf(moved), position: target + 1 }));
    else setOrder(previous); // roll back the optimistic move
  };

  const row = (item: Service, index: number | null) => (
    <li
      key={item.id}
      className="flex flex-col gap-3 py-4 md:flex-row md:items-center"
      data-testid={`catalog-row-${item.id}`}
    >
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-bold text-text-primary">{nameOf(item)}</span>
          <CatalogStatusBadge item={item} />
        </div>
        <span className="text-caption text-text-secondary">
          {formatPrice(item.price, lang, item.currency)} · {formatDurationMinutes(item.durationMinutes, lang)}{' '}
          · {t('assigned', { count: item.assignedProfessionalCount })}
        </span>
        {item.moderation === 'Hidden' && item.moderationReason && (
          <span className="text-helper text-danger-700">
            {tCatalog('hiddenReason', { reason: item.moderationReason })}
          </span>
        )}
      </div>
      {!item.isArchived && (
        <div className="flex flex-wrap items-center gap-2">
          <Switch
            checked={item.isActive}
            label={t('activeFor', { name: nameOf(item) })}
            hideLabel
            disabled={busy !== null}
            onCheckedChange={(on) =>
              void run(`toggle-${item.id}`, () => send(item.id, on ? 'activate' : 'deactivate'))
            }
          />
          <span aria-hidden="true" className="text-caption text-text-primary">
            {t('active')}
          </span>
          {index !== null && (
            <>
              <IconButton
                icon="chevD"
                className="rotate-180"
                label={t('moveUp', { name: nameOf(item) })}
                disabled={index === 0 || busy !== null}
                onClick={() => void move(index, -1)}
              />
              <IconButton
                icon="chevD"
                label={t('moveDown', { name: nameOf(item) })}
                disabled={index === live.length - 1 || busy !== null}
                onClick={() => void move(index, 1)}
              />
            </>
          )}
          <ButtonLink
            href={`/shop/services/${item.id}`}
            variant="secondary"
            size="sm"
            icon="edit"
            aria-label={t('editItem', { name: nameOf(item) })}
          >
            {t('edit')}
          </ButtonLink>
          <Button
            variant="ghost"
            size="sm"
            icon="book"
            aria-label={t('archiveItem', { name: nameOf(item) })}
            onClick={() => setConfirm({ item, action: 'archive' })}
          >
            {t('archive')}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            icon="trash"
            aria-label={t('deleteItem', { name: nameOf(item) })}
            onClick={() => setConfirm({ item, action: 'delete' })}
          >
            {t('delete')}
          </Button>
        </div>
      )}
    </li>
  );

  return (
    <div className="flex flex-col gap-3">
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
      {failure && <InlineAlert tone="danger" title={failure} />}
      <ul className="flex flex-col divide-y divide-border-row" data-testid="shop-services">
        {live.map((item, index) => row(item, index))}
        {archived.map((item) => row(item, null))}
      </ul>
      <ConfirmDialog
        open={confirm !== null}
        onOpenChange={(open) => !open && setConfirm(null)}
        title={
          confirm
            ? t(confirm.action === 'archive' ? 'archiveTitle' : 'deleteTitle', { name: nameOf(confirm.item) })
            : ''
        }
        body={confirm?.action === 'delete' ? t('deleteBody') : t('archiveBody')}
        confirmLabel={confirm?.action === 'delete' ? t('delete') : t('archive')}
        loading={busy === 'confirm'}
        onConfirm={() => {
          if (!confirm) return;
          const { item, action } = confirm;
          void run('confirm', async () =>
            action === 'archive'
              ? send(item.id, 'archive')
              : ensureOk(
                  await browserApi.DELETE('/api/v1/shop/services/{serviceId}', {
                    params: { path: { serviceId: item.id } },
                  }),
                ),
          ).then(() => setConfirm(null));
        }}
      />
    </div>
  );
}

/**
 * The owner adds or edits one of the shop's services (D-137) with the shop's own barbers who do it. A new service starts
 * with every active barber picked and opens its own page once created.
 */
export function ShopServiceEditor({
  service,
  categories,
  professionals,
}: {
  service?: Service;
  categories: Category[];
  professionals: ServiceFormProfessional[];
}) {
  const router = useRouter();
  return (
    <ServiceForm
      categories={categories}
      service={service}
      professionals={professionals}
      assignedIds={service?.professionalIds}
      barbersAddedBy="platform"
      onSubmitValues={async (body, _reason, professionalIds) => {
        if (service) {
          ensureOk(
            await browserApi.PUT('/api/v1/shop/services/{serviceId}', {
              params: { path: { serviceId: service.id } },
              body: { ...body, professionalIds },
            }),
          );
          return;
        }
        const created = ensureOk(
          await browserApi.POST('/api/v1/shop/services', { body: { ...body, professionalIds } }),
        );
        router.push(`/shop/services/${created.id}`);
        return 'navigated';
      }}
    />
  );
}
