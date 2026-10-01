'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/cards';
import { Checkbox, PhoneField, SelectField, TextField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import {
  activeOf,
  draftOf,
  insertPlaceholder,
  isDirty,
  MAX_BODY_LENGTH,
  MAX_BUTTONS,
  placeholderToken,
  type TemplateButton,
  type TemplateDetail,
  type TemplatePreview,
} from '@/lib/admin/whatsapp';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDate, formatNumber, formatTime } from '@/lib/i18n/format';
import { BubblePreview } from './BubblePreview';

type Props = {
  initial: TemplateDetail;
  canEdit: boolean;
  canActivate: boolean;
  canTestSend: boolean;
};

type IssueT = (key: string, values?: Record<string, string>) => string;

/**
 * The WhatsApp template editor (DV-A12, R-NTF-02, D-109). Left: the text with placeholder chips (the audience's
 * whitelist only), up to two buttons whose links the platform builds, and the approved Meta template name. Right: a
 * neutral message bubble rendered by the API with fixed sample data (never a real customer), the validation issues, the
 * actions and the version history. Saving edits the draft; activating makes it live for future messages only; the
 * test send goes to a number typed here and confirmed as a test recipient (R-NTF-08).
 */
export function TemplateEditor({ initial, canEdit, canActivate, canTestSend }: Props) {
  const t = useTranslations('adminWhatsApp.editor');
  const tIssue = useTranslations('adminWhatsApp.issues') as unknown as IssueT;
  const tPlaceholder = useTranslations('adminWhatsApp.placeholders') as unknown as (key: string) => string;
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const [detail, setDetail] = useState(initial);
  const stored = draftOf(detail) ?? activeOf(detail);
  const [body, setBody] = useState(stored?.body ?? '');
  const [buttons, setButtons] = useState<TemplateButton[]>(stored?.buttons ?? []);
  const [providerTemplateName, setProviderTemplateName] = useState(stored?.providerTemplateName ?? '');
  const [preview, setPreview] = useState<TemplatePreview>();
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string }>();
  const [confirmActivate, setConfirmActivate] = useState(false);
  const textarea = useRef<HTMLTextAreaElement>(null);
  const draft = draftOf(detail);
  const active = activeOf(detail);
  const dirty = isDirty({ body, buttons, providerTemplateName }, draft ?? active);

  // Live preview and validation from the API (debounced); the API is the single source of the rules.
  useEffect(() => {
    const timer = setTimeout(async () => {
      const { data } = await browserApi.POST('/api/v1/admin/whatsapp/templates/{templateId}/preview', {
        params: { path: { templateId: detail.id } },
        body: { body, buttons },
      });
      if (data) setPreview(data);
    }, 350);
    return () => clearTimeout(timer);
  }, [body, buttons, detail.id]);

  const run = async (work: () => Promise<TemplateDetail>, success: string) => {
    setBusy(true);
    setNotice(undefined);
    try {
      const next = await work();
      setDetail(next);
      setNotice({ tone: 'success', text: success });
      return next;
    } catch (error) {
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : undefined) ?? t('failed'),
      });
      return undefined;
    } finally {
      setBusy(false);
    }
  };

  const save = () =>
    run(
      async () =>
        ensureOk(
          await browserApi.PUT('/api/v1/admin/whatsapp/templates/{templateId}/draft', {
            params: { path: { templateId: detail.id } },
            body: {
              body,
              buttons,
              providerTemplateName: providerTemplateName.trim() || null,
              version: detail.version,
            },
          }),
        ),
      t('saved'),
    );

  const activate = async () => {
    setConfirmActivate(false);
    let current = detail;
    if (dirty) {
      const saved = await save();
      if (!saved) return;
      current = saved;
    }
    const toActivate = draftOf(current);
    if (!toActivate) return;
    await run(
      async () =>
        ensureOk(
          await browserApi.POST(
            '/api/v1/admin/whatsapp/templates/{templateId}/versions/{versionId}/activate',
            {
              params: { path: { templateId: current.id, versionId: toActivate.id } },
              body: { version: current.version },
            },
          ),
        ),
      t('activated', { number: formatNumber(toActivate.number, locale) }),
    );
  };

  const restore = async (versionId: string) => {
    const next = await run(
      async () =>
        ensureOk(
          await browserApi.POST(
            '/api/v1/admin/whatsapp/templates/{templateId}/versions/{versionId}/restore',
            {
              params: { path: { templateId: detail.id, versionId } },
              body: { version: detail.version },
            },
          ),
        ),
      t('restored'),
    );
    const restored = next && draftOf(next);
    if (restored) {
      setBody(restored.body);
      setButtons(restored.buttons);
      setProviderTemplateName(restored.providerTemplateName ?? '');
    }
  };

  const insert = (name: string) => {
    const element = textarea.current;
    const { text, caret } = insertPlaceholder(
      body,
      element?.selectionStart ?? body.length,
      element?.selectionEnd ?? body.length,
      name,
    );
    setBody(text);
    requestAnimationFrame(() => {
      element?.focus();
      element?.setSelectionRange(caret, caret);
    });
  };

  const issues = preview?.issues ?? [];
  const bodyIssues = issues.filter((i) => i.field === 'body');
  const otherIssues = issues.filter((i) => i.field !== 'body');
  const targets: TemplateButton['target'][] =
    detail.audience === 'Customer' ? ['ManageBooking', 'ShopPage'] : ['ShopPage'];

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
      <Card as="section" className="flex flex-col gap-4 p-5" aria-labelledby="template-text">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="template-text" className="text-h3 font-bold text-navy-900">
            {t('text')}
          </h2>
          {draft ? (
            <Badge tone="warning">{t('draftBadge', { number: formatNumber(draft.number, locale) })}</Badge>
          ) : (
            active && (
              <Badge tone="success">
                {t('activeBadge', { number: formatNumber(active.number, locale) })}
              </Badge>
            )
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="template-body" className="text-label font-bold text-text-primary">
            {t('body')}
          </label>
          <textarea
            id="template-body"
            ref={textarea}
            value={body}
            onChange={(event) => setBody(event.target.value)}
            readOnly={!canEdit}
            rows={9}
            dir={detail.locale === 'ar' ? 'rtl' : 'ltr'}
            lang={detail.locale}
            aria-invalid={bodyIssues.length > 0}
            aria-describedby="template-body-help template-body-issues"
            className={cn(
              'min-h-[200px] w-full rounded-field border-[1.5px] bg-surface px-3 py-2.5 text-input leading-relaxed text-text-primary',
              bodyIssues.length > 0 ? 'border-danger-500' : 'border-border-input',
            )}
          />
          <p id="template-body-help" className="text-helper text-text-secondary">
            {t('length', {
              used: formatNumber(body.length, locale),
              max: formatNumber(MAX_BODY_LENGTH, locale),
            })}
          </p>
          <ul id="template-body-issues" className="flex flex-col gap-1" aria-live="polite">
            {bodyIssues.map((issue) => (
              <li
                key={`${issue.code}-${issue.placeholder ?? ''}`}
                className="text-helper font-bold text-danger-700"
              >
                {tIssue(issue.code.replace(/\./g, '_'), { name: issue.placeholder ?? '' })}
              </li>
            ))}
          </ul>
        </div>

        {canEdit && (
          <div className="flex flex-col gap-2">
            <p className="text-label font-bold text-text-primary" id="placeholder-chips">
              {t('insert')}
            </p>
            <ul className="flex flex-wrap gap-2" aria-labelledby="placeholder-chips">
              {detail.allowedPlaceholders.map((name) => (
                <li key={name}>
                  <button
                    type="button"
                    onClick={() => insert(name)}
                    className="inline-flex min-h-11 items-center gap-1.5 rounded-pill border-[1.5px] border-border-strong bg-surface px-3 text-label text-text-strong hover:border-brand-500"
                    data-placeholder={name}
                  >
                    {tPlaceholder(name)}
                    <span className="font-latin text-helper text-text-secondary" dir="ltr">
                      {placeholderToken(name)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}

        <fieldset className="flex min-w-0 flex-col gap-3">
          <legend className="text-label font-bold text-text-primary">{t('buttons')}</legend>
          {buttons.map((button, index) => (
            <div key={index} className="flex flex-wrap items-end gap-2">
              <TextField
                label={t('buttonLabel')}
                value={button.label}
                maxLength={25}
                disabled={!canEdit}
                onChange={(event) =>
                  setButtons((current) =>
                    current.map((b, i) => (i === index ? { ...b, label: event.target.value } : b)),
                  )
                }
                className="min-w-[160px] flex-1"
              />
              <SelectField
                label={t('buttonTarget')}
                value={button.target}
                disabled={!canEdit}
                onChange={(event) =>
                  setButtons((current) =>
                    current.map((b, i) =>
                      i === index ? { ...b, target: event.target.value as TemplateButton['target'] } : b,
                    ),
                  )
                }
                className="min-w-[160px] flex-1"
              >
                {targets.map((target) => (
                  <option key={target} value={target}>
                    {t(`target.${target}`)}
                  </option>
                ))}
              </SelectField>
              {canEdit && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setButtons((current) => current.filter((_, i) => i !== index))}
                >
                  {t('removeButton')}
                </Button>
              )}
            </div>
          ))}
          {canEdit && buttons.length < MAX_BUTTONS && (
            <div>
              <Button
                variant="secondary"
                size="sm"
                icon="plus"
                onClick={() =>
                  setButtons((current) => [
                    ...current,
                    {
                      label: '',
                      target:
                        targets.find((target) => !current.some((b) => b.target === target)) ?? targets[0]!,
                    },
                  ])
                }
              >
                {t('addButton')}
              </Button>
            </div>
          )}
        </fieldset>

        <TextField
          label={t('providerName')}
          helper={t('providerNameHelp')}
          value={providerTemplateName}
          disabled={!canEdit}
          dir="ltr"
          onChange={(event) => setProviderTemplateName(event.target.value)}
        />
        {otherIssues.map((issue) => (
          <p key={issue.code} className="text-helper font-bold text-danger-700">
            {tIssue(issue.code.replace(/\./g, '_'), { name: issue.placeholder ?? '' })}
          </p>
        ))}
      </Card>

      <div className="flex flex-col gap-4">
        <Card as="section" className="flex flex-col gap-3 p-5" aria-labelledby="template-preview">
          <h2 id="template-preview" className="text-h3 font-bold text-navy-900">
            {t('preview')}
          </h2>
          <p className="text-helper text-text-secondary">{t('previewHelp')}</p>
          <BubblePreview locale={detail.locale} body={preview?.body ?? ''} buttons={preview?.buttons ?? []} />
          {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
          <div className="flex flex-wrap gap-2">
            {canEdit && (
              <Button
                variant="secondary"
                onClick={() => void save()}
                disabled={busy || !dirty || issues.length > 0}
              >
                {t('saveDraft')}
              </Button>
            )}
            {canActivate && (
              <Button
                variant="primary"
                onClick={() => setConfirmActivate(true)}
                disabled={busy || issues.length > 0 || (!draft && !dirty)}
              >
                {t('activate')}
              </Button>
            )}
            {canTestSend && <TestSend templateId={detail.id} draftId={draft?.id} disabled={busy} />}
          </div>
          <ConfirmDialog
            open={confirmActivate}
            onOpenChange={setConfirmActivate}
            title={t('activateTitle')}
            body={t('activateBody')}
            confirmLabel={t('activate')}
            tone="default"
            onConfirm={() => void activate()}
          />
        </Card>

        <Card as="section" className="flex flex-col gap-3 p-5" aria-labelledby="template-history">
          <h2 id="template-history" className="text-h3 font-bold text-navy-900">
            {t('history')}
          </h2>
          <ol className="flex flex-col gap-2" data-testid="template-history">
            {detail.versions.map((version) => (
              <li
                key={version.id}
                className="flex flex-col gap-1 rounded-button bg-bg-subtle p-3"
                data-status={version.status}
              >
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-label font-bold text-text-primary">
                    {t('versionNumber', { number: formatNumber(version.number, locale) })}
                  </span>
                  <Badge
                    size="sm"
                    tone={
                      version.status === 'Active'
                        ? 'success'
                        : version.status === 'Draft'
                          ? 'warning'
                          : 'neutral'
                    }
                  >
                    {t(`status.${version.status}`)}
                  </Badge>
                </div>
                <p
                  className="line-clamp-2 text-helper whitespace-pre-line text-text-strong"
                  dir={detail.locale === 'ar' ? 'rtl' : 'ltr'}
                  lang={detail.locale}
                >
                  {version.body}
                </p>
                <p className="text-helper text-text-secondary">
                  {version.activatedAt
                    ? t('activatedBy', {
                        name: version.activatedByName ?? t('system'),
                        date: `${formatDate(version.activatedAt, locale, { withWeekday: false })} ${formatTime(version.activatedAt, locale)}`,
                      })
                    : t('editedBy', {
                        name: version.createdByName ?? t('system'),
                        date: formatDate(version.createdAt, locale, { withWeekday: false }),
                      })}
                </p>
                {canEdit && version.status === 'Archived' && (
                  <div>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => void restore(version.id)}
                      disabled={busy}
                    >
                      {t('restore')}
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </ol>
        </Card>
      </div>
    </div>
  );
}

/**
 * The test send (R-NTF-08): the number is typed here (never prefilled) and explicitly confirmed as a test recipient; the
 * API refuses a registered customer's number and renders sample data only.
 */
function TestSend({
  templateId,
  draftId,
  disabled,
}: {
  templateId: string;
  draftId?: string;
  disabled: boolean;
}) {
  const t = useTranslations('adminWhatsApp.testSend');
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [phone, setPhone] = useState<string>();
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ tone: 'success' | 'danger'; text: string }>();

  const send = async () => {
    setBusy(true);
    setResult(undefined);
    try {
      const sent = ensureOk(
        await browserApi.POST('/api/v1/admin/whatsapp/templates/{templateId}/test-send', {
          params: { path: { templateId } },
          body: { versionId: draftId ?? null, recipient: phone ?? '', confirmTestRecipient: confirmed },
        }),
      );
      setResult(
        sent.status === 'Failed'
          ? { tone: 'danger', text: t('failedSend', { error: sent.lastError ?? '' }) }
          : { tone: 'success', text: t('sent', { recipient: sent.recipientMasked }) },
      );
    } catch (error) {
      setResult({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : undefined) ?? t('failed'),
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) {
          setPhone(undefined);
          setConfirmed(false);
          setResult(undefined);
        }
      }}
      trigger={
        <Button variant="ghost" disabled={disabled}>
          {t('open')}
        </Button>
      }
      title={t('title')}
      description={t('body')}
      footer={
        <Button variant="primary" onClick={() => void send()} disabled={busy || !phone || !confirmed}>
          {t('send')}
        </Button>
      }
    >
      <div className="flex flex-col gap-3">
        <PhoneField label={t('recipient')} onValueChange={(_, e164) => setPhone(e164 ?? undefined)} />
        <Checkbox
          label={t('confirm')}
          checked={confirmed}
          onChange={(event) => setConfirmed(event.target.checked)}
        />
        {result && <InlineAlert tone={result.tone} title={result.text} />}
      </div>
    </Dialog>
  );
}
