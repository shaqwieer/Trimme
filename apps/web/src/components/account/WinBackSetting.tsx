'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Switch } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

/**
 * Whether shops may send the customer «ندعوك للعودة» (D-155: the plans' win-back message, the one marketing message;
 * on by default). Booking messages — confirmation, change, cancellation, the reminder — always go out. Saved as soon as
 * it is switched; a failure puts it back.
 */
export function WinBackSetting({ enabled }: { enabled: boolean }) {
  const t = useTranslations('account.winBack');
  const apiMessage = useApiErrorMessage();
  const [on, setOn] = useState(enabled);
  const [pending, setPending] = useState(false);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string }>();

  const change = async (next: boolean) => {
    setOn(next);
    setPending(true);
    setNotice(undefined);
    try {
      ensureOk(await browserApi.PUT('/api/v1/me/win-back', { body: { enabled: next } }));
      setNotice({ tone: 'success', text: next ? t('on') : t('off') });
    } catch (failure) {
      setOn(!next);
      setNotice({
        tone: 'danger',
        text: apiMessage(failure instanceof ApiError ? failure : 'server.unexpected') ?? '',
      });
    } finally {
      setPending(false);
    }
  };

  return (
    <section
      aria-labelledby="win-back-title"
      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
      data-testid="win-back-setting"
    >
      <h2 id="win-back-title" className="text-h3 font-bold text-navy-900">
        {t('title')}
      </h2>
      <p className="text-caption text-text-secondary">{t('body')}</p>
      <Switch
        checked={on}
        onCheckedChange={(next) => void change(next)}
        label={t('label')}
        disabled={pending}
      />
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
    </section>
  );
}
