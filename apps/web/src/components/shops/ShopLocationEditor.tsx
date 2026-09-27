'use client';

import { useTranslations } from 'next-intl';
import { LocationPicker } from '@/components/map/LocationPicker';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { formatDate } from '@/lib/i18n/format';
import { formatCoordinates } from '@/lib/map/config';
import { type EditorMode, type ShopLocation, shopApi } from './shopApi';

type ShopLocationEditorProps = {
  mode: EditorMode;
  location: ShopLocation | null;
  /** False when the admin policy (or a missing permission) keeps the location read-only for this user. */
  canEdit: boolean;
  locale: 'ar' | 'en';
};

/** The pin picker for the admin (any shop) or the shop itself (only when its policy opens the location). */
export function ShopLocationEditor({ mode, location, canEdit, locale }: ShopLocationEditorProps) {
  const t = useTranslations('locationPicker');
  const tSource = useTranslations('locationSource');
  const router = useRouter();

  if (!canEdit) {
    return (
      <div className="flex flex-col gap-3" data-testid="location-readonly">
        <InlineAlert tone="info" title={t('readOnlyTitle')}>
          {t('readOnlyBody')}
        </InlineAlert>
        {location ? (
          <dl className="grid gap-3 text-caption sm:grid-cols-2">
            <div>
              <dt className="text-helper text-text-tertiary">{t('resolved')}</dt>
              <dd className="font-semibold text-text-primary">
                {location.formattedAddress ?? location.district ?? '—'}
              </dd>
            </div>
            <div>
              <dt className="text-helper text-text-tertiary">{t('coordinates')}</dt>
              <dd dir="ltr" className="font-latin font-semibold text-text-primary">
                {formatCoordinates({ lat: location.latitude, lng: location.longitude })}
              </dd>
            </div>
          </dl>
        ) : (
          <p className="text-caption text-text-secondary">{t('notSet')}</p>
        )}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      {location && (
        <p className="text-helper text-text-tertiary">
          {t('source')}: {tSource(location.source)} · {t('confirmedAt')}:{' '}
          {formatDate(location.confirmedAt, locale)}
        </p>
      )}
      <LocationPicker
        scope={mode.kind}
        initial={
          location
            ? {
                latitude: location.latitude,
                longitude: location.longitude,
                addressLine: location.addressLine,
                district: location.district,
                city: location.city,
                formattedAddress: location.formattedAddress,
                source: location.source,
              }
            : null
        }
        onSave={async (picked) => {
          await shopApi(mode).saveLocation(picked);
          router.refresh();
        }}
      />
    </div>
  );
}
