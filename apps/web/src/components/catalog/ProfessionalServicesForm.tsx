'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Checkbox, SelectField } from '@/components/ui/inputs';
import { useSaveGroup } from '@/components/ui/SaveGroup';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { DURATION_OPTIONS } from '@/lib/forms/price';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

/**
 * The services a barber does: only their own shop's services, with that shop's prices (DV-S04), and how long each takes
 * this barber (D-149: a haircut takes Ahmed 30 minutes and Mohamed 45; left at the standard length unless set). The
 * platform admin (`Admin.Professionals.AssignServices`) or, from the barber's page, the shop's owner
 * (`Shop.Barbers.Manage`, D-146), who also picks the barbers on each service's page (D-137). Inside a {@link SaveGroup}
 * (the owner's barber page, D-155) it has no button of its own: the page saves it together with the profile.
 */
export function ProfessionalServicesForm({
  scope,
  data,
  canAssign,
}: {
  scope: 'admin' | 'shop';
  data: components['schemas']['ProfessionalServicesResponse'];
  canAssign: boolean;
}) {
  const t = useTranslations('adminServices.professionalServices');
  const locale = useLocale();
  const lang = asLocale(locale);
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string }>();
  const [selected, setSelected] = useState(() =>
    data.services.filter((s) => s.assigned).map((s) => s.serviceId),
  );
  // The barber's own length per service ('' = the service's standard length).
  const [own, setOwn] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      data.services.map((s) => [s.serviceId, s.ownDurationMinutes ? String(s.ownDurationMinutes) : '']),
    ),
  );
  const [pending, setPending] = useState(false);
  // What was saved last, to tell whether anything changed since (D-155).
  const [saved, setSaved] = useState(() => snapshot(selected, own));
  const dirty = snapshot(selected, own) !== saved;

  const save = async (): Promise<boolean> => {
    setNotice(undefined);
    setPending(true);
    try {
      const request = {
        params: { path: { professionalId: data.professionalId } },
        body: {
          serviceIds: null,
          services: selected.map((serviceId) => ({
            serviceId,
            durationMinutes: own[serviceId] ? Number(own[serviceId]) : null,
          })),
        },
      };
      ensureOk(
        scope === 'shop'
          ? await browserApi.PUT('/api/v1/shop/professionals/{professionalId}/services', request)
          : await browserApi.PUT('/api/v1/admin/professionals/{professionalId}/services', request),
      );
      setSaved(snapshot(selected, own));
      setNotice({ tone: 'success', text: t('saved') });
      router.refresh();
      return true;
    } catch (e) {
      setNotice({
        tone: 'danger',
        text: apiMessage(e instanceof ApiError ? e : 'server.unexpected') ?? '',
      });
      return false;
    } finally {
      setPending(false);
    }
  };
  const grouped = useSaveGroup('services', dirty && canAssign, save);

  if (data.services.length === 0)
    return (
      <p className="text-caption text-text-secondary">{scope === 'shop' ? t('emptyShop') : t('empty')}</p>
    );

  return (
    <div className="flex flex-col gap-4" data-testid="professional-services">
      <p className="text-caption text-text-secondary">{t('body')}</p>
      <fieldset className="flex min-w-0 flex-col divide-y divide-border-row" disabled={!canAssign}>
        <legend className="sr-only">{t('title')}</legend>
        {data.services.map((service) => {
          const name = localizedName(locale, service.nameAr, service.nameEn);
          const checked = selected.includes(service.serviceId);
          return (
            <div
              key={service.serviceId}
              className="flex flex-col gap-2 py-3 md:flex-row md:items-center md:justify-between"
            >
              <Checkbox
                label={name}
                description={`${formatPrice(service.price, lang, service.currency)} — ${formatDurationMinutes(service.durationMinutes, lang)}${service.isActive ? '' : ` — ${t('inactive')}`}`}
                checked={checked}
                onChange={(e) =>
                  setSelected((current) =>
                    e.target.checked
                      ? [...current, service.serviceId]
                      : current.filter((id) => id !== service.serviceId),
                  )
                }
              />
              {checked && (
                <SelectField
                  label={t('ownDuration', { service: name })}
                  name={`duration-${service.serviceId}`}
                  value={own[service.serviceId] ?? ''}
                  onChange={(e) => setOwn((current) => ({ ...current, [service.serviceId]: e.target.value }))}
                  className="md:w-64"
                >
                  <option value="">
                    {t('standardDuration', {
                      duration: formatDurationMinutes(service.durationMinutes, lang),
                    })}
                  </option>
                  {DURATION_OPTIONS.map((minutes) => (
                    <option key={minutes} value={minutes}>
                      {formatDurationMinutes(minutes, lang)}
                    </option>
                  ))}
                </SelectField>
              )}
            </div>
          );
        })}
      </fieldset>
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      {canAssign && !grouped && (
        <Button size="md" icon="check" loading={pending} onClick={() => void save()} className="self-start">
          {t('save')}
        </Button>
      )}
    </div>
  );
}

/** The assignment as one comparable string: the services ticked and each one's own length. */
function snapshot(selected: string[], own: Record<string, string>): string {
  return [...selected]
    .sort()
    .map((id) => `${id}:${own[id] ?? ''}`)
    .join(',');
}
