import type { Metadata } from 'next';
import Image from 'next/image';
import { getTranslations } from 'next-intl/server';
import { FavoriteButton } from '@/components/favorites/FavoriteButton';
import { Avatar, ImagePlaceholder } from '@/components/ui/Avatar';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { RatingStars } from '@/components/ui/Rating';
import { EmptyState, PermissionDenied } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { requireCustomer } from '@/lib/auth/server';
import { bookHref } from '@/lib/booking/links';
import { type AppLocale, formatNumber, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/favorites'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'favorites' });
  return { title: t('title') };
}

/**
 * Saved shops and professionals (c-profile 1928–1965, R-CUS-10, D-098): shop rows with rating, area and open status,
 * professional rows with a "book" shortcut to the wizard with them preselected. Hearts remove (and re-add) in place.
 */
export default async function FavoritesPage({ params }: PageProps<'/[locale]/account/favorites'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw) as AppLocale;
  const me = await requireCustomer(locale, '/account/favorites');
  if (!me) return <PermissionDenied homeHref="/" />;

  const api = await getServerApi();
  const { data, response } = await api.GET('/api/v1/me/favorites');
  if (!data) throw new Error(`GET /api/v1/me/favorites failed with status ${response.status}`);
  const [t, tOpening] = await Promise.all([
    getTranslations({ locale, namespace: 'favorites' }),
    getTranslations({ locale, namespace: 'opening' }),
  ]);
  const empty = data.shops.length === 0 && data.professionals.length === 0;

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6" data-testid="favorites">
      <div className="flex flex-col gap-1">
        <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
        {!empty && (
          <p className="text-helper text-text-secondary">
            {t('count', {
              shops: data.shops.length,
              pros: data.professionals.length,
              shopsText: formatNumber(data.shops.length, locale),
              prosText: formatNumber(data.professionals.length, locale),
            })}
          </p>
        )}
      </div>

      {empty ? (
        <EmptyState
          icon="heart"
          title={t('empty.title')}
          body={t('empty.body')}
          action={
            <ButtonLink href="/search" variant="primary" size="md">
              {t('empty.cta')}
            </ButtonLink>
          }
        />
      ) : (
        <>
          {data.shops.length > 0 && (
            <section aria-labelledby="fav-shops" className="flex flex-col gap-3">
              <h2 id="fav-shops" className="text-h3 font-bold text-text-primary">
                {t('shops')}
              </h2>
              <ul className="flex flex-col gap-3">
                {data.shops.map((shop) => {
                  const name = localizedName(locale, shop.nameAr, shop.nameEn);
                  return (
                    <li
                      key={shop.id}
                      className="flex items-center gap-3 rounded-card border border-border bg-surface p-3 shadow-e1"
                    >
                      {(shop.coverUrl ?? shop.logoUrl) ? (
                        <Image
                          src={(shop.coverUrl ?? shop.logoUrl)!}
                          alt=""
                          width={64}
                          height={64}
                          className="size-16 shrink-0 rounded-field object-cover"
                        />
                      ) : (
                        <ImagePlaceholder className="size-16 shrink-0 rounded-field" />
                      )}
                      <div className="flex min-w-0 flex-1 flex-col gap-1">
                        <Link
                          href={`/shops/${shop.slug}`}
                          className="truncate text-label font-bold text-text-primary hover:underline"
                        >
                          {name}
                        </Link>
                        <p className="flex flex-wrap items-center gap-2 text-helper text-text-secondary">
                          {shop.reviewCount > 0 && (
                            <RatingStars value={shop.rating} count={shop.reviewCount} />
                          )}
                          {shop.district && <span>{shop.district}</span>}
                        </p>
                        <p className="flex flex-wrap items-center gap-2">
                          <Badge tone={shop.isOpenNow ? 'success' : 'neutral'} size="sm">
                            {shop.isOpenNow ? tOpening('openNow') : tOpening('closed')}
                          </Badge>
                          {!shop.isOpenNow && shop.nextOpensAt && (
                            <span className="text-helper text-text-secondary">
                              {tOpening('opensAt', {
                                time: formatTime(shop.nextOpensAt, locale, shop.timeZone),
                              })}
                            </span>
                          )}
                        </p>
                      </div>
                      <FavoriteButton target={{ kind: 'shop', id: shop.id }} name={name} initiallySaved />
                    </li>
                  );
                })}
              </ul>
            </section>
          )}

          {data.professionals.length > 0 && (
            <section aria-labelledby="fav-pros" className="flex flex-col gap-3">
              <h2 id="fav-pros" className="text-h3 font-bold text-text-primary">
                {t('professionals')}
              </h2>
              <ul className="flex flex-col gap-3">
                {data.professionals.map((pro) => {
                  const name = localizedName(locale, pro.nameAr, pro.nameEn);
                  return (
                    <li
                      key={pro.id}
                      className="flex items-center gap-3 rounded-card border border-border bg-surface p-3 shadow-e1"
                    >
                      <Avatar name={name} src={pro.avatarUrl} size="lg" />
                      <div className="flex min-w-0 flex-1 flex-col gap-1">
                        <Link
                          href={`/shops/${pro.shopSlug}/professionals/${pro.slug}`}
                          className="truncate text-label font-bold text-text-primary hover:underline"
                        >
                          {name}
                        </Link>
                        <p className="flex flex-wrap items-center gap-2 text-helper text-text-secondary">
                          <span>{localizedName(locale, pro.shopNameAr, pro.shopNameEn)}</span>
                          {pro.reviewCount > 0 && <RatingStars value={pro.rating} count={pro.reviewCount} />}
                        </p>
                      </div>
                      <ButtonLink
                        href={bookHref(pro.shopSlug, { pro: pro.id })}
                        variant="primary"
                        size="sm"
                        aria-label={t('bookWith', { name })}
                      >
                        {t('book')}
                      </ButtonLink>
                      <FavoriteButton
                        target={{ kind: 'professional', id: pro.id, shopId: pro.shopId }}
                        name={name}
                        initiallySaved
                      />
                    </li>
                  );
                })}
              </ul>
            </section>
          )}
        </>
      )}
    </div>
  );
}
