import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LoyaltyOffer, LoyaltyProgress, LoyaltyRewardRow } from '@/components/loyalty/LoyaltyParts';
import { ButtonLink } from '@/components/ui/Button';
import { EmptyState, PermissionDenied } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { requireCustomer } from '@/lib/auth/server';
import { bookHref } from '@/lib/booking/links';
import { localizedName } from '@/lib/i18n/localized';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/loyalty'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'loyalty' });
  return { title: t('title') };
}

/**
 * The customer's loyalty cards (D-132): one card per salon where they have visits or a coupon. Each completed visit
 * fills a green circle; a full card becomes a coupon, chosen on the confirm step of the next booking at that salon.
 */
export default async function LoyaltyPage({ params }: PageProps<'/[locale]/account/loyalty'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const me = await requireCustomer(locale, '/account/loyalty');
  if (!me) return <PermissionDenied homeHref="/" />;

  const api = await getServerApi();
  const { data, response } = await api.GET('/api/v1/me/loyalty');
  if (!data) throw new Error(`GET /api/v1/me/loyalty failed with status ${response.status}`);
  const t = await getTranslations({ locale, namespace: 'loyalty' });

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6" data-testid="loyalty-page">
      <div className="flex flex-col gap-1">
        <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
        <p className="text-helper text-text-secondary">{t('intro')}</p>
      </div>

      {data.items.length === 0 ? (
        <EmptyState
          icon="tag"
          title={t('empty.title')}
          body={t('empty.body')}
          action={
            <ButtonLink href="/shops" variant="primary" size="md">
              {t('empty.cta')}
            </ButtonLink>
          }
        />
      ) : (
        <ul className="flex flex-col gap-4">
          {data.items.map((card) => (
            <li
              key={card.shop.id}
              className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1"
              data-testid="loyalty-card"
            >
              <Link
                href={`/shops/${card.shop.slug}`}
                className="text-label font-bold text-text-primary hover:underline"
              >
                {localizedName(locale, card.shop.nameAr, card.shop.nameEn)}
              </Link>
              {card.program ? (
                <>
                  <LoyaltyOffer program={card.program} />
                  <LoyaltyProgress program={card.program} stamps={card.stamps} />
                </>
              ) : (
                <p className="text-helper text-text-secondary">{t('programEnded')}</p>
              )}
              {card.rewards.map((reward) => (
                <LoyaltyRewardRow key={reward.id} reward={reward} />
              ))}
              {card.rewards.some((r) => r.status === 'Available') && (
                <ButtonLink
                  href={bookHref(card.shop.slug)}
                  variant="primary"
                  size="md"
                  className="self-start"
                >
                  {t('bookToUse')}
                </ButtonLink>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
