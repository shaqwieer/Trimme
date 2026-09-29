'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Checkbox, TextField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { permissionGroups, permissionKey, ungrantable } from '@/lib/admin/admin';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { codeToMessageKey } from '@/lib/forms/problem';

type RoleProps = { id: string; name: string; managed: boolean; seed: boolean; permissions: string[] };

/**
 * One admin role's permissions (a-roles matrix made editable, DV-A15, D-106). Managed roles are read-only. An admin
 * can tick only permissions they hold themselves (the API refuses anything else with `role.escalation`), SuperAdmin
 * permissions never appear, and seed roles keep their names. Every save is audited.
 */
export function RoleEditor({
  role,
  catalogue,
  held,
  canManage,
}: {
  role: RoleProps;
  catalogue: string[];
  held: string[];
  canManage: boolean;
}) {
  const t = useTranslations('adminRoles.editor');
  const tCodes = useTranslations('permissions.codes');
  const tAreas = useTranslations('permissions.areas');
  const tv = useTranslations('validation');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const editable = canManage && !role.managed;
  const [selected, setSelected] = useState<Set<string>>(() => new Set(role.permissions));
  const [name, setName] = useState(role.name);
  const [nameError, setNameError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<string>();
  const [failure, setFailure] = useState<string>();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const grantable = catalogue.filter((code) => !code.startsWith('SuperAdmin.'));
  const blocked = new Set(ungrantable(grantable, role.permissions, held));

  const fail = (error: unknown) =>
    setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));

  const toggle = (code: string, on: boolean) =>
    setSelected((current) => {
      const next = new Set(current);
      if (on) next.add(code);
      else next.delete(code);
      return next;
    });

  const save = async () => {
    setBusy(true);
    setSaved(undefined);
    setFailure(undefined);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/roles/{roleId}/permissions', {
          params: { path: { roleId: role.id } },
          body: { permissions: grantable.filter((code) => selected.has(code)) },
        }),
      );
      setSaved(t('saved'));
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setBusy(false);
    }
  };

  const rename = async () => {
    setBusy(true);
    setNameError(undefined);
    setFailure(undefined);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/roles/{roleId}', {
          params: { path: { roleId: role.id } },
          body: { name: name.trim() },
        }),
      );
      setSaved(t('renamed'));
      router.refresh();
    } catch (error) {
      const code = error instanceof ApiError ? error.fieldErrors.name?.[0] : undefined;
      if (code) setNameError(tv(codeToMessageKey(code) as 'generic'));
      else fail(error);
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      ensureOk(
        await browserApi.DELETE('/api/v1/admin/roles/{roleId}', { params: { path: { roleId: role.id } } }),
      );
      router.push('/admin/roles');
    } catch (error) {
      setConfirmDelete(false);
      fail(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-5" data-testid="role-editor">
      {role.managed && <InlineAlert tone="info" title={t('managed')} />}
      {!role.managed && !canManage && <InlineAlert tone="info" title={t('readOnly')} />}
      {editable && !role.seed && (
        <div className="flex flex-wrap items-end gap-2">
          <TextField
            label={t('name')}
            value={name}
            maxLength={50}
            onChange={(event) => setName(event.target.value)}
            error={nameError}
            className="min-w-0 flex-1 sm:max-w-[320px]"
          />
          <Button
            size="md"
            variant="secondary"
            loading={busy}
            disabled={name.trim() === role.name}
            onClick={() => void rename()}
          >
            {t('rename')}
          </Button>
          <Button size="md" variant="danger" icon="trash" onClick={() => setConfirmDelete(true)}>
            {t('delete')}
          </Button>
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-2">
        {permissionGroups(role.managed ? catalogue : grantable).map((group) => (
          <fieldset
            key={group.area}
            className="flex min-w-0 flex-col gap-2 rounded-card border border-border p-4"
          >
            <legend className="px-1 text-label font-bold text-navy-900">
              {tAreas(group.area as 'Shops')}
            </legend>
            {group.codes.map((code) => (
              <Checkbox
                key={code}
                label={tCodes(permissionKey(code) as 'Admin_Shops_View')}
                description={editable && blocked.has(code) ? t('notHeld') : undefined}
                checked={selected.has(code)}
                disabled={!editable || (blocked.has(code) && !selected.has(code))}
                onChange={(event) => toggle(code, event.target.checked)}
              />
            ))}
          </fieldset>
        ))}
      </div>

      {editable && (
        <div className="sticky bottom-3 z-10 flex flex-wrap items-center gap-3 rounded-card border border-border bg-surface p-3 shadow-e3">
          <Button size="md" icon="check" loading={busy} onClick={() => void save()}>
            {t('save')}
          </Button>
          <span className="text-helper text-text-secondary">{t('auditNote')}</span>
        </div>
      )}
      {saved && <InlineAlert tone="success" title={saved} />}
      {failure && <InlineAlert tone="danger" title={failure} />}

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title={t('deleteTitle')}
        body={t('deleteBody')}
        confirmLabel={t('delete')}
        loading={busy}
        onConfirm={() => void remove()}
      />
    </div>
  );
}

/** Creates an empty admin role, then opens it to choose its permissions. */
export function CreateRoleForm() {
  const t = useTranslations('adminRoles.create');
  const tv = useTranslations('validation');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [name, setName] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const create = async () => {
    if (name.trim().length < 2) {
      setError(tv('required'));
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      const role = ensureOk(await browserApi.POST('/api/v1/admin/roles', { body: { name: name.trim() } }));
      router.push(`/admin/roles/${role.id}`);
    } catch (failure) {
      const code = failure instanceof ApiError ? failure.fieldErrors.name?.[0] : undefined;
      setError(
        code
          ? tv(codeToMessageKey(code) as 'generic')
          : apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-wrap items-end gap-2">
      <TextField
        label={t('name')}
        value={name}
        maxLength={50}
        onChange={(event) => setName(event.target.value)}
        error={error}
        className="min-w-0 flex-1 sm:max-w-[320px]"
      />
      <Button size="md" icon="plus" loading={busy} onClick={() => void create()}>
        {t('submit')}
      </Button>
    </div>
  );
}
