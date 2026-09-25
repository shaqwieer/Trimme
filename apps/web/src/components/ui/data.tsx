import type { ReactNode } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';
import { Icon } from './icons';

/* ---------------------------------------------------------------- Responsive table */

export type Column<Row> = {
  key: string;
  header: ReactNode;
  cell: (row: Row) => ReactNode;
  /** Grid track on desktop, e.g. "2fr" or "40px". */
  width?: string;
  /** On phones: `primary` = card title, `meta` = labelled line, `hidden` = omitted. */
  mobile?: 'primary' | 'meta' | 'hidden';
  align?: 'start' | 'end';
};

type ResponsiveTableProps<Row> = {
  caption: string;
  columns: Column<Row>[];
  rows: Row[];
  rowKey: (row: Row) => string;
  /** Trailing per-row actions (e.g. an overflow menu); shown on both layouts. */
  actions?: (row: Row) => ReactNode;
  empty?: ReactNode;
};

/**
 * Data table (design 743–774) that becomes a card list below 768px (spec §5). Both layouts are
 * rendered; CSS shows one, and the hidden one is removed from the accessibility tree by display:none.
 */
export function ResponsiveTable<Row>({
  caption,
  columns,
  rows,
  rowKey,
  actions,
  empty,
}: ResponsiveTableProps<Row>) {
  const t = useTranslations('ui');
  if (rows.length === 0 && empty) return <>{empty}</>;

  const primary = columns.find((c) => c.mobile === 'primary') ?? columns[0];
  const metas = columns.filter((c) => c !== primary && c.mobile !== 'hidden');

  return (
    <>
      <div className="hidden overflow-hidden rounded-button border border-border bg-surface md:block">
        <table className="w-full border-collapse text-start">
          <caption className="sr-only">{caption}</caption>
          <thead className="bg-bg-page">
            <tr className="border-b border-border">
              {columns.map((column) => (
                <th
                  key={column.key}
                  scope="col"
                  style={column.width ? { width: column.width } : undefined}
                  className={cn(
                    'px-4 py-3 font-latin text-badge font-bold text-text-secondary',
                    column.align === 'end' ? 'text-end' : 'text-start',
                  )}
                >
                  {column.header}
                </th>
              ))}
              {actions && (
                <th scope="col" className="w-12 px-4 py-3">
                  <span className="sr-only">{t('actions')}</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={rowKey(row)} className="border-b border-border-row last:border-b-0">
                {columns.map((column, index) => {
                  const Cell = index === 0 ? 'th' : 'td';
                  return (
                    <Cell
                      key={column.key}
                      scope={index === 0 ? 'row' : undefined}
                      className={cn(
                        'px-4 py-3 text-[0.84375rem] text-text-strong',
                        index === 0 && 'font-bold text-text-primary',
                        column.align === 'end' ? 'text-end' : 'text-start',
                      )}
                    >
                      {column.cell(row)}
                    </Cell>
                  );
                })}
                {actions && <td className="px-2 py-1 text-end">{actions(row)}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul aria-label={caption} className="flex flex-col gap-2.5 md:hidden">
        {rows.map((row) => (
          <li
            key={rowKey(row)}
            className="flex items-start gap-3 rounded-card border border-border bg-surface p-4 shadow-e1"
          >
            <div className="min-w-0 flex-1">
              {primary && <p className="font-bold text-text-primary">{primary.cell(row)}</p>}
              <dl className="mt-1.5 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-helper">
                {metas.map((column) => (
                  <div key={column.key} className="contents">
                    <dt className="text-text-tertiary">{column.header}</dt>
                    <dd className="text-text-strong">{column.cell(row)}</dd>
                  </div>
                ))}
              </dl>
            </div>
            {actions?.(row)}
          </li>
        ))}
      </ul>
    </>
  );
}

/* ---------------------------------------------------------------- Pagination */

type PaginationProps = {
  page: number;
  pageSize: number;
  total: number;
  /** Builds the link for a page (pages are URL state, so they are shareable and crawlable). */
  hrefForPage: (page: number) => string;
};

/** Pagination (design 758–774): summary + previous / numbered / next; arrows mirror in RTL. */
export function Pagination({ page, pageSize, total, hrefForPage }: PaginationProps) {
  const t = useTranslations('ui.pagination');
  const locale = useLocale() as AppLocale;
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);
  const pages = pageWindow(page, pageCount);

  const cell =
    'inline-flex size-11 items-center justify-center rounded-sm border border-border-input font-latin text-label font-bold';

  return (
    <nav aria-label={t('label')} className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-helper text-text-secondary">
        {t('summary', {
          from: formatNumber(from, locale),
          to: formatNumber(to, locale),
          total: formatNumber(total, locale),
        })}
      </p>
      <ul className="flex items-center gap-1.5">
        <li>
          {page > 1 ? (
            <Link
              href={hrefForPage(page - 1)}
              aria-label={t('previous')}
              className={cn(cell, 'text-text-strong hover:border-brand-500')}
            >
              <Icon name="chevL" className="size-4" />
            </Link>
          ) : (
            <span aria-disabled="true" className={cn(cell, 'text-text-disabled')}>
              <Icon name="chevL" className="size-4" />
              <span className="sr-only">{t('previous')}</span>
            </span>
          )}
        </li>
        {pages.map((p, index) =>
          p === 'gap' ? (
            <li key={`gap-${index}`} aria-hidden="true" className="px-1 text-text-tertiary">
              …
            </li>
          ) : (
            <li key={p}>
              <Link
                href={hrefForPage(p)}
                aria-label={t('page', { page: p })}
                aria-current={p === page ? 'page' : undefined}
                className={cn(
                  cell,
                  p === page
                    ? 'border-navy-900 bg-navy-900 text-on-navy'
                    : 'text-text-strong hover:border-brand-500',
                )}
              >
                {formatNumber(p, locale)}
              </Link>
            </li>
          ),
        )}
        <li>
          {page < pageCount ? (
            <Link
              href={hrefForPage(page + 1)}
              aria-label={t('next')}
              className={cn(cell, 'text-text-strong hover:border-brand-500')}
            >
              <Icon name="chevR" className="size-4" />
            </Link>
          ) : (
            <span aria-disabled="true" className={cn(cell, 'text-text-disabled')}>
              <Icon name="chevR" className="size-4" />
              <span className="sr-only">{t('next')}</span>
            </span>
          )}
        </li>
      </ul>
    </nav>
  );
}

/** At most 7 entries: first, last, current ±1, with gaps. */
export function pageWindow(page: number, pageCount: number): Array<number | 'gap'> {
  if (pageCount <= 7) return Array.from({ length: pageCount }, (_, i) => i + 1);
  const set = new Set([1, pageCount, page - 1, page, page + 1].filter((p) => p >= 1 && p <= pageCount));
  const sorted = [...set].sort((a, b) => a - b);
  const result: Array<number | 'gap'> = [];
  sorted.forEach((p, i) => {
    if (i > 0 && p - (sorted[i - 1] ?? p) > 1) result.push('gap');
    result.push(p);
  });
  return result;
}

/* ---------------------------------------------------------------- Breadcrumb */

/** Breadcrumb (design 745–750). The last item is the current page; separators mirror in RTL. */
export function Breadcrumb({ items }: { items: Array<{ label: ReactNode; href?: string }> }) {
  const t = useTranslations('ui');
  return (
    <nav aria-label={t('breadcrumb')}>
      <ol className="flex flex-wrap items-center gap-2 text-label text-text-secondary">
        {items.map((item, index) => {
          const last = index === items.length - 1;
          return (
            <li key={index} className="flex items-center gap-2">
              {item.href && !last ? (
                <Link href={item.href} className="hover:text-text-primary hover:underline">
                  {item.label}
                </Link>
              ) : (
                <span
                  aria-current={last ? 'page' : undefined}
                  className={cn(last && 'font-bold text-text-primary')}
                >
                  {item.label}
                </span>
              )}
              {!last && <Icon name="chevR" className="size-3.5 text-decorative-200" />}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/* ---------------------------------------------------------------- Timeline */

export type TimelineItem = {
  id: string;
  title: ReactNode;
  meta?: ReactNode;
  tone?: 'brand' | 'success' | 'warning' | 'danger' | 'neutral';
};

const timelineDots = {
  brand: 'bg-brand-500 shadow-[0_0_0_3px_var(--color-brand-100)]',
  success: 'bg-success-500 shadow-[0_0_0_3px_var(--color-success-50)]',
  warning: 'bg-warning-500 shadow-[0_0_0_3px_var(--color-warning-50)]',
  danger: 'bg-danger-500 shadow-[0_0_0_3px_var(--color-danger-50)]',
  neutral: 'bg-decorative-400 shadow-[0_0_0_3px_var(--color-bg-muted)]',
} as const;

/** Vertical timeline for booking history and audit activity (design 181–195). */
export function Timeline({ items }: { items: TimelineItem[] }) {
  return (
    <ol className="flex flex-col">
      {items.map((item, index) => (
        <li key={item.id} className="relative flex gap-3 pb-4 last:pb-0">
          {index < items.length - 1 && (
            <span aria-hidden="true" className="absolute start-[8px] top-4 h-full w-0.5 bg-border" />
          )}
          <span
            aria-hidden="true"
            className={cn(
              'relative ms-1 mt-1.5 size-2.5 shrink-0 rounded-full',
              timelineDots[item.tone ?? 'brand'],
            )}
          />
          <div className="flex flex-col gap-0.5">
            <span className="text-caption font-bold text-text-primary">{item.title}</span>
            {item.meta && <span className="text-helper text-text-tertiary">{item.meta}</span>}
          </div>
        </li>
      ))}
    </ol>
  );
}
