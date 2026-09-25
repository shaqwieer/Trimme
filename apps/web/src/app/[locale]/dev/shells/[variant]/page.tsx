import { notFound } from 'next/navigation';
import { CustomerShell } from '@/components/shell/CustomerShell';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { Ltr } from '@/components/text/Ltr';
import { formatDate, formatDistanceKm, formatPrice, formatRating, formatTime } from '@/lib/i18n/format';

const SAMPLE_INSTANT = '2026-09-18T14:30:00Z';
const variants = ['customer', 'shop', 'admin'] as const;
type Variant = (typeof variants)[number];

const isVariant = (value: string): value is Variant => (variants as readonly string[]).includes(value);

/** Development preview of the layout shells with sample content (not a product route). */
export default async function ShellPreview({ params }: PageProps<'/[locale]/dev/shells/[variant]'>) {
  const { locale, variant } = await params;
  if (!isVariant(variant)) {
    notFound();
  }
  const lang = locale === 'en' ? 'en' : 'ar';

  const sample = (
    <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      {[1, 2, 3].map((n) => (
        <article key={n} className="rounded-card border border-border bg-surface p-5 shadow-e1">
          <h2 className="text-h3 font-bold text-navy-900">
            {lang === 'ar' ? 'صالون الأصالة للحلاقة' : 'Al Asala Barbers'}
          </h2>
          <p className="text-caption text-text-secondary">
            {formatDate(SAMPLE_INSTANT, lang)} · {formatTime(SAMPLE_INSTANT, lang)}
          </p>
          <p className="mt-2 flex flex-wrap gap-3 text-caption text-text-strong">
            <span className="font-latin font-semibold">{formatPrice(85, lang)}</span>
            <span>{formatDistanceKm(2.4, lang)}</span>
            <span>★ {formatRating(4.8, lang)}</span>
            <Ltr className="font-latin">+966 51 234 5678</Ltr>
          </p>
        </article>
      ))}
    </div>
  );

  if (variant === 'customer') {
    return (
      <CustomerShell>
        <div className="mx-auto max-w-[1280px] px-4 py-6 md:px-6">{sample}</div>
      </CustomerShell>
    );
  }

  const titles = {
    shop: { ar: 'النظرة التشغيلية', en: 'Overview' },
    admin: { ar: 'نظرة عامة على المنصة', en: 'Platform overview' },
  } as const;

  return (
    <DashboardShell variant={variant} title={titles[variant][lang]}>
      {sample}
    </DashboardShell>
  );
}
