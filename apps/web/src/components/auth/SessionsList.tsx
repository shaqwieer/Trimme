'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { ConfirmDialog } from '@/components/ui/overlays';
import { ErrorState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';

const SESSIONS_KEY = ['auth', 'sessions'] as const;

/**
 * Account security (spec §9 revoke-all, R-AUTH-05): the signed-in devices, sign out of one device, or of every
 * other device. The design has no screen for this (DV-A01); it uses the account card and list patterns.
 */
export function SessionsList() {
  const t = useTranslations('account.sessions');
  const locale = useLocale() as AppLocale;
  const queryClient = useQueryClient();
  const apiMessage = useApiErrorMessage();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string }>();

  const sessions = useQuery({
    queryKey: SESSIONS_KEY,
    queryFn: async () => ensureOk(await browserApi.GET('/api/v1/auth/sessions')),
  });

  const revokeOne = useMutation({
    mutationFn: async (sessionId: string) =>
      ensureOk(
        await browserApi.DELETE('/api/v1/auth/sessions/{sessionId}', { params: { path: { sessionId } } }),
      ),
    onSuccess: () => setNotice({ tone: 'success', text: t('revoked') }),
    onError: (error) =>
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: SESSIONS_KEY }),
  });

  const revokeOthers = useMutation({
    mutationFn: async () => ensureOk(await browserApi.POST('/api/v1/auth/sessions/revoke-all')),
    onSuccess: ({ revoked }) => {
      setConfirmOpen(false);
      setNotice({ tone: 'success', text: t('revokedOthers', { count: revoked }) });
    },
    onError: (error) =>
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: SESSIONS_KEY }),
  });

  if (sessions.isPending) return <SkeletonList rows={2} label={t('title')} />;
  if (sessions.isError)
    return (
      <ErrorState
        action={
          <Button size="sm" variant="outline" onClick={() => void sessions.refetch()}>
            {t('title')}
          </Button>
        }
      />
    );

  const others = sessions.data.filter((s) => !s.isCurrent);

  return (
    <section aria-labelledby="sessions-title" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="sessions-title" className="text-h3 font-bold text-navy-900">
          {t('title')}
        </h2>
        <Button
          variant="outline"
          size="sm"
          icon="logout"
          disabled={others.length === 0}
          onClick={() => setConfirmOpen(true)}
        >
          {t('revokeOthers')}
        </Button>
      </div>

      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}

      <ul
        className="flex flex-col divide-y divide-border-subtle rounded-card border border-border bg-surface"
        data-testid="sessions"
      >
        {sessions.data.map((session) => (
          <li key={session.id} className="flex items-center gap-3 px-4 py-3.5">
            <span
              aria-hidden="true"
              className="flex size-10 shrink-0 items-center justify-center rounded-button bg-brand-100 text-brand-700"
            >
              <Icon name="shield" className="size-5" />
            </span>
            <div className="min-w-0 flex-1">
              <p className="flex flex-wrap items-center gap-2 text-label font-bold text-text-primary">
                <span className="font-latin">{session.deviceLabel}</span>
                {session.isCurrent && (
                  <span className="rounded-badge bg-success-50 px-2 py-0.5 text-badge font-bold text-success-700">
                    {t('current')}
                  </span>
                )}
              </p>
              <p className="text-helper text-text-tertiary">
                {t('lastActive', {
                  date: `${formatDate(session.lastSeenAt, locale)} · ${formatTime(session.lastSeenAt, locale)}`,
                })}
              </p>
            </div>
            {!session.isCurrent && (
              <Button
                variant="ghost"
                size="xs"
                aria-label={t('revokeLabel', { device: session.deviceLabel })}
                loading={revokeOne.isPending && revokeOne.variables === session.id}
                onClick={() => revokeOne.mutate(session.id)}
              >
                {t('revoke')}
              </Button>
            )}
          </li>
        ))}
      </ul>
      {others.length === 0 && <p className="text-caption text-text-secondary">{t('onlyThisDevice')}</p>}

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t('revokeOthersTitle')}
        body={t('revokeOthersBody')}
        confirmLabel={t('revokeOthers')}
        loading={revokeOthers.isPending}
        onConfirm={() => revokeOthers.mutate()}
      />
    </section>
  );
}
