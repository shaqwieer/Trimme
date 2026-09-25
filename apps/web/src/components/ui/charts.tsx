import type { ReactNode } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';
import { ButtonLink } from './Button';
import { Icon } from './icons';

/* ---------------------------------------------------------------- Rating distribution */

type RatingDistributionProps = {
  /** Review counts per star value (5 → 1). */
  counts: Record<1 | 2 | 3 | 4 | 5, number>;
};

/** Rating distribution bars (design 3680–3683): 4–5 stars in brand blue, 1–3 in neutral. */
export function RatingDistribution({ counts }: RatingDistributionProps) {
  const t = useTranslations('ui.ratingDistribution');
  const locale = useLocale() as AppLocale;
  const total = Object.values(counts).reduce((sum, n) => sum + n, 0) || 1;

  return (
    <ul aria-label={t('label')} className="flex flex-col gap-1.5">
      {([5, 4, 3, 2, 1] as const).map((stars) => (
        <li key={stars} className="flex items-center gap-2">
          <span className="sr-only">{t('row', { stars, count: formatNumber(counts[stars], locale) })}</span>
          <span
            aria-hidden="true"
            className="w-3 font-latin text-[0.6875rem] font-semibold text-text-tertiary"
          >
            {stars}
          </span>
          <span aria-hidden="true" className="h-1.5 flex-1 overflow-hidden rounded-xs bg-bg-subtle">
            <span
              className={cn('block h-full rounded-xs', stars >= 4 ? 'bg-brand-500' : 'bg-border-strong')}
              style={{ width: `${(counts[stars] / total) * 100}%` }}
            />
          </span>
          <span
            aria-hidden="true"
            className="w-8 text-end font-latin text-[0.6875rem] font-medium text-text-tertiary"
          >
            {formatNumber(counts[stars], locale)}
          </span>
        </li>
      ))}
    </ul>
  );
}

/* ---------------------------------------------------------------- Bar chart */

export type BarDatum = { label: string; value: number };

type BarChartProps = {
  title: ReactNode;
  subtitle?: ReactNode;
  data: BarDatum[];
  /** Column headings of the accessible data table. */
  labelHeader: string;
  valueHeader: string;
  /** Values at or above this share of the maximum are emphasised in navy (design: >80%). */
  emphasisThreshold?: number;
};

/**
 * Single-series bar chart (design 636–651, "bookings by hour of day"). Per the dataviz rules:
 * one series → no legend (the title names it); thin bars with 4px rounded data-ends on a baseline,
 * 2px gaps; values appear on hover, the peak is direct-labelled, and a data table serves assistive
 * technology. Regular bars use brand-500 (2.84:1) instead of the design's #B9CBDD (1.62:1) — the
 * contrast relief is the label + table (D-049).
 */
export function BarChart({
  title,
  subtitle,
  data,
  labelHeader,
  valueHeader,
  emphasisThreshold = 0.8,
}: BarChartProps) {
  const t = useTranslations('ui.chart');
  const locale = useLocale() as AppLocale;
  const max = Math.max(...data.map((d) => d.value), 1);
  const peak = data.reduce(
    (best, d) => (d.value > best.value ? d : best),
    data[0] ?? { label: '', value: 0 },
  );

  return (
    <figure className="flex flex-col gap-3">
      <figcaption className="flex items-baseline justify-between gap-2">
        <span className="text-[0.875rem] font-bold text-text-primary">{title}</span>
        {subtitle && <span className="text-helper text-text-tertiary">{subtitle}</span>}
      </figcaption>
      <div aria-hidden="true" className="flex h-[112px] items-end gap-0.5 border-b border-border">
        {data.map((d) => {
          const emphasised = d.value / max >= emphasisThreshold;
          const isPeak = d === peak;
          // Bars use at most 85% of the plot height so the value label always fits above the peak.
          const height = Math.max((d.value / max) * 85, 4);
          return (
            <div
              key={d.label}
              className="group relative flex h-full flex-1 flex-col items-center justify-end"
            >
              <span
                style={{ bottom: `calc(${height}% + 4px)` }}
                className={cn(
                  'pointer-events-none absolute font-latin text-[0.6875rem] font-semibold whitespace-nowrap text-text-strong',
                  isPeak ? 'opacity-100' : 'opacity-0 group-hover:opacity-100',
                )}
              >
                {formatNumber(d.value, locale)}
              </span>
              <span
                className={cn(
                  'w-full max-w-7 rounded-t-[4px] transition-colors',
                  emphasised ? 'bg-navy-900' : 'bg-brand-500 group-hover:bg-brand-700',
                )}
                style={{ height: `${height}%` }}
              />
            </div>
          );
        })}
      </div>
      <div aria-hidden="true" className="flex gap-0.5">
        {data.map((d) => (
          <span
            key={d.label}
            className="flex-1 text-center font-latin text-[0.625rem] font-medium text-text-tertiary"
          >
            {d.label}
          </span>
        ))}
      </div>
      <table className="sr-only">
        <caption>
          {t('table')}: {title}
        </caption>
        <thead>
          <tr>
            <th scope="col">{labelHeader}</th>
            <th scope="col">{valueHeader}</th>
          </tr>
        </thead>
        <tbody>
          {data.map((d) => (
            <tr key={d.label}>
              <th scope="row">{d.label}</th>
              <td>
                {formatNumber(d.value, locale)}
                {d === peak ? ` (${t('peak')})` : ''}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}

/* ---------------------------------------------------------------- QR card */

type QrCardProps = {
  name: string;
  /** Public short link, e.g. trimme.sa/s/alasalah-malqa (shown LTR). */
  url: string;
  downloadHref: string;
  /** Rendered QR image (Phase 16); until then the design's navy QR tile is shown. */
  image?: ReactNode;
  share?: ReactNode;
};

/** QR card (design 655–663): navy code tile, shop name, short link, download + share. */
export function QrCard({ name, url, downloadHref, image, share }: QrCardProps) {
  const t = useTranslations('ui.qr');
  return (
    <div className="flex items-center gap-4 rounded-card border border-border bg-surface p-4">
      <div
        role={image ? undefined : 'img'}
        aria-label={image ? undefined : t('code', { name })}
        className="flex size-24 shrink-0 items-center justify-center rounded-button bg-navy-900 text-on-navy-accent"
      >
        {image ?? <Icon name="qr" className="size-14" />}
      </div>
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <span className="truncate text-[0.9375rem] font-bold text-text-primary">{name}</span>
        <bdi dir="ltr" className="truncate font-latin text-badge font-medium text-text-tertiary">
          {url}
        </bdi>
        <div className="flex flex-wrap gap-2">
          <ButtonLink href={downloadHref} size="xs" icon="download">
            {t('download')}
          </ButtonLink>
          {share}
        </div>
      </div>
    </div>
  );
}
