'use client';

import { useQuery } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { BOARD_KEY } from './boardKey';

type Booking = components['schemas']['ShopBookingResponse'];

/**
 * A booking's services at the visit (D-147: «تعديل خدمات الحجز»). The barber takes a service off until «انتهت الخدمة»
 * (at least one stays; a package's own services stay with it) and adds one of the shop's services they do once the
 * customer has arrived. The server checks both, and refuses an addition that would run into the next booking.
 */
export function BookingServicesEditor({
  booking,
  ownProfessionalId,
  onChanged,
}: {
  booking: Booking;
  ownProfessionalId: string | null;
  onChanged: () => Promise<unknown>;
}) {
  const t = useTranslations('shopBoard.drawer.services');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const [adding, setAdding] = useState(false);
  const [serviceId, setServiceId] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string>();

  const { item } = booking;
  const isPackage = Boolean(item.packageId);
  const lines =
    item.packageItems.length > 0
      ? item.packageItems
      : [
          {
            serviceId: item.serviceId ?? '',
            nameAr: item.nameAr,
            nameEn: item.nameEn ?? null,
            price: item.price,
            durationMinutes: item.durationMinutes,
            added: false,
          },
        ];
  const removable = (line: (typeof lines)[number]) =>
    lines.length > 1 && line.price != null && line.durationMinutes != null && (!isPackage || line.added);

  const services = useQuery({
    queryKey: [BOARD_KEY, 'services-to-add'],
    enabled: adding,
    queryFn: async () => ensureOk(await browserApi.GET('/api/v1/shop/services')),
  });
  const candidates = (services.data ?? []).filter(
    (s) =>
      s.isActive &&
      !s.isArchived &&
      s.moderation === 'Visible' &&
      (!ownProfessionalId || s.professionalIds.includes(ownProfessionalId)) &&
      !lines.some((line) => line.serviceId === s.id),
  );

  const run = async (key: string, call: () => Promise<unknown>) => {
    setError(undefined);
    setBusy(key);
    try {
      await call();
      setAdding(false);
      setServiceId('');
      await onChanged();
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setBusy(null);
    }
  };

  const remove = (id: string) =>
    run(id, async () =>
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/services/{serviceId}/delete', {
          params: { path: { bookingId: booking.id, serviceId: id } },
          body: { version: booking.version },
        }),
      ),
    );
  const add = () =>
    run('add', async () =>
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/services', {
          params: { path: { bookingId: booking.id } },
          body: { serviceId, version: booking.version },
        }),
      ),
    );

  return (
    <section aria-labelledby="services-title" className="flex flex-col gap-2" data-testid="booking-services">
      <h3 id="services-title" className="text-label font-bold text-text-primary">
        {t('title')}
      </h3>
      <ul className="flex flex-col divide-y divide-border-row rounded-card border border-border">
        {lines.map((line) => (
          <li key={line.serviceId} className="flex items-center justify-between gap-2 px-3 py-2">
            <span className="flex min-w-0 flex-col">
              <span className="text-label font-bold text-text-primary">
                {localizedName(locale, line.nameAr, line.nameEn)}
              </span>
              {line.price != null && line.durationMinutes != null && (
                <span className="text-helper text-text-secondary">
                  {formatPrice(line.price, locale, item.currency)} ·{' '}
                  {formatDurationMinutes(line.durationMinutes, locale)}
                  {line.added && ` · ${t('added')}`}
                </span>
              )}
            </span>
            {removable(line) && (
              <Button
                size="sm"
                variant="ghost"
                loading={busy === line.serviceId}
                disabled={busy !== null}
                aria-label={t('removeLabel', { name: localizedName(locale, line.nameAr, line.nameEn) })}
                onClick={() => void remove(line.serviceId)}
              >
                {t('remove')}
              </Button>
            )}
          </li>
        ))}
      </ul>
      {error && <InlineAlert tone="danger" title={error} />}
      {booking.status === 'Arrived' ? (
        adding ? (
          <div className="flex flex-col gap-2">
            <SelectField
              label={t('pick')}
              value={serviceId}
              disabled={services.isPending}
              onChange={(event) => setServiceId(event.target.value)}
            >
              <option value="">{services.isPending ? t('loading') : t('choose')}</option>
              {candidates.map((s) => (
                <option key={s.id} value={s.id}>
                  {localizedName(locale, s.nameAr, s.nameEn ?? null)} —{' '}
                  {formatPrice(s.price, locale, s.currency)}
                </option>
              ))}
            </SelectField>
            <p className="text-helper text-text-tertiary">{t('addHint')}</p>
            <div className="flex gap-2">
              <Button size="sm" disabled={!serviceId} loading={busy === 'add'} onClick={() => void add()}>
                {t('confirmAdd')}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setAdding(false)}>
                {t('cancel')}
              </Button>
            </div>
          </div>
        ) : (
          <Button
            size="sm"
            variant="outline"
            icon="plus"
            className="self-start"
            onClick={() => setAdding(true)}
          >
            {t('add')}
          </Button>
        )
      ) : (
        booking.status !== 'Pending' && (
          <p className="text-helper text-text-tertiary">{t('addAfterArrival')}</p>
        )
      )}
    </section>
  );
}
