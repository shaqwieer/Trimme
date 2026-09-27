'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, ButtonLink, IconButton } from '@/components/ui/Button';
import { Switch } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { CatalogStatusBadge } from './CatalogStatusBadge';

type Service = components['schemas']['ShopServiceResponse'];
type Package = components['schemas']['ShopPackageResponse'];
type Item = Service | Package;
type Kind = 'services' | 'packages';

const isPackage = (item: Item): item is Package => 'items' in item;

async function send(kind: Kind, id: string, action: 'activate' | 'deactivate' | 'archive') {
  const service = { params: { path: { serviceId: id } } };
  const pkg = { params: { path: { packageId: id } } };
  if (kind === 'services') {
    if (action === 'activate')
      return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/activate', service));
    if (action === 'deactivate')
      return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/deactivate', service));
    return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/archive', service));
  }
  if (action === 'activate')
    return ensureOk(await browserApi.POST('/api/v1/shop/packages/{packageId}/activate', pkg));
  if (action === 'deactivate')
    return ensureOk(await browserApi.POST('/api/v1/shop/packages/{packageId}/deactivate', pkg));
  return ensureOk(await browserApi.POST('/api/v1/shop/packages/{packageId}/archive', pkg));
}

async function reorder(kind: Kind, orderedIds: string[]) {
  if (kind === 'services')
    ensureOk(await browserApi.PUT('/api/v1/shop/services/order', { body: { orderedIds } }));
  else ensureOk(await browserApi.PUT('/api/v1/shop/packages/order', { body: { orderedIds } }));
}

/**
 * The shop's services or packages (s-services, corrected per DV-S03): on/off switch, keyboard reorder (move up/down
 * buttons, announced), edit, archive and — for services nothing uses — delete. Drag-and-drop reorder is deferred.
 */
export function ShopCatalogList({
  kind,
  items,
  canManage,
}: {
  kind: Kind;
  items: Item[];
  canManage: boolean;
}) {
  const t = useTranslations('shopServices');
  const tCatalog = useTranslations('catalog');
  const locale = useLocale();
  const lang = locale === 'en' ? 'en' : 'ar';
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
  const [confirm, setConfirm] = useState<{ item: Item; action: 'archive' | 'delete' } | null>(null);

  const byId = new Map(items.map((item) => [item.id, item]));
  const live = order.map((id) => byId.get(id)).filter((item): item is Item => item !== undefined);
  const archived = items.filter((item) => item.isArchived);
  const nameOf = (item: Item) => localizedName(locale, item.nameAr, item.nameEn);

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
    if (await run(`move-${moved.id}`, () => reorder(kind, next))) {
      setAnnouncement(t('moved', { name: nameOf(moved), position: target + 1 }));
    } else {
      setOrder(previous); // roll back the optimistic move
    }
  };

  const row = (item: Item, index: number | null) => (
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
          {formatPrice(item.price, lang, item.currency)} · {formatDurationMinutes(item.durationMinutes, lang)}
          {isPackage(item)
            ? ` · ${t('items', { count: item.items.length })}`
            : ` · ${t('assigned', { count: item.assignedProfessionalCount })}`}
        </span>
        {item.moderation === 'Hidden' && item.moderationReason && (
          <span className="text-helper text-danger-700">
            {tCatalog('hiddenReason', { reason: item.moderationReason })}
          </span>
        )}
        {isPackage(item) && !item.isArchived && !item.isBookable && item.moderation === 'Visible' && (
          <span className="text-helper text-warning-700">{tCatalog('unbookable')}</span>
        )}
      </div>
      {canManage && !item.isArchived && (
        <div className="flex flex-wrap items-center gap-2">
          <Switch
            checked={item.isActive}
            label={t('activeFor', { name: nameOf(item) })}
            hideLabel
            disabled={busy !== null}
            onCheckedChange={(on) =>
              void run(`toggle-${item.id}`, () => send(kind, item.id, on ? 'activate' : 'deactivate'))
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
            href={kind === 'services' ? `/shop/services/${item.id}` : `/shop/packages/${item.id}`}
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
          {kind === 'services' && (
            <Button
              variant="ghost"
              size="sm"
              icon="trash"
              aria-label={t('deleteItem', { name: nameOf(item) })}
              onClick={() => setConfirm({ item, action: 'delete' })}
            >
              {t('delete')}
            </Button>
          )}
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
      <ul className="flex flex-col divide-y divide-border-row" data-testid={`shop-${kind}`}>
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
              ? send(kind, item.id, 'archive')
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
