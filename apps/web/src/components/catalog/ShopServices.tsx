'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, ButtonLink, IconButton } from '@/components/ui/Button';
import { TextField } from '@/components/ui/inputs';
import { NumberStepper } from '@/components/ui/NumberStepper';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { DURATION_OPTIONS, parsePrice } from '@/lib/forms/price';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { CatalogStatusBadge } from './CatalogStatusBadge';
import { ServiceForm, type ServiceFormProfessional, type ServiceFormSection } from './ServiceForm';

type Service = components['schemas']['ShopServiceResponse'];

async function send(id: string, action: 'activate' | 'deactivate' | 'archive') {
  const path = { params: { path: { serviceId: id } } };
  if (action === 'activate')
    return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/activate', path));
  if (action === 'deactivate')
    return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/deactivate', path));
  return ensureOk(await browserApi.POST('/api/v1/shop/services/{serviceId}/archive', path));
}

/** A section heading for the tiles; `null` holds the services in no section («أخرى»). */
export type ServiceListSection = { id: string; nameAr: string; nameEn: string | null };

/**
 * The owner's services (s-services, corrected per DV-S03; back with D-137), as tiles grouped by the shop's own sections
 * (D-151, the client's 3-2: «الشعر», «اللحية»…, then «أخرى»). Each tile changes its price and its length (− and +, 5
 * minutes at a time) and saves them on its own button with the service's version; the badge says its state and the
 * button the action (D-149); keyboard reorder within its section (announced), the full form, archive and, for a
 * service nothing uses, delete. A changed price never touches bookings already made.
 */
