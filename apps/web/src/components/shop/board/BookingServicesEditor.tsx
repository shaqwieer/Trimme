'use client';

import { useQuery } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
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
 * customer has arrived. D-151 (the client's 9-2): the services to add are listed with their price and the barber's own
 * length; one that cannot finish before the barber's next booking is greyed and cannot be chosen. The server says which
 * fit, by the same rule that refuses such an addition.
 */
export function BookingServicesEditor({
  booking,
  onChanged,
}: {
  booking: Booking;
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
    queryKey: [BOARD_KEY, 'services-to-add', booking.id, booking.version],
    enabled: adding,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/bookings/{bookingId}/services/candidates', {
          params: { path: { bookingId: booking.id } },
        }),
      ),
  });
  // Those that fit first; the rest stay listed, greyed, so the barber sees why they cannot be added.
  const candidates = [...(services.data?.services ?? [])].sort((a, b) => Number(b.fits) - Number(a.fits));
  const room = services.data?.roomMinutes;

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
                  {formatPrice(line.price, locale, item.currency)} —{' '}
                  {formatDurationMinutes(line.durationMinutes, locale)}
                  {line.added && ` — ${t('added')}`}
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
            <fieldset className="flex min-w-0 flex-col gap-2" data-testid="services-to-add">
              <legend className="mb-1 text-label font-bold text-text-strong">{t('pick')}</legend>
              {room != null && (
                <p className="text-helper text-text-secondary">
                  {t('room', { duration: formatDurationMinutes(room, locale) })}
                </p>
              )}
              {services.isPending ? (
                <SkeletonList rows={3} label={t('loading')} />
              ) : services.isError ? (
                <InlineAlert tone="danger" title={apiMessage('server.unexpected')} />
              ) : candidates.length === 0 ? (
                <p className="text-helper text-text-secondary">{t('none')}</p>
              ) : (
                candidates.map((s) => (
                  <RadioCard
                    key={s.serviceId}
                    name="serviceToAdd"
                    value={s.serviceId}
                    checked={serviceId === s.serviceId}
                    disabled={!s.fits}
                    onChange={() => setServiceId(s.serviceId)}
                    aside={
                      <span className="text-label font-bold text-text-primary">
                        {formatPrice(s.price, locale, s.currency)}
                      </span>
                    }
                  >
                    <span className="flex flex-col">
                      <span>{localizedName(locale, s.nameAr, s.nameEn ?? null)}</span>
                      <span className="text-helper text-text-secondary">
                        {s.fits
                          ? formatDurationMinutes(s.durationMinutes, locale)
                          : t('doesNotFit', { duration: formatDurationMinutes(s.durationMinutes, locale) })}
                      </span>
                    </span>
                  </RadioCard>
                ))
              )}
            </fieldset>
            <p className="text-helper text-text-tertiary">{t('addHint')}</p>
            <div className="flex gap-2">
              <Button size="sm" disabled={!serviceId} loading={busy === 'add'} onClick={() => void add()}>
                {t('confirmAdd')}
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  setAdding(false);
                  setServiceId('');
                }}
              >
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
