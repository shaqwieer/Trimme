'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { TextareaField, TextField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { codeToMessageKey } from '@/lib/forms/problem';
import { type AppLocale, formatPrice } from '@/lib/i18n/format';
import { addDays, type LocalDate } from '@/lib/i18n/localDate';
import { longDate } from './periods';

type AddOn = components['schemas']['LoyaltyAddOnResponse'];
type Price = components['schemas']['LoyaltyAddOnPriceResponse'];

const NOTES_MAX = 500;

/**
 * The shop's loyalty add-on on the admin's subscription tab (D-149, the client's 6-2): on until a date, at the price in
 * force (recorded with the period); switched on again while on, the new period starts after the current end. Switching
 * it off stops collecting and using points from today; the points are kept. Each period is listed with its price version.
 * `Admin.Loyalty.Manage` switches it; the price is the SuperAdmin's (subscription plans page). No payment in v1.
 */
export function LoyaltyAddOnPanel({
  shopId,
  addOn,
  price,
  today,
  canManage,
}: {
  shopId: string;
  addOn: AddOn;
  /** The price in force; `undefined` when this admin cannot read prices (the server records it either way). */
  price: Price | null | undefined;
  today: LocalDate;
  canManage: boolean;
}) {
  const t = useTranslations('adminSubscriptions.addOn');
  const tv = useTranslations('validation');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [endDate, setEndDate] = useState('');
  const [notes, setNotes] = useState('');
  const [errors, setErrors] = useState<{ endDate?: string; notes?: string }>({});
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string }>();
  const [pending, setPending] = useState<'on' | 'stop' | null>(null);
  const [confirm, setConfirm] = useState(false);
  const extending = addOn.active && Boolean(addOn.endDate);
  const min = extending && addOn.endDate && addOn.endDate >= today ? addDays(addOn.endDate, 1) : today;

  const fail = (error: unknown) => {
    if (error instanceof ApiError && error.isValidation) {
      const field = (name: string) => {
        const code = error.fieldErrors[name]?.[0];
        return code ? tv(codeToMessageKey(code) as 'generic') : undefined;
      };
      setErrors({ endDate: field('endDate'), notes: field('notes') });
    } else
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      });
  };

  const switchOn = async (event: FormEvent) => {
    event.preventDefault();
    setNotice(undefined);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(endDate) || endDate < min) {
      setErrors({ endDate: tv('outOfRange') });
      return;
    }
    setErrors({});
    setPending('on');
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/loyalty-add-on', {
          params: { path: { shopId } },
          body: { endDate, notes: notes.trim() || null },
        }),
      );
      setEndDate('');
      setNotes('');
      setNotice({ tone: 'success', text: t('switched') });
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setPending(null);
    }
  };

  const stop = async () => {
    setNotice(undefined);
    setPending('stop');
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/loyalty-add-on/stop', {
          params: { path: { shopId } },
        }),
      );
      setConfirm(false);
      setNotice({ tone: 'success', text: t('stopped') });
      router.refresh();
    } catch (error) {
      setConfirm(false);
      fail(error);
    } finally {
      setPending(null);
    }
  };

  return (
    <div className="flex flex-col gap-4" data-testid="loyalty-add-on">
      <p className="text-caption text-text-secondary">{t('intro')}</p>
      <div className="flex flex-wrap items-center gap-3">
        <Badge tone={addOn.active ? 'success' : 'neutral'}>
          {addOn.active && addOn.endDate
            ? t('active', { date: longDate(addOn.endDate, locale) })
            : t('inactive')}
        </Badge>
        {price !== undefined && (
          <span className="text-caption text-text-secondary">
            {price ? t('price', { price: formatPrice(price.amount, locale, price.currency) }) : t('noPrice')}
          </span>
        )}
      </div>

      {canManage && (
        <form
          method="post"
          noValidate
          onSubmit={(e) => void switchOn(e)}
          className="flex flex-col gap-3"
          data-testid="loyalty-add-on-form"
        >
          <TextField
            label={t('endDate')}
            type="date"
            name="endDate"
            value={endDate}
            min={min}
            onChange={(e) => setEndDate(e.target.value)}
            dir="ltr"
            helper={t('endDateHelper')}
            error={errors.endDate}
          />
          <TextareaField
            label={t('notes')}
            name="notes"
            value={notes}
            maxLength={NOTES_MAX}
            rows={2}
            onChange={(e) => setNotes(e.target.value)}
            error={errors.notes}
          />
          <div className="flex flex-wrap gap-2">
            <Button type="submit" size="sm" icon="check" loading={pending === 'on'}>
              {extending ? t('extend') : t('switchOn')}
            </Button>
            {addOn.active && (
              <Button type="button" size="sm" variant="outline" onClick={() => setConfirm(true)}>
                {t('stop')}
              </Button>
            )}
          </div>
        </form>
      )}
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}

      <div className="flex flex-col gap-2">
        <h3 className="text-label font-bold text-text-strong">{t('periods')}</h3>
        {addOn.periods.length === 0 ? (
          <p className="text-caption text-text-secondary">{t('noPeriods')}</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border-row" data-testid="loyalty-add-on-periods">
            {addOn.periods.map((period) => (
              <li key={period.id} className="flex flex-col gap-0.5 py-2.5">
                <span className="text-caption font-semibold text-text-primary">
                  {t('period', {
                    start: longDate(period.startDate, locale),
                    end: longDate(period.lastDay, locale),
                  })}
                </span>
                <span className="text-helper text-text-secondary">
                  {t('periodPrice', {
                    price: formatPrice(period.amount, locale, period.currency),
                    version: period.priceVersionNumber,
                  })}
                  {period.stoppedOn && ` — ${t('stoppedOn', { date: longDate(period.stoppedOn, locale) })}`}
                </span>
                {period.notes && <span className="text-helper text-text-tertiary">{period.notes}</span>}
              </li>
            ))}
          </ul>
        )}
      </div>

      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title={t('stopTitle')}
        body={t('stopBody')}
        confirmLabel={t('stop')}
        loading={pending === 'stop'}
        onConfirm={() => void stop()}
      />
    </div>
  );
}
