import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';
import {
  EditProfessionalForm,
  ProfessionalAvatarEditor,
  ProfessionalStatusActions,
  WhatsAppSettings,
} from '@/components/admin/ProfessionalForms';
import { ProfessionalStatusBadge } from '@/components/admin/ProfessionalStatusBadge';
import { ProfessionalServicesForm } from '@/components/catalog/ProfessionalServicesForm';
import { BarberLoginControls } from '@/components/shop/BarberLoginControls';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, ErrorState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';
import { SaveGroup } from '@/components/ui/SaveGroup';

export const metadata: Metadata = { robots: { index: false, follow: false } };

function Card({ title, children, testId }: { title: string; children: ReactNode; testId?: string }) {
  return (
    <section
      className="flex flex-col gap-4 rounded-card border border-border bg-surface p-5 shadow-e1 md:p-6"
      data-testid={testId}
    >
      <h2 className="text-h3 font-bold text-navy-900">{title}</h2>
      {children}
    </section>
  );
}

/**
 * One of the owner's barbers (D-146): their login, the services they do, their WhatsApp number (masked; it is also the
 * sign-in, so it can be changed but not removed), photo, profile and whether they take bookings. Another shop's barber
 * is not found. There is no change of shop (D-011) and no delete: a barber with history is turned off instead.
 */
export default async function ShopBarberPage({
  params,
}: PageProps<'/[locale]/shop/barbers/[professionalId]'>) {
  const { locale: raw, professionalId } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBarbers' });
  const tAdmin = await getTranslations({ locale, namespace: 'adminProfessionals' });
  const tServices = await getTranslations({ locale, namespace: 'adminServices.professionalServices' });

  return (
    <ShopFrame locale={locale} path={`/shop/barbers/${professionalId}`} title={t('detail.title')}>
      {async (me, shop) => {
        if (shop.status === 'Suspended') return <EmptyState icon="shield" title={t('detail.title')} />;
        if (!me.permissions.includes('Shop.Barbers.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const path = { params: { path: { professionalId } } };
        const { data: barber, response } = await api.GET('/api/v1/shop/professionals/{professionalId}', path);
        if (response.status === 404 || response.status === 400) {
          return <EmptyState icon="users" title={t('detail.notFound')} />;
        }
        if (!barber) return <ErrorState />;
        const [{ data: services }, { data: login }] = await Promise.all([
          api.GET('/api/v1/shop/professionals/{professionalId}/services', path),
          api.GET('/api/v1/shop/professionals/{professionalId}/login', path),
        ]);
        const name = localizedName(locale, barber.nameAr, barber.nameEn);

        return (
          <div className="flex max-w-[960px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('title'), href: '/shop/barbers' }, { label: name }]} />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-5 shadow-e1 md:p-6">
              <div className="flex flex-wrap items-center gap-3">
                <h2 className="text-h2 font-bold text-navy-900" data-testid="barber-name">
                  {name}
                </h2>
                <ProfessionalStatusBadge status={barber.status} />
              </div>
              <ProfessionalStatusActions id={barber.id} status={barber.status} scope="shop" />
            </section>

            {barber.status === 'Active' && login && (
              <Card title={t('detail.login')}>
                <BarberLoginControls scope="shop" professionalId={barber.id} name={name} login={login} />
              </Card>
            )}

            {/* One save for the services and the profile (D-155, the client's 3-8). */}
            <SaveGroup testId="barber-save">
              {services && (
                <Card title={tServices('title')}>
                  <ProfessionalServicesForm scope="shop" data={services} canAssign />
                </Card>
              )}

              <Card title={tAdmin('whatsapp.title')}>
                <WhatsAppSettings
                  id={barber.id}
                  whatsApp={barber.whatsApp}
                  canManage
                  canReveal={false}
                  scope="shop"
                />
              </Card>

              <Card title={tAdmin('detail.photo')}>
                <ProfessionalAvatarEditor
                  id={barber.id}
                  avatarUrl={barber.avatarUrl}
                  name={name}
                  scope="shop"
                />
              </Card>

              <Card title={tAdmin('detail.profileTitle')}>
                <EditProfessionalForm professional={barber} scope="shop" />
              </Card>
            </SaveGroup>
          </div>
        );
      }}
    </ShopFrame>
  );
}
