'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';

type Messages = components['schemas']['ShopMessagesResponse'];
type Item = Messages['items'][number];
type Audience = Item['audience'];

const AUDIENCES: Audience[] = ['Customer', 'Professional'];
const key = (item: Pick<Item, 'audience' | 'event'>) => `${item.audience}.${item.event}`;

/**
 * A shop's nine WhatsApp booking messages (D-142): five to the customer and four to the barber, each on or off. All are
 * on until an admin turns one off. A message turned off reaches nobody at this shop: no customer who books here and none
 * of its barbers. Only platform admins see this; the shop is never shown it. Saving sends every switch.
 */
export function ShopWhatsAppMessagesForm({
  shopId,
  messages,
  canEdit,
}: {
  shopId: string;
  messages: Messages;
  canEdit: boolean;
}) {
  const t = useTranslations('adminShops.whatsapp');
  const tw = useTranslations('adminWhatsApp');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [enabled, setEnabled] = useState<Record<string, boolean>>(() =>
    Object.fromEntries(messages.items.map((item) => [key(item), item.enabled])),
  );
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [pending, setPending] = useState(false);

  const total = messages.items.length;
  const on = messages.items.filter((item) => enabled[key(item)]).length;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaved(false);
    setFailure(undefined);
    setPending(true);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/shops/{shopId}/whatsapp-messages', {
          params: { path: { shopId } },
          body: {
            messages: messages.items.map((item) => ({
              audience: item.audience,
              event: item.event,
              enabled: enabled[key(item)] ?? true,
            })),
          },
        }),
      );
      setSaved(true);
      router.refresh();
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    } finally {
      setPending(false);
    }
  };

  return (
    <form
      method="post"
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-5"
      data-testid="shop-whatsapp-form"
    >
      <p className="text-caption text-text-secondary">{t('intro')}</p>
      {!canEdit && <InlineAlert tone="info" title={t('readOnly')} />}
      <p className="text-label font-bold text-navy-900" data-testid="shop-whatsapp-count">
        {t('count', { on: formatNumber(on, locale), total: formatNumber(total, locale) })}
      </p>
      {AUDIENCES.map((audience) => (
        <fieldset key={audience} className="flex flex-col rounded-card border border-border">
          <legend className="px-2 text-label font-bold text-text-strong">{t(`audience.${audience}`)}</legend>
          <ul className="flex flex-col divide-y divide-border-row">
            {messages.items
              .filter((item) => item.audience === audience)
              .map((item) => {
                const id = key(item);
                const isOn = enabled[id] ?? true;
                return (
                  <li key={id} className="flex flex-col gap-2 px-4 py-3" data-testid={`shop-whatsapp-${id}`}>
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                      <Switch
                        checked={isOn}
                        onCheckedChange={(value) => setEnabled((current) => ({ ...current, [id]: value }))}
                        label={tw(`event.${item.event}`)}
                        disabled={!canEdit}
                        className="min-w-0 flex-1 font-bold"
                      />
                      <Badge tone={isOn ? 'success' : 'neutral'}>{isOn ? t('on') : t('off')}</Badge>
                    </div>
                    {item.templateName && (
                      <span className="font-latin text-helper text-text-tertiary" dir="ltr">
                        {item.templateName}
                      </span>
                    )}
                    {item.audience === 'Professional' && item.event === 'BookingCancelled' && (
                      <span className="text-helper text-text-secondary">{t('cancelHint')}</span>
                    )}
                    {item.bodyAr && (
                      <details className="text-helper text-text-secondary">
                        <summary className="cursor-pointer text-text-link">{t('showText')}</summary>
                        <p
                          className="bg-subtle mt-2 rounded-field p-3 text-caption whitespace-pre-line text-text-primary"
                          dir="rtl"
                          lang="ar"
                        >
                          {item.bodyAr}
                        </p>
                      </details>
                    )}
                  </li>
                );
              })}
          </ul>
        </fieldset>
      ))}
      {failure && <InlineAlert tone="danger" title={failure} />}
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {canEdit && (
        <Button type="submit" variant="primary" loading={pending} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
