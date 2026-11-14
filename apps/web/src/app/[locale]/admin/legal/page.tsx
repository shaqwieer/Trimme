import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { LegalEditor } from '@/components/admin/LegalEditor';
import { builtInSections } from '@/components/legal/LegalPage';
import type { RichNode } from '@/components/legal/RichText';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { ErrorState } from '@/components/ui/states';
import { asLocale, LANGUAGE_NAME_KEY, routing } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const KINDS = ['terms', 'privacy'] as const;
type Kind = (typeof KINDS)[number];

/** The built-in text of a page in one language, as a document the editor opens on (D-144). */
async function builtInDocument(kind: Kind, locale: string): Promise<RichNode> {
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'legal' });
  return {
    type: 'doc',
    content: builtInSections(kind, (key) => t(key as Parameters<typeof t>[0])).flatMap((section) => [
      { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: section.title }] },
      { type: 'paragraph', content: [{ type: 'text', text: section.body }] },
    ]),
  };
}

/**
 * Terms and privacy (D-144): the platform admin writes both legal pages per language in rich text. A tab per page and
 * per language; the editor opens on the saved text, or on the built-in one. Saving publishes at once; restoring
 * brings the built-in text back.
 */
export default async function LegalAdminPage({ params, searchParams }: PageProps<'/[locale]/admin/legal'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'legalAdmin' });
  const tProfile = await getTranslations({ locale: lang, namespace: 'account.profile' });
  const kind: Kind = firstParam(query.page) === 'privacy' ? 'privacy' : 'terms';
  const requested = firstParam(query.lang);
  const language = routing.locales.find((l) => l === requested) ?? 'ar';
  const href = (page: Kind, l: string) => `/admin/legal?page=${page}&lang=${l}`;

  return (
    <AdminFrame
      locale={locale}
      path={href(kind, language)}
      title={t('title')}
      permission="Admin.Settings.View"
    >
      {async (me) => {
        const { data: documents } = await (await getServerApi()).GET('/api/v1/admin/legal');
        if (!documents) return <ErrorState />;
        const saved = documents.find((d) => d.kind === kind && d.locale === language);
        const initial = (saved?.content as RichNode | undefined) ?? (await builtInDocument(kind, language));
        const languageName = tProfile(LANGUAGE_NAME_KEY[language]);
        return (
          <div className="flex max-w-[960px] flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            <LinkTabs
              label={t('title')}
              tabs={KINDS.map((k) => ({
                href: href(k, language),
                label: t(`kinds.${k}`),
                active: k === kind,
              }))}
            />
            <nav aria-label={t('locale')} className="flex flex-wrap gap-2">
              {routing.locales.map((l) => {
                const has = documents.some((d) => d.kind === kind && d.locale === l);
                return (
                  <a
                    key={l}
                    href={`/${locale}${href(kind, l)}`}
                    aria-current={l === language ? 'page' : undefined}
                    lang={l}
                    className={
                      l === language
                        ? 'inline-flex min-h-10 items-center gap-2 rounded-pill bg-navy-900 px-4 text-label font-bold text-on-navy'
                        : 'inline-flex min-h-10 items-center gap-2 rounded-pill border border-border bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle'
                    }
                  >
                    {tProfile(LANGUAGE_NAME_KEY[l])}
                    {has && <span aria-hidden="true" className="size-2 rounded-full bg-success-500" />}
                  </a>
                );
              })}
            </nav>
            <p className="text-helper text-text-secondary" data-testid="legal-status">
              {saved
                ? t('custom', {
                    date: formatDate(saved.updatedAt, lang, { withYear: true, withWeekday: false }),
                  })
                : t('builtIn')}
            </p>
            <LegalEditor
              key={`${kind}-${language}-${saved?.updatedAt ?? 'built-in'}`}
              kind={kind}
              locale={language}
              label={t('editorLabel', { page: t(`kinds.${kind}`), language: languageName })}
              initial={initial}
              saved={Boolean(saved)}
              canEdit={me.permissions.includes('Admin.Settings.Edit')}
            />
          </div>
        );
      }}
    </AdminFrame>
  );
}
