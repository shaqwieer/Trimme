import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LoyaltyHistory } from '@/components/loyalty/LoyaltyHistory';
import { LoyaltyRewardRow, LoyaltyRulesText } from '@/components/loyalty/LoyaltyParts';
import { LoyaltyPointsCard } from '@/components/loyalty/LoyaltyPointsCard';
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
 * The customer's loyalty cards (D-149): one per salon where their mobile number has points, history or a coupon. Each
 * visit that ends adds points; a full card is a reward chosen on the confirm step of the next booking there, and the
 * points beyond it show as «+٢» on the next card. While a salon's programme is paused the points are kept. D-132 coupons
 * earned before stay listed until used or expired.
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
          {data.items.map((card) => {
            const coupons = card.coupons.filter((c) => c.status !== 'Redeemed');
            const canUse =
              (card.active && card.rewardsReady > 0) || coupons.some((c) => c.status === 'Available');
            return (
              <li
                key={card.shop.id}
                className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1"
                data-testid="loyalty-card"
                data-active={card.active}
              >
                <Link
                  href={`/shops/${card.shop.slug}`}
                  className="text-label font-bold text-text-primary hover:underline"
                >
                  {localizedName(locale, card.shop.nameAr, card.shop.nameEn)}
                </Link>
                {card.rules && <LoyaltyRulesText rules={card.rules} />}
                {!card.active && <p className="text-helper text-text-secondary">{t('card.paused')}</p>}
                {card.rules && <LoyaltyPointsCard card={card} rules={card.rules} />}
                {coupons.length > 0 && (
                  <div className="flex flex-col gap-2">
                    <p className="text-helper font-bold text-text-secondary">{t('coupons')}</p>
                    {coupons.map((reward) => (
                      <LoyaltyRewardRow key={reward.id} reward={reward} />
                    ))}
                  </div>
                )}
                <LoyaltyHistory history={card.history} />
                {canUse && (
                  <ButtonLink
                    href={bookHref(card.shop.slug)}
                    variant="primary"
                    size="md"
                    className="self-start"
                  >
                    {t('card.bookToUse')}
                  </ButtonLink>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
