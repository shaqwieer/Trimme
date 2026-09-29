'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Checkbox, SelectField, TextField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

export type RoleOption = { name: string; label: string };

/**
 * A staff member's roles and access (DV-A15, D-106). The API refuses your own account, giving or taking SuperAdmin
 * unless you are one, removing the last SuperAdmin, and any role with permissions you lack; the refusal is shown.
 */
export function StaffActions({
  userId,
  name,
  current,
  roles,
  disabled,
  self,
}: {
  userId: string;
  name: string;
  current: string[];
  roles: RoleOption[];
  disabled: boolean;
  self: boolean;
}) {
  const t = useTranslations('adminRoles.staff');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(() => new Set(current));
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  const fail = (error: unknown) =>
    setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));

  const saveRoles = async () => {
    setBusy(true);
    setFailure(undefined);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/staff/{userId}/roles', {
          params: { path: { userId } },
          body: { roles: [...selected] },
        }),
      );
      setEditing(false);
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setBusy(false);
    }
  };

  const toggleAccess = async () => {
    setBusy(true);
    setFailure(undefined);
    try {
      const path = { params: { path: { userId } }, body: { reason: null } };
      ensureOk(
        disabled
          ? await browserApi.POST('/api/v1/admin/staff/{userId}/enable', path)
          : await browserApi.POST('/api/v1/admin/staff/{userId}/disable', path),
      );
      setConfirming(false);
      router.refresh();
    } catch (error) {
      setConfirming(false);
      fail(error);
    } finally {
      setBusy(false);
    }
  };

  if (self) return <span className="text-helper text-text-tertiary">{t('you')}</span>;
  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex flex-wrap justify-end gap-2">
        <Button
          size="sm"
          variant="secondary"
          icon="edit"
          onClick={() => setEditing(true)}
          aria-label={t('editFor', { name })}
        >
          {t('edit')}
        </Button>
        <Button
          size="sm"
          variant={disabled ? 'outline' : 'danger'}
          onClick={() => setConfirming(true)}
          aria-label={t(disabled ? 'enableFor' : 'disableFor', { name })}
        >
          {disabled ? t('enable') : t('disable')}
        </Button>
      </div>
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Dialog
        open={editing}
        onOpenChange={setEditing}
        title={t('editTitle', { name })}
        description={t('editBody')}
        footer={
          <Button size="md" loading={busy} disabled={selected.size === 0} onClick={() => void saveRoles()}>
            {t('save')}
          </Button>
        }
      >
        <fieldset className="flex flex-col gap-2">
          <legend className="sr-only">{t('roles')}</legend>
          {roles.map((role) => (
            <Checkbox
              key={role.name}
              label={role.label}
              checked={selected.has(role.name)}
              onChange={(event) =>
                setSelected((set) => {
                  const next = new Set(set);
                  if (event.target.checked) next.add(role.name);
                  else next.delete(role.name);
                  return next;
                })
              }
            />
          ))}
        </fieldset>
      </Dialog>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        tone={disabled ? 'default' : 'danger'}
        title={t(disabled ? 'enableTitle' : 'disableTitle', { name })}
        body={t(disabled ? 'enableBody' : 'disableBody')}
        confirmLabel={disabled ? t('enable') : t('disable')}
        loading={busy}
        onConfirm={() => void toggleAccess()}
      />
    </div>
  );
}

/** Invites a platform staff member by email with one admin role (the existing invitation flow, D-055). */
export function InviteStaffForm({ roles }: { roles: RoleOption[] }) {
  const t = useTranslations('adminRoles.invite');
  const tv = useTranslations('validation');
  const apiMessage = useApiErrorMessage();
  const [email, setEmail] = useState('');
  const [role, setRole] = useState(roles[0]?.name ?? '');
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState<string>();
  const [failure, setFailure] = useState<string>();

  const invite = async () => {
    if (!/^\S+@\S+\.\S+$/.test(email.trim())) {
      setFailure(tv('emailInvalid'));
      return;
    }
    setBusy(true);
    setFailure(undefined);
    setSent(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/staff/invitations', {
          body: { email: email.trim(), role, locale: null },
        }),
      );
      setSent(t('sent', { email: email.trim() }));
      setEmail('');
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="grid gap-3 sm:grid-cols-[2fr_1fr_auto] sm:items-end">
        <TextField
          label={t('email')}
          type="email"
          dir="ltr"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />
        <SelectField label={t('role')} value={role} onChange={(event) => setRole(event.target.value)}>
          {roles.map((r) => (
            <option key={r.name} value={r.name}>
              {r.label}
            </option>
          ))}
        </SelectField>
        <Button size="md" icon="plus" loading={busy} onClick={() => void invite()}>
          {t('submit')}
        </Button>
      </div>
      {sent && <InlineAlert tone="success" title={sent} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
    </div>
  );
}
