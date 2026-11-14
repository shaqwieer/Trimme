import { getTranslations } from 'next-intl/server';
import { PublicShell } from '@/components/shell/PublicShell';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { type AppLocale, formatDate } from '@/lib/i18n/format';
import { RichText, type RichNode } from './RichText';

/** The built-in sections, shown until the admin saves the page's text in this language (D-144). */
export function builtInSections(
  kind: 'terms' | 'privacy',
  t: (key: string) => string,
): Array<{ key: string; title: string; body: string }> {
  const keys =
    kind === 'terms'
      ? (['service', 'account', 'bookings', 'reviews'] as const)
      : (['data', 'location', 'messages', 'cookies', 'rights'] as const);
  return keys.map((key) => ({
    key,
    title: t(`${kind}.sections.${key}.title`),
    body: t(`${kind}.sections.${key}.body`),
  }));
}

/**
 * Terms of use and privacy policy (DV-A28). The platform admin writes them per language in rich text (D-144); until
 * they do, the built-in text states the product's real rules (no payment in v1, phone never shared with shops,
 * location kept on the device). The draft notice was removed at the client's request (D-138).
 */
export async function LegalPage({ kind, locale }: { kind: 'terms' | 'privacy'; locale: AppLocale }) {
  const t = await getTranslations({ locale, namespace: 'legal' });
  const api = await getPublicApi();
  // The API answers 404 while nothing is saved; any failure falls back to the built-in text too.
  const saved = await optional(
    () => api.GET('/api/v1/public/legal/{kind}', { params: { path: { kind }, query: { locale } } }),
    `legal ${kind}`,
  );
  const document = saved?.content as RichNode | undefined;

  return (
    <PublicShell variant="marketing">
      <article
        className="mx-auto flex max-w-[760px] flex-col gap-6 px-4 py-10 md:px-6"
        data-testid="legal-page"
      >
        <h1 className="text-h1 font-bold text-navy-900">{t(`${kind}.title`)}</h1>
        {saved && document?.type === 'doc' ? (
          <>
            <p className="text-helper text-text-secondary">
              {t('updated', {
                date: formatDate(saved.updatedAt, locale, { withYear: true, withWeekday: false }),
              })}
            </p>
            <RichText document={document} />
          </>
        ) : (
          <>
            <p className="text-body text-text-secondary">{t(`${kind}.description`)}</p>
            {builtInSections(kind, (key) => t(key as Parameters<typeof t>[0])).map((section) => (
              <section key={section.key} className="flex flex-col gap-2">
                <h2 className="text-h3 font-bold text-navy-900">{section.title}</h2>
                <p className="text-body text-text-strong">{section.body}</p>
              </section>
            ))}
          </>
        )}
      </article>
    </PublicShell>
  );
}
