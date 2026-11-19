'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { TextField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { codeToMessageKey } from '@/lib/forms/problem';
import { localizedName } from '@/lib/i18n/localized';

type Section = components['schemas']['ServiceSectionResponse'];
type Body = components['schemas']['ServiceSectionRequest'];

/** Whose sections: the owner's own shop (from the claims) or, for the admin, one shop. */
export type SectionScope = { kind: 'shop' } | { kind: 'admin'; shopId: string };

/** The longest section name (the API's `ServiceSection.MaxNameLength`). */
const MAX_NAME = 60;

const api = {
  create: async (scope: SectionScope, body: Body) =>
    ensureOk(
      scope.kind === 'shop'
        ? await browserApi.POST('/api/v1/shop/service-sections', { body })
        : await browserApi.POST('/api/v1/admin/shops/{shopId}/service-sections', {
            params: { path: { shopId: scope.shopId } },
            body,
          }),
    ),
  rename: async (scope: SectionScope, sectionId: string, body: Body) =>
    ensureOk(
      scope.kind === 'shop'
        ? await browserApi.PUT('/api/v1/shop/service-sections/{sectionId}', {
            params: { path: { sectionId } },
            body,
          })
        : await browserApi.PUT('/api/v1/admin/shops/{shopId}/service-sections/{sectionId}', {
            params: { path: { shopId: scope.shopId, sectionId } },
            body,
          }),
    ),
  remove: async (scope: SectionScope, sectionId: string) =>
    ensureOk(
      scope.kind === 'shop'
        ? await browserApi.DELETE('/api/v1/shop/service-sections/{sectionId}', {
            params: { path: { sectionId } },
          })
        : await browserApi.DELETE('/api/v1/admin/shops/{shopId}/service-sections/{sectionId}', {
            params: { path: { shopId: scope.shopId, sectionId } },
          }),
    ),
  order: async (scope: SectionScope, orderedIds: string[]) =>
    ensureOk(
      scope.kind === 'shop'
        ? await browserApi.PUT('/api/v1/shop/service-sections/order', { body: { orderedIds } })
        : await browserApi.PUT('/api/v1/admin/shops/{shopId}/service-sections/order', {
            params: { path: { shopId: scope.shopId } },
            body: { orderedIds },
          }),
    ),
};

/**
 * The shop's own sections (D-149, the owner's item 1): the tabs its services sit under on the shop page and in the
 * booking, in this order. The owner (or the admin, for one shop) adds, renames, orders and deletes them; a deleted
 * section's services stay and show under «أخرى». A service is placed in a section on its own form.
 */
