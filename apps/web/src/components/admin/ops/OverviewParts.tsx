import { useLocale, useTranslations } from 'next-intl';
import { share } from '@/lib/admin/admin';
import { type AppLocale, formatDayNumber, formatNumber } from '@/lib/i18n/format';
import { toUtcNoon } from '@/lib/i18n/localDate';

export type TrendDay = { date: string; completed: number; cancelledOrNoShow: number; other: number };

/**
 * Bookings per day over 14 days (a-overview 2721–2733): completed at the base, cancelled or no-show on top, and the
 * rest (still pending, confirmed or in the chair) above. Three series share one legend; the peak day's total is
 * labelled directly and a data table serves assistive technology (dataviz rules, D-049).
 */
export function BookingTrend({ days }: { days: TrendDay[] }) {
  const t = useTranslations('adminOverview.trend');
  const locale = useLocale() as AppLocale;
  const totals = days.map((d) => d.completed + d.cancelledOrNoShow + d.other);
  const max = Math.max(...totals, 1);
  const peak = totals.indexOf(Math.max(...totals));
  const series = [
    { key: 'completed', className: 'bg-navy-900', label: t('completed') },
    { key: 'cancelledOrNoShow', className: 'bg-brand-500', label: t('cancelledOrNoShow') },
    { key: 'other', className: 'bg-brand-300', label: t('other') },
  ] as const;

  return (
    <figure className="flex flex-col gap-3" data-testid="booking-trend">
      <figcaption className="flex flex-wrap items-center gap-x-4 gap-y-1">
        {series.map((s) => (
          <span key={s.key} className="inline-flex items-center gap-1.5 text-helper text-text-secondary">
            <span aria-hidden="true" className={`size-2.5 rounded-[3px] ${s.className}`} />
            {s.label}
          </span>
        ))}
      </figcaption>
      <div aria-hidden="true" className="flex h-[140px] items-end gap-1 border-b border-border">
        {days.map((day, index) => {
          const height = (totals[index]! / max) * 85;
          return (
            <div
              key={day.date}
              className="group relative flex h-full flex-1 flex-col items-center justify-end"
            >
              <span
                style={{ bottom: `calc(${height}% + 4px)` }}
                className={`pointer-events-none absolute font-latin text-[0.6875rem] font-semibold text-text-strong ${
                  index === peak && totals[index]! > 0 ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'
                }`}
              >
                {formatNumber(totals[index]!, locale)}
              </span>
              <span
                className="flex w-full max-w-7 flex-col-reverse overflow-hidden rounded-t-[4px]"
                style={{ height: `${height}%` }}
              >
                {series.map((s) =>
                  day[s.key] > 0 ? (
                    <span
                      key={s.key}
                      className={s.className}
                      style={{ height: `${(day[s.key] / totals[index]!) * 100}%` }}
                    />
                  ) : null,
                )}
              </span>
            </div>
          );
        })}
      </div>
      <div aria-hidden="true" className="flex gap-1">
        {days.map((day) => (
          <span key={day.date} className="flex-1 text-center font-latin text-[0.625rem] text-text-tertiary">
            {formatDayNumber(toUtcNoon(day.date), locale, 'UTC')}
          </span>
        ))}
      </div>
      {/* sr-only on a wrapper: a table never shrinks to the 1px box, so on its own it can overflow the page (RTL). */}
      <div className="sr-only">
        <table>
          <caption>{t('table')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('date')}</th>
              {series.map((s) => (
                <th key={s.key} scope="col">
                  {s.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {days.map((day) => (
              <tr key={day.date}>
                <th scope="row">{day.date}</th>
                {series.map((s) => (
                  <td key={s.key}>{formatNumber(day[s.key], locale)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </figure>
  );
}

/** A ranked list with a proportional bar per row (a-overview "most booked services", 2735–2748). */
export function RankedBars({
  rows,
  label,
}: {
  rows: Array<{ key: string; name: string; value: number }>;
  label: string;
}) {
  const locale = useLocale() as AppLocale;
  const max = Math.max(...rows.map((r) => r.value), 0);
  return (
    <ul aria-label={label} className="flex flex-col gap-3">
      {rows.map((row) => (
        <li key={row.key} className="flex flex-col gap-1.5">
          <div className="flex items-baseline justify-between gap-3 text-caption">
            <span className="min-w-0 truncate font-semibold text-text-primary">{row.name}</span>
            <span className="font-latin font-bold text-text-strong">{formatNumber(row.value, locale)}</span>
          </div>
          <span aria-hidden="true" className="h-2 overflow-hidden rounded-full bg-brand-100">
            <span
              className="block h-full rounded-full bg-brand-600"
              style={{ width: `${share(row.value, max)}%` }}
            />
          </span>
        </li>
      ))}
    </ul>
  );
}
