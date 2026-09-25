import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { PublicShell } from '@/components/shell/PublicShell';
import { Avatar } from '@/components/ui/Avatar';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button, ButtonLink, IconButton } from '@/components/ui/Button';
import { AppointmentCard, KpiTile, ProfessionalOption, ServiceOption, ShopCard } from '@/components/ui/cards';
import { BarChart, QrCard, RatingDistribution } from '@/components/ui/charts';
import { Breadcrumb, Pagination, ResponsiveTable, Timeline } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { RatingStars } from '@/components/ui/Rating';
import { RadioCard, TagChip } from '@/components/ui/selection';
import {
  EmptyState,
  ErrorState,
  ExpiredSession,
  InlineAlert,
  PermissionDenied,
  Skeleton,
  SkeletonList,
} from '@/components/ui/states';

export const metadata: Metadata = { title: 'Server components', robots: { index: false, follow: false } };

/**
 * Proof route for D-048 "Server-Component safe": this page has NO 'use client' and renders every
 * component the library documents as RSC-safe. If any of them attached its own event handler,
 * rendering would fail at request time. Guarded by E2E (smoke/gallery.spec.ts).
 */
export default async function ServerComponentsPage({ params }: PageProps<'/[locale]/dev/components/server'>) {
  const { locale } = await params;
  const ar = locale !== 'en';
  const tx = (a: string, e: string) => (ar ? a : e);
  const rows = [
    { id: '1', name: tx('صالون الأصالة', 'Al Asala Salon'), district: tx('الملقا', 'Al Malqa') },
    { id: '2', name: tx('باربر هاوس', 'Barber House'), district: tx('حطين', 'Hittin') },
  ];

  return (
    <PublicShell>
      <div className="mx-auto flex max-w-[1280px] flex-col gap-6 px-4 py-8 md:px-6">
        <h1 className="text-h1 font-bold text-navy-900">{tx('مكونات الخادم', 'Server components')}</h1>
        <div className="grid grid-cols-1 gap-5 md:grid-cols-2 xl:grid-cols-3">
          <Block title={tx('الأزرار والشارات', 'Buttons & badges')}>
            <div className="flex flex-wrap gap-2">
              <Button>{tx('احجز الآن', 'Book now')}</Button>
              <ButtonLink href="/search" variant="outline">
                {tx('استكشف', 'Explore')}
              </ButtonLink>
              <IconButton icon="heart" label={tx('المفضلة', 'Favourite')} variant="outline" />
            </div>
            <div className="flex flex-wrap gap-2">
              <StatusBadge kind="booking" status="Confirmed" />
              <StatusBadge kind="subscription" status="ExpiringSoon" />
              <Badge tone="info">{tx('معلومة', 'Info')}</Badge>
              <TagChip>{tx('لحية', 'Beard')}</TagChip>
            </div>
            <RatingStars value={4.6} count={158} />
          </Block>

          <Block title={tx('البطاقات', 'Cards')}>
            <ShopCard
              shop={{
                href: '/shops/barber-house',
                name: tx('باربر هاوس', 'Barber House'),
                verified: false,
                rating: 4.7,
                reviewCount: 82,
                district: tx('حطين', 'Hittin'),
                distanceKm: 3.1,
                openingLabel: tx('يفتح ٢:٠٠ م', 'Opens 2:00 pm'),
                isOpen: false,
                fromPrice: 40,
              }}
            />
            <AppointmentCard
              appointment={{
                href: '/account/bookings/demo',
                dayNumber: '20',
                monthLabel: tx('سبتمبر', 'Sep'),
                serviceName: tx('حلاقة شعر', 'Haircut'),
                status: 'Pending',
                shopName: tx('باربر هاوس', 'Barber House'),
                professionalName: tx('راكان المطيري', 'Rakan Almutairi'),
                timeRange: tx('٤:٠٠ م — ٤:٣٠ م', '4:00 pm — 4:30 pm'),
              }}
            />
            <KpiTile label={tx('المحلات النشطة', 'Active shops')} value="114" icon="store" />
          </Block>

          <Block title={tx('الاختيار بدون JavaScript', 'Selection without JavaScript')}>
            <ServiceOption
              name="server-service"
              value="haircut"
              defaultChecked
              title={tx('حلاقة شعر', 'Haircut')}
              price={40}
              durationMinutes={30}
            />
            <RadioCard name="server-service" value="beard">
              {tx('تهذيب لحية', 'Beard trim')}
            </RadioCard>
            <ProfessionalOption
              name="server-pro"
              value="p1"
              displayName={tx('راكان المطيري', 'Rakan Almutairi')}
              rating={4.8}
              reviewCount={64}
            />
          </Block>

          <Block title={tx('الرسوم ورمز QR', 'Charts & QR')}>
            <RatingDistribution counts={{ 5: 60, 4: 15, 3: 5, 2: 1, 1: 1 }} />
            <BarChart
              title={tx('الحجوزات حسب اليوم', 'Bookings by day')}
              labelHeader={tx('اليوم', 'Day')}
              valueHeader={tx('الحجوزات', 'Bookings')}
              data={[
                { label: '1', value: 12 },
                { label: '2', value: 18 },
                { label: '3', value: 9 },
              ]}
            />
            <QrCard
              name={tx('باربر هاوس', 'Barber House')}
              url="trimme.sa/s/barber-house"
              downloadHref="/dev/components"
            />
          </Block>

          <Block title={tx('البيانات والتنقل', 'Data & navigation')}>
            <Breadcrumb
              items={[{ label: tx('الإدارة', 'Admin'), href: '/admin' }, { label: tx('المحلات', 'Shops') }]}
            />
            <LinkTabs
              label={tx('الأقسام', 'Sections')}
              tabs={[
                { href: '/dev/components/server', label: tx('الخدمات', 'Services'), active: true },
                {
                  href: '/dev/components/server?tab=reviews',
                  label: tx('التقييمات', 'Reviews'),
                  active: false,
                },
              ]}
            />
            <ResponsiveTable
              caption={tx('المحلات', 'Shops')}
              rows={rows}
              rowKey={(row) => row.id}
              columns={[
                { key: 'name', header: tx('المحل', 'Shop'), cell: (row) => row.name, mobile: 'primary' },
                { key: 'district', header: tx('الحي', 'District'), cell: (row) => row.district },
              ]}
            />
            <Pagination
              page={2}
              pageSize={10}
              total={48}
              hrefForPage={(page) => `/dev/components/server?page=${page}`}
            />
            <Timeline items={[{ id: 'a', title: tx('تم إنشاء الحجز', 'Booking created'), meta: '4:10' }]} />
          </Block>

          <Block title={tx('الحالات', 'States')}>
            <Avatar name={tx('راكان المطيري', 'Rakan Almutairi')} />
            <EmptyState icon="pin" title={tx('لا توجد محلات قريبة', 'No shops nearby')} />
            <InlineAlert tone="warning" title={tx('الاشتراك ينتهي قريباً', 'Subscription expiring soon')} />
            <Skeleton className="h-3 w-1/2" />
            <SkeletonList label={tx('جارٍ التحميل', 'Loading')} rows={1} />
            <PermissionDenied />
            <ExpiredSession signInHref="/auth/sign-in" />
            <ErrorState />
          </Block>
        </div>
      </div>
    </PublicShell>
  );
}

function Block({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex min-w-0 flex-col gap-4 rounded-section bg-surface p-[22px] shadow-e1">
      <h2 className="text-h3 font-bold text-text-primary">{title}</h2>
      {children}
    </section>
  );
}