export function ServiceSections({
  scope,
  sections,
  canManage = true,
}: {
  scope: SectionScope;
  sections: Section[];
  canManage?: boolean;
}) {
  const t = useTranslations('shopServices.sections');
  const tv = useTranslations('validation');
  const locale = useLocale();
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [busy, setBusy] = useState<string | null>(null);
  const [failure, setFailure] = useState<string>();
  const [announcement, setAnnouncement] = useState('');
  const [editing, setEditing] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<Section | null>(null);

  const nameOf = (s: Section) => localizedName(locale, s.nameAr, s.nameEn);

  /** Runs one change; a validation answer becomes the first field's message, anything else the API's message. */
  const run = async (key: string, action: () => Promise<unknown>) => {
    setBusy(key);
    setFailure(undefined);
    try {
      await action();
      router.refresh();
      return true;
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        const code = Object.values(error.fieldErrors)[0]?.[0];
        const key = codeToMessageKey(code);
        setFailure(key === 'tooLong' ? tv('tooLong', { max: MAX_NAME }) : tv(key as 'invalid'));
      } else {
        setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      }
      return false;
    } finally {
      setBusy(null);
    }
  };

  const move = async (index: number, delta: -1 | 1) => {
    const target = index + delta;
    if (target < 0 || target >= sections.length) return;
    const next = sections.map((s) => s.id);
    [next[index], next[target]] = [next[target]!, next[index]!];
    const moved = sections[index]!;
    if (await run(`move-${moved.id}`, () => api.order(scope, next)))
      setAnnouncement(t('moved', { name: nameOf(moved), position: target + 1 }));
  };

  return (
    <div className="flex flex-col gap-3" data-testid="service-sections">
      <p className="text-caption text-text-secondary">{t('body')}</p>
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
      {failure && <InlineAlert tone="danger" title={failure} />}
      {sections.length === 0 ? (
        <p className="text-caption text-text-tertiary">{t('empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border-row">
          {sections.map((section, index) => (
            <li
              key={section.id}
              className="flex flex-col gap-3 py-3"
              data-testid={`section-row-${section.id}`}
            >
              {editing === section.id ? (
                <SectionNameForm
                  initial={section}
                  submitLabel={t('save')}
                  busy={busy === `rename-${section.id}`}
                  onCancel={() => setEditing(null)}
                  onSubmit={(body) =>
                    void run(`rename-${section.id}`, () => api.rename(scope, section.id, body)).then(
                      (ok) => ok && setEditing(null),
                    )
                  }
                />
              ) : (
                <div className="flex flex-col gap-2 md:flex-row md:items-center">
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="font-bold text-text-primary">{nameOf(section)}</span>
                    <span className="text-caption text-text-secondary">
                      {t('count', { count: section.serviceCount })}
                    </span>
                  </div>
                  {canManage && (
                    <div className="flex flex-wrap items-center gap-2">
                      <IconButton
                        icon="chevD"
                        className="rotate-180"
                        label={t('moveUp', { name: nameOf(section) })}
                        disabled={index === 0 || busy !== null}
                        onClick={() => void move(index, -1)}
                      />
                      <IconButton
                        icon="chevD"
                        label={t('moveDown', { name: nameOf(section) })}
                        disabled={index === sections.length - 1 || busy !== null}
                        onClick={() => void move(index, 1)}
                      />
                      <Button
                        variant="secondary"
                        size="sm"
                        icon="edit"
                        aria-label={t('renameItem', { name: nameOf(section) })}
                        disabled={busy !== null}
                        onClick={() => setEditing(section.id)}
                      >
                        {t('rename')}
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        icon="trash"
                        aria-label={t('deleteItem', { name: nameOf(section) })}
                        disabled={busy !== null}
                        onClick={() => setConfirm(section)}
                      >
                        {t('delete')}
                      </Button>
                    </div>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {canManage && (
        <SectionNameForm
          submitLabel={t('add')}
          busy={busy === 'create'}
          resetOnSubmit
          onSubmit={(body) => run('create', () => api.create(scope, body))}
        />
      )}
      <ConfirmDialog
        open={confirm !== null}
        onOpenChange={(open) => !open && setConfirm(null)}
        title={confirm ? t('deleteTitle', { name: nameOf(confirm) }) : ''}
        body={t('deleteBody')}
        confirmLabel={t('delete')}
        loading={busy === 'delete'}
        onConfirm={() => {
          if (!confirm) return;
          void run('delete', () => api.remove(scope, confirm.id)).then(() => setConfirm(null));
        }}
      />
    </div>
  );
}

/** A section's Arabic name (required) and English name (optional): adding one, or renaming one in place. */
function SectionNameForm({
  initial,
  submitLabel,
  busy,
  onSubmit,
  onCancel,
  resetOnSubmit = false,
}: {
  initial?: Section;
  submitLabel: string;
  busy: boolean;
  onSubmit: (body: Body) => Promise<boolean> | void;
  onCancel?: () => void;
  resetOnSubmit?: boolean;
}) {
  const t = useTranslations('shopServices.sections');
  const tv = useTranslations('validation');
  const [nameAr, setNameAr] = useState(initial?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(initial?.nameEn ?? '');
  const [error, setError] = useState<string>();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!nameAr.trim()) {
      setError(tv('required'));
      return;
    }
    setError(undefined);
    const done = await onSubmit({ nameAr: nameAr.trim(), nameEn: nameEn.trim() || null });
    if (done && resetOnSubmit) {
      setNameAr('');
      setNameEn('');
    }
  };

  return (
    <form
      method="post"
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-3 md:flex-row md:items-start"
      aria-label={initial ? t('renameItem', { name: initial.nameAr }) : t('add')}
    >
      <TextField
        label={t('nameAr')}
        value={nameAr}
        onChange={(e) => setNameAr(e.target.value)}
        maxLength={MAX_NAME}
        dir="rtl"
        error={error}
        className="md:flex-1"
      />
      <TextField
        label={t('nameEn')}
        value={nameEn}
        onChange={(e) => setNameEn(e.target.value)}
        maxLength={MAX_NAME}
        dir="ltr"
        optional
        className="md:flex-1"
      />
      <div className="flex gap-2 md:pt-7">
        <Button type="submit" size="md" icon={initial ? 'check' : 'plus'} loading={busy}>
          {submitLabel}
        </Button>
        {onCancel && (
          <Button type="button" size="md" variant="ghost" onClick={onCancel}>
            {t('cancel')}
          </Button>
        )}
      </div>
    </form>
  );
}
