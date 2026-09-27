import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { PlanForm } from '@/components/subscriptions/PlanForm';
import { Breadcrumb } from '@/components/ui/data';
import { asLocale } from '@/i18n/routing';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** New subscription plan (SuperAdmin). It starts as a draft; publish it once it has a price. */
export default async function NewSubscriptionPlanPage({
  params,
}: PageProps<'/[locale]/admin/subscription-plans/new'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'subscriptionPlans' });

  return (
    <AdminFrame
      locale={locale}
      path="/admin/subscription-plans/new"
      title={t('add')}
      permission="SuperAdmin.SubscriptionPlans.Manage"
    >
      {() => (
        <div className="flex max-w-[1000px] flex-col gap-5">
          <Breadcrumb
            items={[{ label: t('detail.back'), href: '/admin/subscription-plans' }, { label: t('add') }]}
          />
          <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
            <PlanForm />
          </section>
        </div>
      )}
    </AdminFrame>
  );
}
