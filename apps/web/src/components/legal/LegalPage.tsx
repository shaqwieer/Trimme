import { getTranslations } from 'next-intl/server';
import { PublicShell } from '@/components/shell/PublicShell';
import { InlineAlert } from '@/components/ui/states';
import type { AppLocale } from '@/lib/i18n/format';

/**
 * Terms of use and privacy policy (DV-A28). The text is a placeholder that states the product's real rules (no payment
 * in v1, phone never shared with shops, location kept on the device) and is clearly marked for legal review.
 */
export async function LegalPage({ kind, locale }: { kind: 'terms' | 'privacy'; locale: AppLocale }) {
  const t = await getTranslations({ locale, namespace: 'legal' });
  const sections =
    kind === 'terms'
      ? (['service', 'account', 'bookings', 'reviews'] as const).map((key) => ({
          key,
          title: t(`terms.sections.${key}.title`),
          body: t(`terms.sections.${key}.body`),
        }))
      : (['data', 'location', 'messages', 'cookies', 'rights'] as const).map((key) => ({
          key,
          title: t(`privacy.sections.${key}.title`),
          body: t(`privacy.sections.${key}.body`),
        }));
  return (
    <PublicShell variant="marketing">
      <article className="mx-auto flex max-w-[760px] flex-col gap-6 px-4 py-10 md:px-6">
        <h1 className="text-h1 font-bold text-navy-900">{t(`${kind}.title`)}</h1>
        <InlineAlert tone="warning" title={t('draft')} />
        <p className="text-body text-text-secondary">{t(`${kind}.description`)}</p>
        {sections.map((section) => (
          <section key={section.key} className="flex flex-col gap-2">
            <h2 className="text-h3 font-bold text-navy-900">{section.title}</h2>
            <p className="text-body text-text-strong">{section.body}</p>
          </section>
        ))}
      </article>
    </PublicShell>
  );
}
