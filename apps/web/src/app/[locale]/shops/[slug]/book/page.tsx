import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { BookingWizard, type WizardViewer } from '@/components/booking/BookingWizard';
import { QueryProvider } from '@/components/providers/QueryProvider';
import { PublicShell } from '@/components/shell/PublicShell';
import { SkeletonList } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { dataOrNull, getPublicApi } from '@/lib/api/public';
import { getMe } from '@/lib/auth/server';
import type { WizardOffer } from '@/lib/booking/wizard';
import { getPublicShop } from '@/lib/discovery/shop-data';
import { localizedName } from '@/lib/i18n/localized';
import { NO_INDEX } from '@/lib/seo/site';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/shops/[slug]/book'>): Promise<Metadata> {
  const { locale: raw, slug } = await params;
  const locale = asLocale(raw);
  const shop = await getPublicShop(slug);
  const t = await getTranslations({ locale, namespace: 'booking' });
  return {
    title: shop
      ? t('meta.title', { shop: localizedName(locale, shop.nameAr, shop.nameEn) })
      : t('titles.service'),
    robots: NO_INDEX,
  };
}

/**
 * The booking wizard (c-booking, D-028): the shop's published services, packages and active professionals are read
 * here, anonymously (the public cache serves them); dates and slots are fetched live by the wizard. Never indexed.
 */
export default async function BookPage({ params }: PageProps<'/[locale]/shops/[slug]/book'>) {
  const { locale: raw, slug } = await params;
  const locale = asLocale(raw);
  const shop = await getPublicShop(slug);
  if (!shop) notFound();

  const api = await getPublicApi();
  const path = { params: { path: { slug } } };
  const [services, packages, professionals, me, t] = await Promise.all([
    api.GET('/api/v1/public/shops/{slug}/services', path).then((r) => dataOrNull(r, 'services') ?? []),
    api.GET('/api/v1/public/shops/{slug}/packages', path).then((r) => dataOrNull(r, 'packages') ?? []),
    api
      .GET('/api/v1/public/shops/{slug}/professionals', path)
      .then((r) => dataOrNull(r, 'professionals') ?? []),
    getMe(),
    getTranslations({ locale, namespace: 'booking' }),
  ]);

  const offers: WizardOffer[] = [
    ...services
      .filter((s) => s.onlineBookable)
      .map((s) => ({
        kind: 'service' as const,
        id: s.id,
        nameAr: s.nameAr,
        nameEn: s.nameEn ?? null,
        descriptionAr: s.descriptionAr ?? null,
        descriptionEn: s.descriptionEn ?? null,
        price: s.price,
        currency: s.currency,
        durationMinutes: s.durationMinutes,
        professionalIds: s.professionalIds,
      })),
    ...packages.map((p) => ({
      kind: 'package' as const,
      id: p.id,
      nameAr: p.nameAr,
      nameEn: p.nameEn ?? null,
      descriptionAr: p.descriptionAr ?? null,
      descriptionEn: p.descriptionEn ?? null,
      price: p.price,
      currency: p.currency,
      durationMinutes: p.durationMinutes,
      professionalIds: p.professionalIds,
    })),
  ];

  const viewer: WizardViewer = !me ? 'guest' : me.userType === 'Customer' ? 'customer' : 'staff';
  const area = [shop.location?.district, shop.location?.city]
    .filter(Boolean)
    .join(locale === 'ar' ? '، ' : ', ');

  return (
    <PublicShell>
      <QueryProvider>
        <Suspense fallback={<SkeletonList rows={4} label={t('loading')} />}>
          <BookingWizard
            shop={{
              slug: shop.slug,
              nameAr: shop.nameAr,
              nameEn: shop.nameEn,
              area: area || null,
              timeZone: shop.timeZone,
              cancellationCutoffMinutes: shop.cancellationCutoffMinutes,
              logoUrl: shop.logoUrl ?? null,
            }}
            offers={offers}
            professionals={professionals.map((p) => ({
              id: p.id,
              nameAr: p.nameAr,
              nameEn: p.nameEn,
              specialtyAr: p.specialtyAr ?? null,
              specialtyEn: p.specialtyEn ?? null,
              avatarUrl: p.avatarUrl ?? null,
              rating: p.rating,
              reviewCount: p.reviewCount,
            }))}
            viewer={viewer}
          />
        </Suspense>
      </QueryProvider>
    </PublicShell>
  );
}
