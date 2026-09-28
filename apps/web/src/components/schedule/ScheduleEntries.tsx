'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useState } from 'react';
import { Badge } from '@/components/ui/Badge';
import { Button, IconButton } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { Checkbox, SelectField, TextareaField, TextField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { Chip, SegmentedControl } from '@/components/ui/selection.client';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatTime } from '@/lib/i18n/format';
import { formatLocalDate, type LocalDate } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { useScheduleErrors } from './errors';
import { formatMinutes, toClock, toMinutes, WEEK, type Weekday } from './time';

type Break = components['schemas']['BreakResponse'];
type TimeOff = components['schemas']['TimeOffResponse'];
type Closure = components['schemas']['ClosureResponse'];
type Professional = components['schemas']['ProfessionalHoursResponse'];
type Affected = components['schemas']['AffectedBookingResponse'];
type TimeOffKind = components['schemas']['TimeOffKind'];

const shortDate = (value: LocalDate, locale: AppLocale) =>
  formatLocalDate(value, locale, { weekday: 'long', day: 'numeric', month: 'long' });

function dateRange(start: LocalDate, end: LocalDate, locale: AppLocale) {
  return start === end ? shortDate(start, locale) : `${shortDate(start, locale)} — ${shortDate(end, locale)}`;
}

/**
 * Save with a conflict preview first (DV-A04): the API lists upcoming bookings the change would overlap. When there
 * are some, the shop sees them and must confirm; nothing is cancelled and no customer is messaged (DV-S22).
 */
function usePreviewedSave() {
  const router = useRouter();
  const errors = useScheduleErrors();
  const [pending, setPending] = useState(false);
  const [affected, setAffected] = useState<Affected[] | null>(null);
  return {
    ...errors,
    pending,
    affected,
    reset: () => {
      errors.clear();
      setAffected(null);
    },
    run: async (
      preview: () => Promise<{ affectedBookings: Affected[] }>,
      save: () => Promise<unknown>,
      onDone: () => void,
    ) => {
      errors.clear();
      setPending(true);
      try {
        if (affected === null) {
          const found = (await preview()).affectedBookings;
          if (found.length > 0) {
            setAffected(found);
            return;
          }
        }
        await save();
        setAffected(null);
        onDone();
        router.refresh();
      } catch (error) {
        errors.fail(error);
      } finally {
        setPending(false);
      }
    },
  };
}

function AffectedList({ bookings }: { bookings: Affected[] }) {
  const t = useTranslations('shopSchedule.conflicts');
  const locale = useLocale() as AppLocale;
  return (
    <InlineAlert tone="warning" title={t('title', { count: bookings.length })}>
      <p>{t('body')}</p>
      <ul className="mt-2 flex flex-col gap-1" data-testid="affected-bookings">
        {bookings.map((b) => (
          <li key={b.bookingId}>
            <bdi>{b.customerName}</bdi> · {localizedName(locale, b.itemNameAr, b.itemNameEn)} ·{' '}
            <bdi>{formatTime(b.startsAt, locale)}</bdi>
          </li>
        ))}
      </ul>
    </InlineAlert>
  );
}

function EntryRow({
  icon,
  title,
  when,
  badge,
  detail,
  actions,
  testId,
}: {
  icon: 'plane' | 'store' | 'coffee';
  title: ReactNode;
  when: ReactNode;
  badge?: ReactNode;
  detail?: ReactNode;
  actions?: ReactNode;
  testId: string;
}) {
  return (
    <li className="flex items-start gap-3 py-3" data-testid={testId}>
      <span
        aria-hidden="true"
        className="flex size-10 shrink-0 items-center justify-center rounded-button bg-brand-100 text-brand-700"
      >
        <Icon name={icon} className="size-5" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-bold text-text-primary">{title}</span>
          {badge}
        </div>
        <span className="text-caption text-text-secondary">{when}</span>
        {detail && <span className="text-helper text-text-tertiary">{detail}</span>}
      </div>
      {actions && <div className="flex shrink-0 gap-1">{actions}</div>}
    </li>
  );
}

/* ------------------------------------------------------------------ Breaks */

type BreakDraft = {
  id?: string;
  version?: number;
  label: string;
  professionalId: string;
  mode: 'weekly' | 'once';
  weekdays: Weekday[];
  date: LocalDate;
  start: string;
  end: string;
};

