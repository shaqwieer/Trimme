'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { SelectField, Switch, TextField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import type { components } from '@/lib/api/schema';
import type { AppLocale } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { useScheduleErrors } from './errors';
import {
  fromWeekRows,
  type HoursInterval,
  intervalLabel,
  toMinutes,
  toWeekRows,
  WEEK,
  type Weekday,
  type WeekRows,
} from './time';

type ProfessionalHours = components['schemas']['ProfessionalHoursResponse'];

/**
 * The weekly hours grid (s-hours "دوام المحل الأسبوعي"): one row per day with an open switch and one or more
 * from–to ranges (a split shift adds a range). An end at or before the start closes after midnight. Times are
 * LTR-isolated inputs on the 5-minute grid.
 */
export function WeekHoursEditor({
  value,
  onChange,
  disabled,
  idPrefix,
}: {
  value: WeekRows;
  onChange: (next: WeekRows) => void;
  disabled?: boolean;
  idPrefix: string;
}) {
  const t = useTranslations('shopSchedule');
  const set = (day: Weekday, next: WeekRows[Weekday]) => onChange({ ...value, [day]: next });

  return (
    <ul className="flex flex-col divide-y divide-border-row" data-testid={`${idPrefix}-week`}>
      {WEEK.map((day) => {
        const entry = value[day];
        return (
          <li
            key={day}
            className="flex flex-col gap-2 py-3 md:flex-row md:items-start"
            data-testid={`${idPrefix}-${day}`}
          >
            <div className="flex min-w-[9.5rem] items-center gap-1">
              <Switch
                checked={entry.open}
                disabled={disabled}
                label={t('openOn', { day: t(`days.${day}`) })}
                hideLabel
                onCheckedChange={(open) => set(day, { ...entry, open })}
              />
              <span className="text-label font-bold text-text-primary">{t(`days.${day}`)}</span>
            </div>
            {entry.open ? (
              <div className="flex flex-1 flex-col gap-2">
                {entry.rows.map((row, index) => {
                  const from = toMinutes(row.start);
                  const to = toMinutes(row.end);
                  const overnight = from !== null && to !== null && to <= from && to !== 0;
                  return (
                    <div key={index} className="flex flex-wrap items-end gap-2">
                      <TextField
                        label={t('from')}
                        type="time"
                        step={300}
                        dir="ltr"
                        className="w-[8.5rem]"
                        value={row.start}
                        disabled={disabled}
                        onChange={(e) =>
                          set(day, {
                            ...entry,
                            rows: entry.rows.map((r, i) =>
                              i === index ? { ...r, start: e.target.value } : r,
                            ),
                          })
                        }
                      />
                      <TextField
                        label={t('to')}
                        type="time"
                        step={300}
                        dir="ltr"
                        className="w-[8.5rem]"
                        value={row.end}
                        disabled={disabled}
                        onChange={(e) =>
                          set(day, {
                            ...entry,
                            rows: entry.rows.map((r, i) => (i === index ? { ...r, end: e.target.value } : r)),
                          })
                        }
                      />
                      {overnight && (
                        <span className="pb-3 text-helper text-text-secondary">{t('nextDay')}</span>
                      )}
                      {!disabled && entry.rows.length > 1 && (
                        <IconButton
                          icon="trash"
                          label={t('removeRange', { day: t(`days.${day}`) })}
                          onClick={() =>
                            set(day, { ...entry, rows: entry.rows.filter((_, i) => i !== index) })
                          }
                        />
                      )}
                    </div>
                  );
                })}
                {!disabled && entry.rows.length < 4 && (
                  <button
                    type="button"
                    className="self-start text-caption font-semibold text-text-link hover:underline"
                    onClick={() =>
                      set(day, { ...entry, rows: [...entry.rows, { start: '17:00', end: '21:00' }] })
                    }
                  >
                    {t('addRange')}
                  </button>
                )}
              </div>
            ) : (
              <span className="text-caption text-text-secondary">{t('closed')}</span>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/** The shop's weekly opening hours: the outer limit for every professional (changes apply from now on). */
export function OpeningHoursCard({
  intervals,
  version,
  canManage,
}: {
  intervals: HoursInterval[];
  version: number | null;
  canManage: boolean;
}) {
  const t = useTranslations('shopSchedule');
  const router = useRouter();
  const [week, setWeek] = useState(() => toWeekRows(intervals));
  // The version to send: the one the last save returned, or the server's after a refresh. The form stays mounted
  // across refreshes (the saved message stays) and takes the server's values only when someone else changed them.
  const [current, setCurrent] = useState(version);
  const [seen, setSeen] = useState(version);
  if (seen !== version) {
    setSeen(version);
    // The refresh after our own save carries the version that save returned: keep any edits made since.
    if (version !== current) {
      setCurrent(version);
      setWeek(toWeekRows(intervals));
    }
  }
  const [pending, setPending] = useState(false);
  const [saved, setSaved] = useState(false);
  const { failure, fail, clear } = useScheduleErrors();

  const save = async () => {
    clear();
    setSaved(false);
    const result = fromWeekRows(week);
    if ('invalidDay' in result) {
      fail(t('invalidTimes', { day: t(`days.${result.invalidDay}`) }));
      return;
    }
    setPending(true);
    try {
      const hours = ensureOk(
        await browserApi.PUT('/api/v1/shop/schedule/opening-hours', {
          body: { intervals: result.intervals, version: current },
        }),
      );
      setCurrent(hours.version);
      setSaved(true);
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <section
      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
      data-testid="opening-hours"
    >
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-h3 font-bold text-text-primary">{t('hours.title')}</h2>
        <span className="text-helper text-text-tertiary">{t('hours.side')}</span>
      </header>
      <p className="text-caption text-text-secondary">{t('hours.body')}</p>
      <WeekHoursEditor
        idPrefix="shop-hours"
        value={week}
        onChange={setWeek}
        disabled={!canManage || pending}
      />
      {failure && <InlineAlert tone="danger" title={failure} />}
      {saved && <InlineAlert tone="success" title={t('hours.saved')} />}
      {canManage && (
        <Button className="self-start" loading={pending} onClick={() => void save()}>
          {t('hours.save')}
        </Button>
      )}
    </section>
  );
}

/**
 * A professional's own weekly hours (DV-A03): follow the shop's hours, or set their own, always within the shop's.
 * The shop manages hours only — never the professional's profile (spec §7).
 */
export function ProfessionalHoursCard({
  professionals,
  shopIntervals,
  canManage,
}: {
  professionals: ProfessionalHours[];
  shopIntervals: HoursInterval[];
  canManage: boolean;
}) {
  const t = useTranslations('shopSchedule');
  const locale = useLocale() as AppLocale;
  const [selectedId, setSelectedId] = useState(professionals[0]?.professionalId ?? '');
  const selected = professionals.find((p) => p.professionalId === selectedId);

  return (
    <section
      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
      data-testid="professional-hours"
    >
      <h2 className="text-h3 font-bold text-text-primary">{t('pros.title')}</h2>
      <p className="text-caption text-text-secondary">{t('pros.body')}</p>
      {professionals.length === 0 ? (
        <p className="text-caption text-text-tertiary">{t('pros.none')}</p>
      ) : (
        <>
          <SelectField
            label={t('pros.select')}
            value={selectedId}
            onChange={(e) => setSelectedId(e.target.value)}
          >
            {professionals.map((p) => (
              <option key={p.professionalId} value={p.professionalId}>
                {localizedName(locale, p.nameAr, p.nameEn)}
                {p.isActive ? '' : ` (${t('pros.disabled')})`}
              </option>
            ))}
          </SelectField>
          {selected && (
            <ProfessionalHoursForm
              key={selected.professionalId}
              professional={selected}
              shopIntervals={shopIntervals}
              canManage={canManage}
            />
          )}
        </>
      )}
    </section>
  );
}

function ProfessionalHoursForm({
  professional,
  shopIntervals,
  canManage,
}: {
  professional: ProfessionalHours;
  shopIntervals: HoursInterval[];
  canManage: boolean;
}) {
  const t = useTranslations('shopSchedule');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const [follows, setFollows] = useState(professional.followsShopHours);
  const [week, setWeek] = useState(() =>
    toWeekRows(professional.followsShopHours ? shopIntervals : professional.intervals),
  );
  const [current, setCurrent] = useState(professional.version);
  const [seen, setSeen] = useState(professional.version);
  if (seen !== professional.version) {
    setSeen(professional.version);
    if (professional.version !== current) {
      setCurrent(professional.version);
      setFollows(professional.followsShopHours);
      setWeek(toWeekRows(professional.followsShopHours ? shopIntervals : professional.intervals));
    }
  }
  const [pending, setPending] = useState(false);
  const [saved, setSaved] = useState(false);
  const { failure, fail, clear } = useScheduleErrors();

  const save = async () => {
    clear();
    setSaved(false);
    const result = fromWeekRows(week);
    if (!follows && 'invalidDay' in result) {
      fail(t('invalidTimes', { day: t(`days.${result.invalidDay}`) }));
      return;
    }
    setPending(true);
    try {
      const hours = ensureOk(
        await browserApi.PUT('/api/v1/shop/professionals/{professionalId}/working-hours', {
          params: { path: { professionalId: professional.professionalId } },
          body: {
            followsShopHours: follows,
            intervals: follows || 'invalidDay' in result ? [] : result.intervals,
            version: current,
          },
        }),
      );
      setCurrent(hours.version);
      setSaved(true);
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <Switch
        checked={follows}
        disabled={!canManage || pending}
        label={t('pros.follows')}
        onCheckedChange={setFollows}
      />
      {follows ? (
        <ul className="flex flex-col gap-1 text-caption text-text-secondary" data-testid="pro-follows-shop">
          {WEEK.map((day) => {
            const own = shopIntervals.filter((i) => i.day === day);
            return (
              <li key={day} className="flex gap-2">
                <span className="min-w-[6rem] font-semibold text-text-primary">{t(`days.${day}`)}</span>
                <span>
                  {own.length === 0 ? t('closed') : own.map((i) => intervalLabel(i, locale)).join('، ')}
                </span>
              </li>
            );
          })}
        </ul>
      ) : (
        <WeekHoursEditor
          idPrefix="pro-hours"
          value={week}
          onChange={setWeek}
          disabled={!canManage || pending}
        />
      )}
      {failure && <InlineAlert tone="danger" title={failure} />}
      {saved && <InlineAlert tone="success" title={t('pros.saved')} />}
      {canManage && (
        <Button className="self-start" variant="secondary" loading={pending} onClick={() => void save()}>
          {t('pros.save')}
        </Button>
      )}
    </div>
  );
}
