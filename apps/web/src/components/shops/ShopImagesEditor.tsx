'use client';

import Image from 'next/image';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { InlineAlert } from '@/components/ui/states';
import { UploadDropZone } from '@/components/ui/UploadDropZone';
import { useRouter } from '@/i18n/navigation';
import { useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { codeToMessageKey } from '@/lib/forms/problem';
import { LockNote } from './ShopProfileEditor';
import {
  type EditorMode,
  type ImageSlot,
  type ShopProfileData,
  type ShopProfileField,
  shopApi,
} from './shopApi';

export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const MAX_GALLERY = 12;

type ShopImagesEditorProps = {
  mode: EditorMode;
  profile: Pick<ShopProfileData, 'logoUrl' | 'coverUrl' | 'gallery' | 'editableFields'>;
  canEdit: boolean;
};

/**
 * Logo, cover and gallery (s-settings: logo tile and 1600×900 cover drop zone). Images are stored in the database
 * (D-064) and served from `/api/v1/media/{id}`; every change saves immediately and the page refreshes.
 */
export function ShopImagesEditor({ mode, profile, canEdit }: ShopImagesEditorProps) {
  const t = useTranslations('shopProfile.images');
  const tv = useTranslations('validation') as unknown as (key: string) => string;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string } | null>(null);
  const api = shopApi(mode);
  const editable = (field: ShopProfileField) =>
    canEdit && (mode.kind === 'admin' || profile.editableFields.includes(field));

  const run = async (key: string, action: () => Promise<unknown>, success: string) => {
    setBusy(key);
    setNotice(null);
    try {
      await action();
      setNotice({ tone: 'success', text: success });
      router.refresh();
    } catch (error) {
      const fileCode = error instanceof ApiError ? error.fieldErrors.file?.[0] : undefined;
      setNotice({
        tone: 'danger',
        text: fileCode
          ? tv(codeToMessageKey(fileCode))
          : (apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? ''),
      });
    } finally {
      setBusy(null);
    }
  };

  const single = (
    slot: ImageSlot,
    field: ShopProfileField,
    url: string | null,
    width: number,
    height: number,
  ) => (
    <section className="flex flex-col gap-2" aria-labelledby={`${slot}-title`}>
      <h3 id={`${slot}-title`} className="flex items-center gap-2 text-label font-bold text-text-strong">
        {t(slot)} {mode.kind === 'shop' && !editable(field) && <LockNote />}
      </h3>
      <div className="flex flex-wrap items-start gap-4">
        {url ? (
          <Image
            src={url}
            alt={t(slot)}
            width={slot === 'logo' ? 120 : 240}
            height={slot === 'logo' ? 120 : 135}
            unoptimized
            data-testid={`${slot}-image`}
            className="rounded-card border border-border object-cover"
            style={{ width: slot === 'logo' ? 120 : 240, height: slot === 'logo' ? 120 : 135 }}
          />
        ) : (
          <span className="flex size-[120px] items-center justify-center rounded-card border border-dashed border-border-dashed text-helper text-text-tertiary">
            {t('empty')}
          </span>
        )}
        {editable(field) && (
          <div className="flex min-w-[240px] flex-1 flex-col gap-2">
            <UploadDropZone
              label={t(slot)}
              prompt={slot === 'logo' ? t('uploadLogo') : t('uploadCover')}
              accept={IMAGE_TYPES}
              maxBytes={MAX_IMAGE_BYTES}
              recommended={{ width, height }}
              onFileSelected={(file) => void run(slot, () => api.upload(slot, file), t('uploaded'))}
            />
            {url && (
              <Button
                variant="ghost"
                size="sm"
                icon="trash"
                loading={busy === `${slot}-remove`}
                onClick={() => void run(`${slot}-remove`, () => api.remove(slot), t('removed'))}
                className="self-start"
              >
                {t('remove')}
              </Button>
            )}
          </div>
        )}
      </div>
    </section>
  );

  return (
    <div className="flex flex-col gap-6" data-testid="shop-images">
      {busy && !busy.endsWith('-remove') && (
        <p role="status" className="text-helper text-text-secondary">
          {t('uploading')}
        </p>
      )}
      {single('logo', 'Logo', profile.logoUrl, 512, 512)}
      {single('cover', 'Cover', profile.coverUrl, 1600, 900)}

      <section className="flex flex-col gap-2" aria-labelledby="gallery-title">
        <h3 id="gallery-title" className="flex items-center gap-2 text-label font-bold text-text-strong">
          {t('gallery')} {mode.kind === 'shop' && !editable('Gallery') && <LockNote />}
        </h3>
        <p className="text-helper text-text-tertiary">{t('galleryHint')}</p>
        {profile.gallery.length === 0 ? (
          <p className="text-caption text-text-secondary">{t('galleryEmpty')}</p>
        ) : (
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {profile.gallery.map((image, index) => (
              <li key={image.id} className="flex flex-col gap-1">
                <Image
                  src={image.url}
                  alt={t('galleryImage', { index: index + 1 })}
                  width={320}
                  height={200}
                  unoptimized
                  className="aspect-[16/10] w-full rounded-card border border-border object-cover"
                />
                {editable('Gallery') && (
                  <Button
                    variant="ghost"
                    size="xs"
                    icon="trash"
                    aria-label={t('removeImage', { index: index + 1 })}
                    loading={busy === image.id}
                    onClick={() => void run(image.id, () => api.removeFromGallery(image.id), t('removed'))}
                    className="self-start"
                  >
                    {t('remove')}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
        {editable('Gallery') && profile.gallery.length < MAX_GALLERY && (
          <UploadDropZone
            label={t('gallery')}
            prompt={t('uploadGallery')}
            accept={IMAGE_TYPES}
            maxBytes={MAX_IMAGE_BYTES}
            recommended={{ width: 1200, height: 800 }}
            onFileSelected={(file) => void run('gallery', () => api.addToGallery(file), t('uploaded'))}
          />
        )}
      </section>

      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
    </div>
  );
}