export function BreaksCard({
  breaks,
  professionals,
  canManage,
  today,
}: {
  breaks: Break[];
  professionals: Professional[];
  canManage: boolean;
  today: LocalDate;
}) {
  const t = useTranslations('shopSchedule');
  const locale = useLocale() as AppLocale;
  const [draft, setDraft] = useState<BreakDraft | null>(null);
  const [removing, setRemoving] = useState<Break | null>(null);
  const nameOf = (id: string | null) => {
    const pro = professionals.find((p) => p.professionalId === id);
    return pro ? localizedName(locale, pro.nameAr, pro.nameEn) : t('breaks.everyone');
  };
  const days = (b: Break) =>
    b.date
      ? shortDate(b.date, locale)
      : b.weekdays.length === 7
        ? t('breaks.everyDay')
        : WEEK.filter((d) => b.weekdays.includes(d))
            .map((d) => t(`days.${d}`))
            .join('، ');

  return (
    <section
      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
      data-testid="breaks"
    >
      <h2 className="text-h3 font-bold text-text-primary">{t('breaks.title')}</h2>
      <p className="text-caption text-text-secondary">{t('breaks.body')}</p>
      {breaks.length === 0 ? (
        <p className="text-caption text-text-tertiary">{t('breaks.empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border-row">
          {breaks.map((b) => (
            <EntryRow
              key={b.id}
              testId={`break-${b.id}`}
              icon="coffee"
              title={b.label}
              when={
                <>
                  {days(b)} ·{' '}
                  <span>
                    {formatMinutes(b.startMinute, locale)} – {formatMinutes(b.endMinute, locale)}
                  </span>
                </>
              }
              detail={nameOf(b.professionalId)}
              actions={
                canManage && (
                  <>
                    <IconButton
                      icon="edit"
                      label={t('edit', { name: b.label })}
                      onClick={() =>
                        setDraft({
                          id: b.id,
                          version: b.version,
                          label: b.label,
                          professionalId: b.professionalId ?? '',
                          mode: b.date ? 'once' : 'weekly',
                          weekdays: b.weekdays,
                          date: b.date ?? today,
                          start: toClock(b.startMinute),
                          end: toClock(b.endMinute),
                        })
                      }
                    />
                    <IconButton
                      icon="trash"
                      label={t('delete', { name: b.label })}
                      onClick={() => setRemoving(b)}
                    />
                  </>
                )
              }
            />
          ))}
        </ul>
      )}
      {canManage && (
        <Button
          variant="outline"
          icon="plus"
          className="self-start border-dashed"
          onClick={() =>
            setDraft({
              label: '',
              professionalId: '',
              mode: 'weekly',
              weekdays: [...WEEK],
              date: today,
              start: '13:00',
              end: '13:30',
            })
          }
        >
          {t('breaks.add')}
        </Button>
      )}
      {draft && (
        <BreakDialog
          draft={draft}
          professionals={professionals}
          today={today}
          onClose={() => setDraft(null)}
        />
      )}
      <DeleteDialog
        target={removing ? { name: removing.label } : null}
        onClose={() => setRemoving(null)}
        remove={async () =>
          ensureOk(
            await browserApi.DELETE('/api/v1/shop/schedule/breaks/{breakId}', {
              params: { path: { breakId: removing!.id } },
            }),
          )
        }
      />
    </section>
  );
}

function BreakDialog({
  draft: initial,
  professionals,
  today,
  onClose,
}: {
  draft: BreakDraft;
  professionals: Professional[];
  today: LocalDate;
  onClose: () => void;
}) {
  const t = useTranslations('shopSchedule');
  const locale = useLocale() as AppLocale;
  const [draft, setDraft] = useState(initial);
  const save = usePreviewedSave();
  const set = (patch: Partial<BreakDraft>) => {
    save.reset();
    setDraft({ ...draft, ...patch });
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const start = toMinutes(draft.start);
    const end = toMinutes(draft.end === '00:00' ? '' : draft.end) ?? (draft.end === '00:00' ? 1440 : null);
    if (!draft.label.trim()) return save.fail(t('breaks.labelRequired'));
    if (start === null || end === null || end <= start) return save.fail(t('breaks.timesInvalid'));
    if (draft.mode === 'weekly' && draft.weekdays.length === 0) return save.fail(t('breaks.daysRequired'));
    const body = {
      label: draft.label.trim(),
      professionalId: draft.professionalId || null,
      weekdays: draft.mode === 'weekly' ? draft.weekdays : [],
      date: draft.mode === 'once' ? draft.date : null,
      startMinute: start,
      endMinute: end,
      version: draft.version ?? null,
    };
    await save.run(
      async () => ensureOk(await browserApi.POST('/api/v1/shop/schedule/breaks/preview', { body })),
      async () =>
        draft.id
          ? ensureOk(
              await browserApi.PUT('/api/v1/shop/schedule/breaks/{breakId}', {
                params: { path: { breakId: draft.id } },
                body,
              }),
            )
          : ensureOk(await browserApi.POST('/api/v1/shop/schedule/breaks', { body })),
      onClose,
    );
  };

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={draft.id ? t('breaks.editTitle') : t('breaks.addTitle')}
      size="lg"
    >
      <form
        noValidate
        onSubmit={(e) => void submit(e)}
        className="flex flex-col gap-4"
        data-testid="break-form"
      >
        <TextField
          label={t('breaks.label')}
          value={draft.label}
          maxLength={60}
          onChange={(e) => set({ label: e.target.value })}
        />
        <SelectField
          label={t('breaks.appliesTo')}
          value={draft.professionalId}
          onChange={(e) => set({ professionalId: e.target.value })}
        >
          <option value="">{t('breaks.everyone')}</option>
          {professionals.map((p) => (
            <option key={p.professionalId} value={p.professionalId}>
              {localizedName(locale, p.nameAr, p.nameEn)}
            </option>
          ))}
        </SelectField>
        <SegmentedControl
          legend={t('breaks.repeat')}
          name="break-mode"
          value={draft.mode}
          onValueChange={(mode) => set({ mode: mode as BreakDraft['mode'] })}
          options={[
            { value: 'weekly', label: t('breaks.weekly') },
            { value: 'once', label: t('breaks.once') },
          ]}
        />
        {draft.mode === 'weekly' ? (
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-2 text-label font-bold text-text-primary">{t('breaks.days')}</legend>
            <div className="flex flex-wrap gap-2">
              {WEEK.map((day) => (
                <Chip
                  key={day}
                  pressed={draft.weekdays.includes(day)}
                  onPressedChange={(on) =>
                    set({ weekdays: on ? [...draft.weekdays, day] : draft.weekdays.filter((d) => d !== day) })
                  }
                >
                  {t(`days.${day}`)}
                </Chip>
              ))}
            </div>
          </fieldset>
        ) : (
          <TextField
            label={t('breaks.date')}
            type="date"
            dir="ltr"
            min={today}
            value={draft.date}
            onChange={(e) => set({ date: e.target.value })}
          />
        )}
        <div className="flex flex-wrap gap-3">
          <TextField
            label={t('from')}
            type="time"
            step={300}
            dir="ltr"
            className="w-[9rem]"
            value={draft.start}
            onChange={(e) => set({ start: e.target.value })}
          />
          <TextField
            label={t('to')}
            type="time"
            step={300}
            dir="ltr"
            className="w-[9rem]"
            value={draft.end}
            onChange={(e) => set({ end: e.target.value })}
          />
        </div>
        {save.affected && <AffectedList bookings={save.affected} />}
        {save.failure && <InlineAlert tone="danger" title={save.failure} />}
        <div className="flex flex-wrap gap-2.5">
          <Button type="submit" loading={save.pending}>
            {save.affected ? t('saveAnyway') : t('save')}
          </Button>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('cancel')}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

/* ------------------------------------------------------- Time off & closures */

type TimeOffDraft = {
  id?: string;
  version?: number;
  professionalId: string;
  kind: TimeOffKind;
  startDate: LocalDate;
  endDate: LocalDate;
  allDay: boolean;
  start: string;
  end: string;
  note: string;
};

type ClosureDraft = {
  id?: string;
  version?: number;
  startDate: LocalDate;
  endDate: LocalDate;
  reason: string;
};

/**
 * s-hours "الإجازات والتعطيل": professionals' time off (their hours only) and shop closures (the whole day), current
 * and upcoming, with the state badge from the design (in force now / scheduled).
 */
export function TimeOffCard({
  timeOff,
  closures,
  professionals,
  canManage,
  today,
}: {
  timeOff: TimeOff[];
  closures: Closure[];
  professionals: Professional[];
  canManage: boolean;
  today: LocalDate;
}) {
  const t = useTranslations('shopSchedule');
  const locale = useLocale() as AppLocale;
  const [leave, setLeave] = useState<TimeOffDraft | null>(null);
  const [closure, setClosure] = useState<ClosureDraft | null>(null);
  const [removing, setRemoving] = useState<{ name: string; remove: () => Promise<unknown> } | null>(null);
  const nameOf = (id: string) => {
    const pro = professionals.find((p) => p.professionalId === id);
    return pro ? localizedName(locale, pro.nameAr, pro.nameEn) : '';
  };
  const state = (s: 'Active' | 'Scheduled') => (
    <Badge tone={s === 'Active' ? 'warning' : 'info'}>{t(`timeOff.state.${s}`)}</Badge>
  );

  return (
    <section
      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
      data-testid="time-off"
    >
      <h2 className="text-h3 font-bold text-text-primary">{t('timeOff.title')}</h2>
      <p className="text-caption text-text-secondary">{t('timeOff.body')}</p>
      {timeOff.length + closures.length === 0 ? (
        <p className="text-caption text-text-tertiary">{t('timeOff.empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border-row">
          {timeOff.map((entry) => (
            <EntryRow
              key={entry.id}
              testId={`time-off-${entry.id}`}
              icon="plane"
              title={nameOf(entry.professionalId)}
              badge={state(entry.state)}
              when={
                <>
                  {t(`timeOff.kind.${entry.kind}`)} · {dateRange(entry.startDate, entry.endDate, locale)}
                  {!entry.allDay && entry.startMinute !== null && entry.endMinute !== null && (
                    <span>
                      {' '}
                      · {formatMinutes(entry.startMinute, locale)} – {formatMinutes(entry.endMinute, locale)}
                    </span>
                  )}
                </>
              }
              detail={entry.note ?? t('timeOff.effect')}
              actions={
                canManage && (
                  <>
                    <IconButton
                      icon="edit"
                      label={t('edit', { name: nameOf(entry.professionalId) })}
                      onClick={() =>
                        setLeave({
                          id: entry.id,
                          version: entry.version,
                          professionalId: entry.professionalId,
                          kind: entry.kind,
                          startDate: entry.startDate,
                          endDate: entry.endDate,
                          allDay: entry.allDay,
                          start: toClock(entry.startMinute ?? 540),
                          end: toClock(entry.endMinute ?? 1020),
                          note: entry.note ?? '',
                        })
                      }
                    />
                    <IconButton
                      icon="trash"
                      label={t('delete', { name: nameOf(entry.professionalId) })}
                      onClick={() =>
                        setRemoving({
                          name: nameOf(entry.professionalId),
                          remove: async () =>
                            ensureOk(
                              await browserApi.DELETE('/api/v1/shop/schedule/time-off/{timeOffId}', {
                                params: { path: { timeOffId: entry.id } },
                              }),
                            ),
                        })
                      }
                    />
                  </>
                )
              }
            />
          ))}
          {closures.map((entry) => (
            <EntryRow
              key={entry.id}
              testId={`closure-${entry.id}`}
              icon="store"
              title={t('timeOff.wholeShop')}
              badge={state(entry.state)}
              when={
                <>
                  {dateRange(entry.startDate, entry.endDate, locale)}
                  {entry.reason && (
                    <>
                      {' '}
                      · <bdi>{entry.reason}</bdi>
                    </>
                  )}
                </>
              }
              detail={t('timeOff.closureEffect')}
              actions={
                canManage && (
                  <>
                    <IconButton
                      icon="edit"
                      label={t('edit', { name: t('timeOff.wholeShop') })}
                      onClick={() =>
                        setClosure({
                          id: entry.id,
                          version: entry.version,
                          startDate: entry.startDate,
                          endDate: entry.endDate,
                          reason: entry.reason ?? '',
                        })
                      }
                    />
                    <IconButton
                      icon="trash"
                      label={t('delete', { name: t('timeOff.wholeShop') })}
                      onClick={() =>
                        setRemoving({
                          name: t('timeOff.wholeShop'),
                          remove: async () =>
                            ensureOk(
                              await browserApi.DELETE('/api/v1/shop/schedule/closures/{closureId}', {
                                params: { path: { closureId: entry.id } },
                              }),
                            ),
                        })
                      }
                    />
                  </>
                )
              }
            />
          ))}
        </ul>
      )}
      {canManage && (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            icon="plus"
            className="border-dashed"
            disabled={professionals.length === 0}
            onClick={() =>
              setLeave({
                professionalId: professionals[0]?.professionalId ?? '',
                kind: 'Vacation',
                startDate: today,
                endDate: today,
                allDay: true,
                start: '09:00',
                end: '17:00',
                note: '',
              })
            }
          >
            {t('timeOff.add')}
          </Button>
          <Button
            variant="outline"
            icon="plus"
            className="border-dashed"
            onClick={() => setClosure({ startDate: today, endDate: today, reason: '' })}
          >
            {t('timeOff.addClosure')}
          </Button>
        </div>
      )}
      {leave && (
        <TimeOffDialog
          draft={leave}
          professionals={professionals}
          today={today}
          onClose={() => setLeave(null)}
        />
      )}
      {closure && <ClosureDialog draft={closure} today={today} onClose={() => setClosure(null)} />}
      <DeleteDialog
        target={removing ? { name: removing.name } : null}
        onClose={() => setRemoving(null)}
        remove={() => removing!.remove()}
      />
    </section>
  );
}

function TimeOffDialog({
  draft: initial,
  professionals,
  today,
  onClose,
}: {
  draft: TimeOffDraft;
  professionals: Professional[];
  today: LocalDate;
  onClose: () => void;
}) {
  const t = useTranslations('shopSchedule');
  const locale = useLocale() as AppLocale;
  const [draft, setDraft] = useState(initial);
  const save = usePreviewedSave();
  const set = (patch: Partial<TimeOffDraft>) => {
    save.reset();
    setDraft({ ...draft, ...patch });
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!draft.professionalId) return save.fail(t('timeOff.professionalRequired'));
    const start = draft.allDay ? null : toMinutes(draft.start);
    const end = draft.allDay ? null : draft.end === '00:00' ? 1440 : toMinutes(draft.end);
    if (!draft.allDay && (start === null || end === null)) return save.fail(t('breaks.timesInvalid'));
    const body = {
      professionalId: draft.professionalId,
      kind: draft.kind,
      startDate: draft.startDate,
      endDate: draft.endDate,
      startMinute: start,
      endMinute: end,
      note: draft.note.trim() || null,
      version: draft.version ?? null,
    };
    await save.run(
      async () => ensureOk(await browserApi.POST('/api/v1/shop/schedule/time-off/preview', { body })),
      async () =>
        draft.id
          ? ensureOk(
              await browserApi.PUT('/api/v1/shop/schedule/time-off/{timeOffId}', {
                params: { path: { timeOffId: draft.id } },
                body,
              }),
            )
          : ensureOk(await browserApi.POST('/api/v1/shop/schedule/time-off', { body })),
      onClose,
    );
  };

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={draft.id ? t('timeOff.editTitle') : t('timeOff.addTitle')}
      size="lg"
    >
      <form
        noValidate
        onSubmit={(e) => void submit(e)}
        className="flex flex-col gap-4"
        data-testid="time-off-form"
      >
        <div className="grid gap-4 md:grid-cols-2">
          <SelectField
            label={t('timeOff.professional')}
            value={draft.professionalId}
            disabled={draft.id !== undefined}
            onChange={(e) => set({ professionalId: e.target.value })}
          >
            {professionals.map((p) => (
              <option key={p.professionalId} value={p.professionalId}>
                {localizedName(locale, p.nameAr, p.nameEn)}
              </option>
            ))}
          </SelectField>
          <SelectField
            label={t('timeOff.kindLabel')}
            value={draft.kind}
            onChange={(e) => set({ kind: e.target.value as TimeOffKind })}
          >
            {(['Vacation', 'Sick', 'Other'] as const).map((kind) => (
              <option key={kind} value={kind}>
                {t(`timeOff.kind.${kind}`)}
              </option>
            ))}
          </SelectField>
          <TextField
            label={t('timeOff.startDate')}
            type="date"
            dir="ltr"
            min={today}
            value={draft.startDate}
            onChange={(e) => set({ startDate: e.target.value })}
          />
          <TextField
            label={t('timeOff.endDate')}
            type="date"
            dir="ltr"
            min={draft.startDate}
            value={draft.endDate}
            onChange={(e) => set({ endDate: e.target.value })}
          />
        </div>
        <Checkbox
          label={t('timeOff.allDay')}
          checked={draft.allDay}
          onChange={(e) => set({ allDay: e.target.checked })}
        />
        {!draft.allDay && (
          <div className="flex flex-wrap gap-3">
            <TextField
              label={t('timeOff.startTime')}
              type="time"
              step={300}
              dir="ltr"
              className="w-[9rem]"
              value={draft.start}
              onChange={(e) => set({ start: e.target.value })}
            />
            <TextField
              label={t('timeOff.endTime')}
              type="time"
              step={300}
              dir="ltr"
              className="w-[9rem]"
              value={draft.end}
              onChange={(e) => set({ end: e.target.value })}
            />
          </div>
        )}
        <TextareaField
          label={t('timeOff.note')}
          optional
          maxLength={200}
          rows={2}
          value={draft.note}
          onChange={(e) => set({ note: e.target.value })}
        />
        {save.affected && <AffectedList bookings={save.affected} />}
        {save.failure && <InlineAlert tone="danger" title={save.failure} />}
        <div className="flex flex-wrap gap-2.5">
          <Button type="submit" loading={save.pending}>
            {save.affected ? t('saveAnyway') : t('save')}
          </Button>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('cancel')}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

function ClosureDialog({
  draft: initial,
  today,
  onClose,
}: {
  draft: ClosureDraft;
  today: LocalDate;
  onClose: () => void;
}) {
  const t = useTranslations('shopSchedule');
  const [draft, setDraft] = useState(initial);
  const save = usePreviewedSave();
  const set = (patch: Partial<ClosureDraft>) => {
    save.reset();
    setDraft({ ...draft, ...patch });
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const body = {
      startDate: draft.startDate,
      endDate: draft.endDate,
      reason: draft.reason.trim() || null,
      version: draft.version ?? null,
    };
    await save.run(
      async () => ensureOk(await browserApi.POST('/api/v1/shop/schedule/closures/preview', { body })),
      async () =>
        draft.id
          ? ensureOk(
              await browserApi.PUT('/api/v1/shop/schedule/closures/{closureId}', {
                params: { path: { closureId: draft.id } },
                body,
              }),
            )
          : ensureOk(await browserApi.POST('/api/v1/shop/schedule/closures', { body })),
      onClose,
    );
  };

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={draft.id ? t('closures.editTitle') : t('closures.addTitle')}
      description={t('closures.body')}
    >
      <form
        noValidate
        onSubmit={(e) => void submit(e)}
        className="flex flex-col gap-4"
        data-testid="closure-form"
      >
        <div className="grid gap-4 md:grid-cols-2">
          <TextField
            label={t('closures.startDate')}
            type="date"
            dir="ltr"
            min={today}
            value={draft.startDate}
            onChange={(e) => set({ startDate: e.target.value })}
          />
          <TextField
            label={t('closures.endDate')}
            type="date"
            dir="ltr"
            min={draft.startDate}
            value={draft.endDate}
            onChange={(e) => set({ endDate: e.target.value })}
          />
        </div>
        <TextField
          label={t('closures.reason')}
          optional
          maxLength={200}
          value={draft.reason}
          onChange={(e) => set({ reason: e.target.value })}
        />
        {save.affected && <AffectedList bookings={save.affected} />}
        {save.failure && <InlineAlert tone="danger" title={save.failure} />}
        <div className="flex flex-wrap gap-2.5">
          <Button type="submit" loading={save.pending}>
            {save.affected ? t('saveAnyway') : t('save')}
          </Button>
          <Button type="button" variant="ghost" onClick={onClose}>
            {t('cancel')}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

function DeleteDialog({
  target,
  onClose,
  remove,
}: {
  target: { name: string } | null;
  onClose: () => void;
  remove: () => Promise<unknown>;
}) {
  const t = useTranslations('shopSchedule');
  const router = useRouter();
  const { failure, fail, clear } = useScheduleErrors();
  const [pending, setPending] = useState(false);
  return (
    <ConfirmDialog
      open={target !== null}
      onOpenChange={(open) => {
        if (!open) {
          clear();
          onClose();
        }
      }}
      title={target ? t('deleteTitle', { name: target.name }) : ''}
      body={failure ?? t('deleteBody')}
      confirmLabel={t('deleteConfirm')}
      loading={pending}
      onConfirm={() => {
        setPending(true);
        void remove()
          .then(() => {
            onClose();
            router.refresh();
          })
          .catch(fail)
          .finally(() => setPending(false));
      }}
    />
  );
}
