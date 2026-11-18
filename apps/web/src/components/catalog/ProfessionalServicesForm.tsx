'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

/**
 * The services a barber does: only their own shop's services, with that shop's prices (DV-S04). The platform admin
 * (`Admin.Professionals.AssignServices`) or, from the barber's page, the shop's owner (`Shop.Barbers.Manage`, D-146),
 * who also picks the barbers on each service's page (D-137).
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
  const [pending, setPending] = useState(false);

  if (data.services.length === 0)
    return (
      <p className="text-caption text-text-secondary">{scope === 'shop' ? t('emptyShop') : t('empty')}</p>
    );

  const save = async () => {
    setNotice(undefined);
    setPending(true);
    try {
      const request = {
        params: { path: { professionalId: data.professionalId } },
        body: { serviceIds: selected },
      };
      ensureOk(
        scope === 'shop'
          ? await browserApi.PUT('/api/v1/shop/professionals/{professionalId}/services', request)
          : await browserApi.PUT('/api/v1/admin/professionals/{professionalId}/services', request),
      );
      setNotice({ tone: 'success', text: t('saved') });
      router.refresh();
    } catch (e) {
      setNotice({
        tone: 'danger',
        text: apiMessage(e instanceof ApiError ? e : 'server.unexpected') ?? '',
      });
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-4" data-testid="professional-services">
      <p className="text-caption text-text-secondary">{t('body')}</p>
      <fieldset className="grid min-w-0 gap-3 sm:grid-cols-2" disabled={!canAssign}>
        <legend className="sr-only">{t('title')}</legend>
        {data.services.map((service) => (
          <Checkbox
            key={service.serviceId}
            label={localizedName(locale, service.nameAr, service.nameEn)}
            description={`${formatPrice(service.price, lang, service.currency)} — ${formatDurationMinutes(service.durationMinutes, lang)}${service.isActive ? '' : ` — ${t('inactive')}`}`}
            checked={selected.includes(service.serviceId)}
            onChange={(e) =>
              setSelected((current) =>
                e.target.checked
                  ? [...current, service.serviceId]
                  : current.filter((id) => id !== service.serviceId),
              )
            }
          />
        ))}
      </fieldset>
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      {canAssign && (
        <Button size="md" icon="check" loading={pending} onClick={() => void save()} className="self-start">
          {t('save')}
        </Button>
      )}
    </div>
  );
}
