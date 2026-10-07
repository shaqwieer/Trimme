'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { PackageForm } from '@/components/catalog/PackageForm';
import { ServiceForm, type ServiceFormProfessional } from '@/components/catalog/ServiceForm';
import { Button } from '@/components/ui/Button';
import { Checkbox, SelectField, TextareaField, TextField } from '@/components/ui/inputs';
import { Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Schemas = components['schemas'];

function useNotice() {
  const apiMessage = useApiErrorMessage();
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string } | null>(null);
  return {
    notice,
    ok: (text: string) => setNotice({ tone: 'success', text }),
    fail: (error: unknown) =>
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      }),
    clear: () => setNotice(null),
  };
}

/**
 * Hide a service or package from customers (reason required) or show it again. Audited server-side (DV-S02).
 */
export function ModerationControl({
  kind,
  id,
  hidden,
  reason,
}: {
  kind: 'service' | 'package';
  id: string;
  hidden: boolean;
  reason?: string | null;
}) {
  const t = useTranslations('adminServices.detail');
  const tv = useTranslations('validation');
  const tCatalog = useTranslations('catalog');
  const router = useRouter();
  const { notice, ok, fail, clear } = useNotice();
  const [open, setOpen] = useState(false);
  const [text, setText] = useState('');
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  const submit = async (action: 'Hide' | 'Unhide') => {
    if (action === 'Hide' && text.trim().length < 5) {
      setError(tv('reasonRequired'));
      return;
    }
    clear();
    setPending(true);
    try {
      const body = { action, reason: action === 'Hide' ? text.trim() : null };
      if (kind === 'service') {
        ensureOk(
          await browserApi.POST('/api/v1/admin/services/{serviceId}/moderation', {
            params: { path: { serviceId: id } },
            body,
          }),
        );
      } else {
        ensureOk(
          await browserApi.POST('/api/v1/admin/packages/{packageId}/moderation', {
            params: { path: { packageId: id } },
            body,
          }),
        );
      }
      setOpen(false);
      setText('');
      ok(action === 'Hide' ? t('hidden') : t('unhidden'));
      router.refresh();
    } catch (e) {
      fail(e);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      {hidden && reason && (
        <p className="text-helper text-danger-700">{tCatalog('hiddenReason', { reason })}</p>
      )}
      {hidden ? (
        <Button
          size="sm"
          variant="secondary"
          icon="check"
          loading={pending}
          onClick={() => void submit('Unhide')}
          className="self-start"
        >
          {t('unhide')}
        </Button>
      ) : (
        <Button size="sm" variant="danger" icon="eyeOff" onClick={() => setOpen(true)} className="self-start">
          {t('hide')}
        </Button>
      )}
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title={t('hide')}
        description={t('moderationBody')}
        footer={
          <Button size="md" variant="dangerSolid" loading={pending} onClick={() => void submit('Hide')}>
            {t('hide')}
          </Button>
        }
      >
        <TextareaField
          label={t('reason')}
          value={text}
          maxLength={500}
          error={error}
          onChange={(e) => setText(e.target.value)}
        />
      </Dialog>
    </div>
  );
}

/** Support override of one shop service: the shop's form plus a required reason; audited before → after. */
export function ServiceOverride({
  service,
  categories,
}: {
  service: Schemas['AdminServiceResponse'];
  categories: Schemas['ServiceCategoryResponse'][];
}) {
  const t = useTranslations('adminServices.detail');
  return (
    <ServiceForm
      categories={categories}
      service={service}
      submitLabel={t('overrideSubmit')}
      reasonLabel={t('overrideReason')}
      onSubmitValues={async (body, reason) => {
        ensureOk(
          await browserApi.PUT('/api/v1/admin/services/{serviceId}/override', {
            params: { path: { serviceId: service.id } },
            body: { ...body, reason },
          }),
        );
      }}
    />
  );
}

/**
 * The admin builds a shop's catalogue for it (D-127): a new service for `shopId`, or an edit of `service`, each with the
 * shop's own price and duration and the shop's barbers who do it. A new service goes back to the shop's services tab.
 */
export function AdminServiceEditor({
  shopId,
  service,
  categories,
  professionals,
}: {
  shopId: string;
  service?: Schemas['AdminServiceResponse'];
  categories: Schemas['ServiceCategoryResponse'][];
  professionals: ServiceFormProfessional[];
}) {
  const t = useTranslations('adminServices.manage');
  const router = useRouter();
  return (
    <ServiceForm
      categories={categories}
      service={service}
      professionals={professionals}
      assignedIds={service?.professionalIds}
      submitLabel={service ? t('save') : t('create')}
      onSubmitValues={async (body, _reason, professionalIds) => {
        if (service) {
          ensureOk(
            await browserApi.PUT('/api/v1/admin/services/{serviceId}', {
              params: { path: { serviceId: service.id } },
              body: { ...body, professionalIds },
            }),
          );
          return;
        }
        ensureOk(
          await browserApi.POST('/api/v1/admin/shops/{shopId}/services', {
            params: { path: { shopId } },
            body: { ...body, version: 0, professionalIds },
          }),
        );
        router.push(`/admin/shops/${shopId}?tab=services`);
        router.refresh();
        return 'navigated';
      }}
    />
  );
}

/**
 * The admin builds a shop's packages for it (D-130): a new package for `shopId`, or an edit of `pkg`, of that shop's own
 * services with its own price and duration. A new package goes back to the shop's services tab.
 */
export function AdminPackageEditor({
  shopId,
  pkg,
  services,
}: {
  shopId: string;
  pkg?: Schemas['ShopPackageResponse'];
  services: Schemas['AdminServiceListItem'][];
}) {
  const router = useRouter();
  return (
    <PackageForm
      services={services}
      pkg={pkg}
      onSubmitValues={async (body) => {
        if (pkg) {
          ensureOk(
            await browserApi.PUT('/api/v1/admin/packages/{packageId}', {
              params: { path: { packageId: pkg.id } },
              body: { ...body, version: pkg.version },
            }),
          );
          return;
        }
        ensureOk(
          await browserApi.POST('/api/v1/admin/shops/{shopId}/packages', {
            params: { path: { shopId } },
            body: { ...body, version: 0 },
          }),
        );
        router.push(`/admin/shops/${shopId}?tab=services`);
        router.refresh();
        return 'navigated';
      }}
    />
  );
}

const ICONS = ['scissors', 'user', 'users', 'star', 'heart', 'layers', 'tag', 'coffee'] as const;

/** Create, edit and turn categories on/off. Categories carry no price or duration (spec §10). */
export function CategoryEditor({ category }: { category?: Schemas['AdminCategoryResponse'] }) {
  const t = useTranslations('adminServices.categories');
  const tv = useTranslations('validation');
  const tIcons = useTranslations('catalog.icons');
  const router = useRouter();
  const { notice, ok, fail, clear } = useNotice();
  const [open, setOpen] = useState(false);
  const [nameAr, setNameAr] = useState(category?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(category?.nameEn ?? '');
  const [icon, setIcon] = useState(category?.icon ?? 'scissors');
  const [order, setOrder] = useState(String(category?.displayOrder ?? 1));
  const [missing, setMissing] = useState(false);
  const [pending, setPending] = useState(false);

  const save = async () => {
    if (!nameAr.trim() || !nameEn.trim()) {
      setMissing(true);
      return;
    }
    clear();
    setPending(true);
    try {
      const body = { nameAr, nameEn, icon, displayOrder: Number(order) || 0 };
      ensureOk(
        category
          ? await browserApi.PUT('/api/v1/admin/service-categories/{categoryId}', {
              params: { path: { categoryId: category.id } },
              body,
            })
          : await browserApi.POST('/api/v1/admin/service-categories', { body }),
      );
      setOpen(false);
      ok(t('saved'));
      if (!category) {
        setNameAr('');
        setNameEn('');
      }
      router.refresh();
    } catch (e) {
      fail(e);
    } finally {
      setPending(false);
    }
  };

  const toggle = async () => {
    if (!category) return;
    clear();
    setPending(true);
    try {
      const path = { params: { path: { categoryId: category.id } } };
      ensureOk(
        category.isActive
          ? await browserApi.POST('/api/v1/admin/service-categories/{categoryId}/deactivate', path)
          : await browserApi.POST('/api/v1/admin/service-categories/{categoryId}/activate', path),
      );
      router.refresh();
    } catch (e) {
      fail(e);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Button
        size="sm"
        variant={category ? 'secondary' : 'primary'}
        icon={category ? 'edit' : 'plus'}
        onClick={() => setOpen(true)}
      >
        {category ? t('edit') : t('add')}
      </Button>
      {category && (
        <Button size="sm" variant="ghost" loading={pending && !open} onClick={() => void toggle()}>
          {category.isActive ? t('deactivate') : t('activate')}
        </Button>
      )}
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title={category ? t('editTitle') : t('add')}
        footer={
          <Button size="md" loading={pending} onClick={() => void save()}>
            {category ? t('save') : t('create')}
          </Button>
        }
      >
        <div className="flex flex-col gap-3">
          <TextField
            label={t('nameAr')}
            dir="rtl"
            value={nameAr}
            onChange={(e) => setNameAr(e.target.value)}
            error={missing && !nameAr.trim() ? tv('required') : undefined}
          />
          <TextField
            label={t('nameEn')}
            dir="ltr"
            value={nameEn}
            onChange={(e) => setNameEn(e.target.value)}
            error={missing && !nameEn.trim() ? tv('required') : undefined}
          />
          <SelectField label={t('icon')} value={icon} onChange={(e) => setIcon(e.target.value)}>
            {ICONS.map((name) => (
              <option key={name} value={name}>
                {tIcons(name)}
              </option>
            ))}
          </SelectField>
          <TextField
            label={t('order')}
            dir="ltr"
            inputMode="numeric"
            value={order}
            onChange={(e) => setOrder(e.target.value)}
          />
        </div>
      </Dialog>
    </div>
  );
}

/**
 * The professional's services: only their own shop's services, with that shop's prices (DV-S04). Admin-only
 * (Admin.Professionals.AssignServices); shops cannot assign (R-NEG-06).
 */
export function ProfessionalServicesForm({
  data,
  canAssign,
}: {
  data: Schemas['ProfessionalServicesResponse'];
  canAssign: boolean;
}) {
  const t = useTranslations('adminServices.professionalServices');
  const locale = useLocale();
  const lang = locale === 'en' ? 'en' : 'ar';
  const router = useRouter();
  const { notice, ok, fail, clear } = useNotice();
  const [selected, setSelected] = useState(() =>
    data.services.filter((s) => s.assigned).map((s) => s.serviceId),
  );
  const [pending, setPending] = useState(false);

  if (data.services.length === 0) return <p className="text-caption text-text-secondary">{t('empty')}</p>;

  const save = async () => {
    clear();
    setPending(true);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/professionals/{professionalId}/services', {
          params: { path: { professionalId: data.professionalId } },
          body: { serviceIds: selected },
        }),
      );
      ok(t('saved'));
      router.refresh();
    } catch (e) {
      fail(e);
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
            description={`${formatPrice(service.price, lang, service.currency)} · ${formatDurationMinutes(service.durationMinutes, lang)}${service.isActive ? '' : ` · ${t('inactive')}`}`}
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
