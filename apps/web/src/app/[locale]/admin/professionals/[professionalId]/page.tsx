import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';
import { ProfessionalServicesForm } from '@/components/admin/AdminCatalog';
import { AdminFrame } from '@/components/admin/AdminFrame';
import {
  EditProfessionalForm,
  ProfessionalAvatarEditor,
  ProfessionalStatusActions,
  WhatsAppSettings,
} from '@/components/admin/ProfessionalForms';
import { ProfessionalStatusBadge } from '@/components/admin/ProfessionalStatusBadge';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
      <h2 className="text-h3 font-bold text-navy-900">{title}</h2>
      {children}
    </section>
  );
}

/**
 * Admin professional detail (DV-A07): profile, photo, status and WhatsApp settings. The shop is shown read-only; it is
 * fixed at creation and there is no way to change it (D-011, DV-S01).
 */
export default async function AdminProfessionalPage({
  params,
}: PageProps<'/[locale]/admin/professionals/[professionalId]'>) {
  const { locale, professionalId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminProfessionals' });
  const tServices = await getTranslations({ locale: lang, namespace: 'adminServices.professionalServices' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/professionals/${professionalId}`}
      title={t('title')}
      permission="Admin.Professionals.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data: professional, response } = await api.GET(
          '/api/v1/admin/professionals/{professionalId}',
          {
            params: { path: { professionalId } },
          },
        );
        if (response.status === 404 || response.status === 400) {
          return (
            <EmptyState icon="users" title={t('detail.notFoundTitle')} body={t('detail.notFoundBody')} />
          );
        }
        if (!professional) return <ErrorState />;

        const { data: services } = await api.GET('/api/v1/admin/professionals/{professionalId}/services', {
          params: { path: { professionalId } },
        });
        const name = lang === 'ar' ? professional.nameAr : professional.nameEn;
        const shopName = lang === 'ar' ? professional.shopNameAr : professional.shopNameEn;
        const canEdit = me.permissions.includes('Admin.Professionals.Edit');

        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <Breadcrumb
              items={[{ label: t('detail.back'), href: '/admin/professionals' }, { label: name }]}
            />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <div className="flex flex-wrap items-center gap-3">
                <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
                <ProfessionalStatusBadge status={professional.status} />
              </div>
              <dl className="grid gap-3 text-caption sm:grid-cols-2">
                <div>
                  <dt className="text-helper text-text-tertiary">{t('detail.shop')}</dt>
                  <dd className="font-semibold text-text-primary" data-testid="professional-shop">
                    <Link
                      href={`/admin/shops/${professional.shopId}`}
                      className="text-text-link hover:underline"
                    >
                      {shopName}
                    </Link>
                  </dd>
                  <dd className="text-helper text-text-tertiary">{t('detail.shopFixed')}</dd>
                </div>
              </dl>
              {me.permissions.includes('Admin.Professionals.Disable') && (
                <ProfessionalStatusActions id={professional.id} status={professional.status} />
              )}
            </section>

            {services && (
              <Card title={tServices('title')}>
                <ProfessionalServicesForm
                  data={services}
                  canAssign={me.permissions.includes('Admin.Professionals.AssignServices')}
                />
              </Card>
            )}

            <Card title={t('whatsapp.title')}>
              <WhatsAppSettings
                id={professional.id}
                whatsApp={professional.whatsApp}
                canManage={me.permissions.includes('Admin.Professionals.ManageWhatsApp')}
                canReveal={me.permissions.includes('Admin.Professionals.RevealWhatsApp')}
              />
            </Card>

            <Card title={t('detail.photo')}>
              {canEdit ? (
                <ProfessionalAvatarEditor
                  id={professional.id}
                  avatarUrl={professional.avatarUrl}
                  name={name}
                />
              ) : null}
            </Card>

            {canEdit && (
              <Card title={t('detail.profileTitle')}>
                <EditProfessionalForm professional={professional} />
              </Card>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
