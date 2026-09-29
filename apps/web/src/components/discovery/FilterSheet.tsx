'use client';

import { useEffect, useRef, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { Switch } from '@/components/ui/inputs';
import { Sheet } from '@/components/ui/overlays';
import { RangeSlider } from '@/components/ui/RangeSlider';
import { RadioCard } from '@/components/ui/selection';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import type { ChosenLocation } from '@/lib/discovery/location';
import { filtersToQuery, type SearchFilters, type SearchSort, toApiQuery } from '@/lib/discovery/search';
import { cn } from '@/lib/cn';
import { type AppLocale, formatPrice } from '@/lib/i18n/format';

type CategoryOption = { id: string; name: string };

/**
 * The filter drawer (c-map "FILTER DRAWER" 1166–1220): sort, service, price range and three switches. One filter state
 * serves the list and the map (mapRules #3): applying writes it to the URL. The live result count is shown in the apply
 * button before the drawer closes; Esc, the scrim and the close button dismiss it (focus returns to the chip).
 */
export function FilterSheet({
  filters,
  categories,
  priceRange,
  location,
  activeCount,
}: {
  filters: SearchFilters;
  categories: CategoryOption[];
  priceRange: { min: number; max: number } | null;
  location: ChosenLocation | null;
  activeCount: number;
}) {
  const t = useTranslations('filters');
  const tSearch = useTranslations('search');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<SearchFilters>(filters);
  const [count, setCount] = useState<number | null>(null);
  const request = useRef(0);

  const bounds = priceRange
    ? {
        min: Math.floor(priceRange.min / 5) * 5,
        max: Math.max(Math.ceil(priceRange.max / 5) * 5, Math.floor(priceRange.min / 5) * 5 + 5),
      }
    : null;
  const price: [number, number] = bounds
    ? [draft.minPrice ?? bounds.min, draft.maxPrice ?? bounds.max]
    : [0, 0];

  function openChange(next: boolean) {
    if (next) setDraft(filters);
    setOpen(next);
  }

  // Live count for the apply button (debounced; one search with a page of one).
  useEffect(() => {
    if (!open) return;
    const id = ++request.current;
    const timer = window.setTimeout(async () => {
      const { data } = await browserApi.GET('/api/v1/public/shops/search', {
        params: { query: toApiQuery({ ...draft, page: 1 }, location, 1) },
      });
      if (id === request.current) setCount(data ? data.total : null);
    }, 350);
    return () => window.clearTimeout(timer);
  }, [draft, location, open]);

  function apply() {
    setOpen(false);
    router.push({ pathname: '/search', query: filtersToQuery({ ...draft, page: 1 }) });
  }

  function clear() {
    setDraft({
      ...filters,
      category: null,
      openNow: false,
      verified: false,
      today: false,
      minPrice: null,
      maxPrice: null,
      sort: location ? 'nearest' : 'rating',
      page: 1,
    });
  }

  const sortOptions: Array<{ value: SearchSort; label: string; disabled?: boolean }> = [
    { value: 'nearest', label: t('sortNearest'), disabled: !location },
    { value: 'rating', label: t('sortRating') },
    { value: 'earliest', label: t('sortEarliest') },
  ];

  return (
    <Sheet
      open={open}
      onOpenChange={openChange}
      side="bottom"
      title={t('title')}
      closeLabel={t('close')}
      className="md:mx-auto md:max-w-[560px]"
      trigger={
        <button
          type="button"
          className={cn(
            'inline-flex min-h-11 items-center gap-1.5 rounded-pill px-4 text-label font-bold',
            activeCount > 0 ? 'bg-navy-900 text-on-navy' : 'border border-border bg-surface text-text-strong',
          )}
        >
          <Icon name="filter" className="size-4" />
          {activeCount > 0 ? tSearch('filtersCount', { count: activeCount }) : tSearch('filters')}
        </button>
      }
    >
      <div className="flex flex-col gap-6 px-5 pt-4 pb-3">
        <fieldset className="flex min-w-0 flex-col gap-2">
          <legend className="mb-2 text-label font-bold text-text-primary">{t('sort')}</legend>
          {sortOptions.map((option) => (
            <RadioCard
              key={option.value}
              name="sort"
              value={option.value}
              checked={draft.sort === option.value}
              disabled={option.disabled}
              onChange={() => setDraft((d) => ({ ...d, sort: option.value }))}
            >
              {option.label}
            </RadioCard>
          ))}
        </fieldset>

        {categories.length > 0 && (
          <fieldset className="flex min-w-0 flex-col gap-2">
            <legend className="mb-2 text-label font-bold text-text-primary">{t('service')}</legend>
            <div className="flex flex-wrap gap-2">
              {[{ id: null as string | null, name: t('anyService') }, ...categories].map((category) => {
                const selected = draft.category === category.id;
                return (
                  <button
                    key={category.id ?? 'any'}
                    type="button"
                    aria-pressed={selected}
                    onClick={() => setDraft((d) => ({ ...d, category: category.id }))}
                    className={cn(
                      'inline-flex min-h-11 items-center rounded-pill px-4 text-label font-bold',
                      selected
                        ? 'bg-navy-900 text-on-navy'
                        : 'border border-border bg-surface text-text-strong hover:border-brand-500',
                    )}
                  >
                    {category.name}
                  </button>
                );
              })}
            </div>
          </fieldset>
        )}

        {bounds && bounds.max > bounds.min && (
          <RangeSlider
            label={t('price')}
            min={bounds.min}
            max={bounds.max}
            step={5}
            value={price}
            thumbLabels={[t('minPrice'), t('maxPrice')]}
            format={(value) => formatPrice(value, locale)}
            onValueChange={([min, max]) =>
              setDraft((d) => ({
                ...d,
                minPrice: min <= bounds.min ? null : min,
                maxPrice: max >= bounds.max ? null : max,
              }))
            }
          />
        )}

        <div className="flex flex-col divide-y divide-border-row">
          <Switch
            label={t('openNow')}
            checked={draft.openNow}
            onCheckedChange={(v) => setDraft((d) => ({ ...d, openNow: v }))}
            className="justify-between py-1"
          />
          <Switch
            label={t('verified')}
            checked={draft.verified}
            onCheckedChange={(v) => setDraft((d) => ({ ...d, verified: v }))}
            className="justify-between py-1"
          />
          <Switch
            label={t('today')}
            checked={draft.today}
            onCheckedChange={(v) => setDraft((d) => ({ ...d, today: v }))}
            className="justify-between py-1"
          />
        </div>
      </div>
      <div className="sticky bottom-0 flex gap-3 border-t border-border bg-surface px-5 py-4">
        <Button type="button" variant="outline" size="lg" onClick={clear}>
          {t('clear')}
        </Button>
        <Button type="button" variant="primary" size="lg" fullWidth onClick={apply} aria-live="polite">
          {count === null ? t('applyPending') : t('apply', { count })}
        </Button>
      </div>
    </Sheet>
  );
}