export function ShopServiceList({
  items,
  sections = [],
}: {
  items: Service[];
  sections?: ServiceListSection[];
}) {
  const t = useTranslations('shopServices');
  const locale = useLocale();
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
  const known = new Set(sections.map((s) => s.id));
  const sectionOf = (item: Service) => (item.sectionId && known.has(item.sectionId) ? item.sectionId : null);
  const groups = [
    ...sections.map((s) => ({ id: s.id as string | null, title: localizedName(locale, s.nameAr, s.nameEn) })),
    { id: null, title: t('otherSection') },
  ]
    .map((group) => ({ ...group, items: live.filter((item) => sectionOf(item) === group.id) }))
    .filter((group) => group.items.length > 0);

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

  // Up and down within the tile's section: it swaps places with its neighbour there in the shop's one order.
  const move = async (group: Service[], index: number, delta: -1 | 1) => {
    const neighbour = group[index + delta];
    const item = group[index];
    if (!neighbour || !item) return;
    const next = [...order];
    const a = next.indexOf(item.id);
    const b = next.indexOf(neighbour.id);
    [next[a], next[b]] = [next[b]!, next[a]!];
    const previous = order;
    setOrder(next);
    const saved = await run(`move-${item.id}`, async () =>
      ensureOk(await browserApi.PUT('/api/v1/shop/services/order', { body: { orderedIds: next } })),
    );
    if (saved) setAnnouncement(t('moved', { name: nameOf(item), position: index + delta + 1 }));
    else setOrder(previous); // roll back the optimistic move
  };

  const save = (item: Service, price: number, durationMinutes: number) =>
    run(`save-${item.id}`, async () =>
      ensureOk(
        await browserApi.PUT('/api/v1/shop/services/{serviceId}', {
          params: { path: { serviceId: item.id } },
          body: {
            nameAr: item.nameAr,
            nameEn: item.nameEn,
            descriptionAr: item.descriptionAr,
            descriptionEn: item.descriptionEn,
            categoryId: item.categoryId,
            sectionId: item.sectionId ?? null,
            onlineBookable: item.onlineBookable,
            professionalIds: item.professionalIds,
            price,
            durationMinutes,
            version: item.version,
          },
        }),
      ),
    );

  const tiles = (group: Service[], movable: boolean) => (
    <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
      {group.map((item, index) => (
        <ServiceTile
          key={item.id}
          item={item}
          busy={busy}
          onToggle={() =>
            void run(`toggle-${item.id}`, () => send(item.id, item.isActive ? 'deactivate' : 'activate'))
          }
          onSave={(price, durationMinutes) => save(item, price, durationMinutes)}
          onMove={
            movable
              ? {
                  up: index === 0 ? undefined : () => void move(group, index, -1),
                  down: index === group.length - 1 ? undefined : () => void move(group, index, 1),
                }
              : undefined
          }
          onArchive={() => setConfirm({ item, action: 'archive' })}
          onDelete={() => setConfirm({ item, action: 'delete' })}
        />
      ))}
    </ul>
  );

  return (
    <div className="flex flex-col gap-4">
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
      {failure && <InlineAlert tone="danger" title={failure} />}
      <div className="flex flex-col gap-5" data-testid="shop-services">
        {groups.map((group) => (
          <section
            key={group.id ?? 'other'}
            aria-labelledby={`service-group-${group.id ?? 'other'}`}
            className="flex flex-col gap-2"
            data-testid={`service-group-${group.id ?? 'other'}`}
          >
            {/* With no sections at all, the services need no visible heading. */}
            <h3
              id={`service-group-${group.id ?? 'other'}`}
              className={sections.length > 0 ? 'text-h3 font-bold text-navy-900' : 'sr-only'}
            >
              {group.title}
            </h3>
            {tiles(group.items, true)}
          </section>
        ))}
        {archived.length > 0 && (
          <section aria-labelledby="service-group-archived" className="flex flex-col gap-2">
            <h3 id="service-group-archived" className="text-h3 font-bold text-text-secondary">
              {t('archivedGroup')}
            </h3>
            {tiles(archived, false)}
          </section>
        )}
      </div>
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

/** One service as a tile (D-151): its price and length changed here, the rest in the full form. */
function ServiceTile({
  item,
  busy,
  onToggle,
  onSave,
  onMove,
  onArchive,
  onDelete,
}: {
  item: Service;
  busy: string | null;
  onToggle: () => void;
  onSave: (price: number, durationMinutes: number) => Promise<boolean>;
  onMove?: { up?: () => void; down?: () => void };
  onArchive: () => void;
  onDelete: () => void;
}) {
  const t = useTranslations('shopServices');
  const tForm = useTranslations('shopServices.form');
  const tCatalog = useTranslations('catalog');
  const tv = useTranslations('validation');
  const locale = useLocale();
  const lang = asLocale(locale);
  const name = localizedName(locale, item.nameAr, item.nameEn);
  const [price, setPrice] = useState(String(item.price));
  const [duration, setDuration] = useState(item.durationMinutes);
  const [saved, setSaved] = useState(false);
  // A newer version from the server (this save, or another screen's) replaces what the fields hold.
  const [version, setVersion] = useState(item.version);
  if (item.version !== version) {
    setVersion(item.version);
    setPrice(String(item.price));
    setDuration(item.durationMinutes);
  }
  const parsed = parsePrice(price);
  const dirty = parsed !== item.price || duration !== item.durationMinutes;

  return (
    <li
      className={cn(
        'flex flex-col gap-3 rounded-card border border-border p-4',
        item.isActive && !item.isArchived ? 'bg-surface' : 'bg-bg-muted',
      )}
      data-testid={`catalog-row-${item.id}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="font-bold text-text-primary">{name}</span>
          <span className="text-helper text-text-secondary">
            {t('assigned', { count: item.assignedProfessionalCount })}
          </span>
        </div>
        <CatalogStatusBadge item={item} />
      </div>
      {item.moderation === 'Hidden' && item.moderationReason && (
        <span className="text-helper text-danger-700">
          {tCatalog('hiddenReason', { reason: item.moderationReason })}
        </span>
      )}
      {item.isArchived ? (
        <span className="text-caption text-text-secondary">
          {formatPrice(item.price, lang, item.currency)} — {formatDurationMinutes(item.durationMinutes, lang)}
        </span>
      ) : (
        <form
          method="post"
          className="flex flex-col gap-3"
          aria-label={t('tileForm', { name })}
          onSubmit={(event) => {
            event.preventDefault();
            if (parsed === null || !dirty) return;
            setSaved(false);
            void onSave(parsed, duration).then((ok) => setSaved(ok));
          }}
        >
          <TextField
            label={tForm('price')}
            inputMode="decimal"
            autoComplete="off"
            value={price}
            error={parsed === null ? tv('priceInvalid') : undefined}
            onChange={(event) => {
              setPrice(event.target.value);
              setSaved(false);
            }}
          />
          <NumberStepper
            label={tForm('duration')}
            value={duration}
            min={DURATION_OPTIONS[0]!}
            max={DURATION_OPTIONS.at(-1)!}
            step={5}
            format={(minutes) => formatDurationMinutes(minutes, lang)}
            onChange={(minutes) => {
              setDuration(minutes);
              setSaved(false);
            }}
          />
          <div className="flex min-h-9 flex-wrap items-center gap-2">
            {dirty && (
              <Button
                type="submit"
                size="sm"
                disabled={parsed === null || busy !== null}
                loading={busy === `save-${item.id}`}
                aria-label={t('saveItem', { name })}
              >
                {t('saveTile')}
              </Button>
            )}
            <span role="status" className="text-helper font-bold text-success-700">
              {saved && !dirty ? t('saved') : ''}
            </span>
          </div>
        </form>
      )}
      {!item.isArchived && (
        <div className="flex flex-wrap items-center gap-1.5 border-t border-border-row pt-3">
          {/* The badge says the state, the button says what it does (D-149, the owner's item 3). */}
          <Button
            size="sm"
            variant="outline"
            aria-label={t(item.isActive ? 'stopBookingFor' : 'startBookingFor', { name })}
            loading={busy === `toggle-${item.id}`}
            disabled={busy !== null}
            onClick={onToggle}
          >
            {t(item.isActive ? 'stopBooking' : 'startBooking')}
          </Button>
          <ButtonLink
            href={`/shop/services/${item.id}`}
            variant="secondary"
            size="sm"
            icon="edit"
            aria-label={t('editItem', { name })}
          >
            {t('edit')}
          </ButtonLink>
          {onMove && (
            <>
              <IconButton
                icon="chevD"
                size="sm"
                className="rotate-180"
                label={t('moveUp', { name })}
                disabled={!onMove.up || busy !== null}
                onClick={onMove.up}
              />
              <IconButton
                icon="chevD"
                size="sm"
                label={t('moveDown', { name })}
                disabled={!onMove.down || busy !== null}
                onClick={onMove.down}
              />
            </>
          )}
          <IconButton icon="book" size="sm" label={t('archiveItem', { name })} onClick={onArchive} />
          <IconButton icon="trash" size="sm" label={t('deleteItem', { name })} onClick={onDelete} />
        </div>
      )}
    </li>
  );
}

/**
 * The owner adds or edits one of the shop's services (D-137) with the shop's own barbers who do it. A new service starts
 * with every active barber picked and opens its own page once created.
 */
export function ShopServiceEditor({
  service,
  sections,
  professionals,
}: {
  service?: Service;
  sections: ServiceFormSection[];
  professionals: ServiceFormProfessional[];
}) {
  const router = useRouter();
  return (
    <ServiceForm
      sections={sections}
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
