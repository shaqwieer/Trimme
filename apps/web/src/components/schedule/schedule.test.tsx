import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '@/lib/api/schema';
import { renderWithIntl } from '@/test/render';
import { OpeningHoursCard, ProfessionalHoursCard } from './HoursEditors';
import { PauseCard } from './PauseCard';
import { BreaksCard } from './ScheduleEntries';
import { fromWeekRows, toClock, toInterval, toMinutes, toWeekRows } from './time';

const api = vi.hoisted(() => ({ PUT: vi.fn(), POST: vi.fn(), DELETE: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

type Professional = components['schemas']['ProfessionalHoursResponse'];

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

const faisal: Professional = {
  professionalId: 'p-1',
  nameAr: 'فيصل',
  nameEn: 'Faisal',
  isActive: true,
  followsShopHours: true,
  intervals: [],
  version: null,
};

beforeEach(() => {
  api.PUT.mockReset();
  api.POST.mockReset();
  api.DELETE.mockReset();
});

describe('time helpers (D-082)', () => {
  it('reads and writes clock times on the 5-minute grid', () => {
    expect(toMinutes('09:05')).toBe(545);
    expect(toMinutes('09:07')).toBeNull();
    expect(toMinutes('24:00')).toBeNull();
    expect(toClock(545)).toBe('09:05');
    expect(toClock(1500)).toBe('01:00');
  });

  it('turns an end at or before the start into a window past midnight', () => {
    expect(toInterval('Thursday', '09:00', '00:00')).toEqual({
      day: 'Thursday',
      startMinute: 540,
      endMinute: 1440,
    });
    expect(toInterval('Thursday', '21:00', '02:00')).toEqual({
      day: 'Thursday',
      startMinute: 1260,
      endMinute: 1560,
    });
    expect(toInterval('Thursday', '09:00', '17:00')).toEqual({
      day: 'Thursday',
      startMinute: 540,
      endMinute: 1020,
    });
    expect(toInterval('Thursday', '09:00', 'x')).toBeNull();
  });

  it('round-trips a week, closed days included', () => {
    const intervals = [
      { day: 'Sunday' as const, startMinute: 540, endMinute: 780 },
      { day: 'Sunday' as const, startMinute: 840, endMinute: 1380 },
      { day: 'Thursday' as const, startMinute: 1260, endMinute: 1560 },
    ];
    const week = toWeekRows(intervals);
    expect(week.Saturday.open).toBe(false);
    expect(week.Sunday.rows).toEqual([
      { start: '09:00', end: '13:00' },
      { start: '14:00', end: '23:00' },
    ]);
    expect(fromWeekRows(week)).toEqual({ intervals });

    week.Monday = { open: true, rows: [{ start: '9', end: '10:00' }] };
    expect(fromWeekRows(week)).toEqual({ invalidDay: 'Monday' });
  });
});

describe('OpeningHoursCard', () => {
  it('saves the whole week with the version read, sending only open days', async () => {
    api.PUT.mockResolvedValue(ok({ intervals: [], version: 8 }));
    renderWithIntl(
      <OpeningHoursCard
        intervals={[{ day: 'Sunday', startMinute: 540, endMinute: 1380 }]}
        version={7}
        canManage
      />,
    );

    const saturday = screen.getByTestId('shop-hours-Saturday');
    expect(within(saturday).getByText('مغلق')).toBeInTheDocument();
    await userEvent.click(within(saturday).getByRole('switch', { name: 'مفتوح يوم السبت' }));
    const [, to] = within(saturday).getAllByLabelText(/من|إلى/);
    await userEvent.clear(to!);
    await userEvent.type(to!, '01:00');
    expect(within(saturday).getByText('(اليوم التالي)')).toBeInTheDocument();
    for (const input of within(saturday).getAllByLabelText(/من|إلى/))
      expect(input).toHaveAttribute('dir', 'ltr');

    await userEvent.click(screen.getByRole('button', { name: 'حفظ الدوام' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledOnce());
    expect(api.PUT.mock.calls[0]![1].body).toEqual({
      intervals: [
        { day: 'Sunday', startMinute: 540, endMinute: 1380 },
        { day: 'Saturday', startMinute: 540, endMinute: 1500 },
      ],
      version: 7,
    });
    expect(await screen.findByText('حُفظ دوام المحل')).toBeInTheDocument();
  });

  it('saves twice in a row with the version the first save returned', async () => {
    api.PUT.mockResolvedValueOnce(ok({ intervals: [], version: 8 })).mockResolvedValueOnce(
      ok({ intervals: [], version: 9 }),
    );
    renderWithIntl(<OpeningHoursCard intervals={[]} version={7} canManage />);
    await userEvent.click(screen.getByRole('button', { name: 'حفظ الدوام' }));
    expect(await screen.findByText('حُفظ دوام المحل')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'حفظ الدوام' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(2));
    expect(api.PUT.mock.calls.map((call) => call[1].body.version)).toEqual([7, 8]);
  });

  it("keeps a professional's saved message and later edits when the refresh of its own save arrives", async () => {
    api.PUT.mockResolvedValue(ok({ ...faisal, version: 8 }));
    function Refreshing() {
      const [pro, setPro] = useState<Professional>({ ...faisal, version: 7 });
      return (
        <>
          <button type="button" onClick={() => setPro({ ...pro, version: 8 })}>
            refresh
          </button>
          <ProfessionalHoursCard professionals={[pro]} shopIntervals={[]} canManage />
        </>
      );
    }
    renderWithIntl(<Refreshing />);

    await userEvent.click(screen.getByRole('button', { name: 'حفظ دوام الحلاق' }));
    expect(await screen.findByText('حُفظ دوام الحلاق')).toBeInTheDocument();
    // An edit made before the refresh arrives survives it, and the next save sends it with the new version.
    await userEvent.click(screen.getByRole('switch', { name: 'يتبع دوام المحل' }));
    await userEvent.click(screen.getByRole('button', { name: 'refresh' }));
    expect(screen.getByText('حُفظ دوام الحلاق')).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'يتبع دوام المحل' })).toHaveAttribute('aria-checked', 'false');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ دوام الحلاق' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(2));
    expect(api.PUT.mock.calls.map((call) => [call[1].body.version, call[1].body.followsShopHours])).toEqual([
      [7, true],
      [8, false],
    ]);
  });

  it('is read-only without the manage permission', () => {
    renderWithIntl(<OpeningHoursCard intervals={[]} version={null} canManage={false} />);
    expect(screen.queryByRole('button', { name: 'حفظ الدوام' })).not.toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'مفتوح يوم الأحد' })).toBeDisabled();
  });

  it('shows a validation problem from the API', async () => {
    api.PUT.mockResolvedValue({
      error: {
        status: 400,
        errorCode: 'validation.failed',
        errors: { intervals: ['validation.hours_overlap'] },
      },
      response: new Response(null, { status: 400 }),
    });
    renderWithIntl(<OpeningHoursCard intervals={[]} version={null} canManage />, { locale: 'en' });
    await userEvent.click(screen.getByRole('button', { name: 'Save hours' }));
    expect(
      await screen.findByText('The ranges overlap, including the hours after midnight'),
    ).toBeInTheDocument();
  });
});

describe('BreaksCard (DV-A04 conflict preview, DV-S22)', () => {
  it('lists the bookings a new break overlaps and saves only after confirmation', async () => {
    api.POST.mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith('/preview')
          ? ok({
              affectedBookings: [
                {
                  bookingId: 'b-1',
                  professionalId: 'p-1',
                  startsAt: '2026-10-04T07:00:00Z',
                  endsAt: '2026-10-04T07:30:00Z',
                  customerName: 'سارة',
                  itemNameAr: 'حلاقة',
                  itemNameEn: null,
                },
              ],
            })
          : ok({}),
      ),
    );
    renderWithIntl(<BreaksCard breaks={[]} professionals={[faisal]} canManage today="2026-10-04" />);
    await userEvent.click(screen.getByRole('button', { name: 'إضافة استراحة' }));
    const form = screen.getByTestId('break-form');
    await userEvent.type(within(form).getByLabelText('اسم الاستراحة'), 'صلاة العصر');
    await userEvent.click(within(form).getByRole('button', { name: 'حفظ' }));

    const affected = await screen.findByTestId('affected-bookings');
    expect(affected).toHaveTextContent('سارة');
    expect(screen.getByText(/لن تُرسل رسائل تلقائية/)).toBeInTheDocument();
    expect(api.POST).toHaveBeenCalledTimes(1);
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({
      label: 'صلاة العصر',
      professionalId: null,
      weekdays: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
      date: null,
      startMinute: 780,
      endMinute: 810,
    });

    await userEvent.click(within(form).getByRole('button', { name: 'حفظ رغم التعارض' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(2));
    expect(api.POST.mock.calls[1]![0]).toBe('/api/v1/shop/schedule/breaks');
  });

  it('saves straight away when nothing overlaps, and refuses an end before the start', async () => {
    api.POST.mockImplementation((path: string) =>
      Promise.resolve(path.endsWith('/preview') ? ok({ affectedBookings: [] }) : ok({})),
    );
    renderWithIntl(<BreaksCard breaks={[]} professionals={[faisal]} canManage today="2026-10-04" />, {
      locale: 'en',
    });
    await userEvent.click(screen.getByRole('button', { name: 'Add break' }));
    const form = screen.getByTestId('break-form');
    await userEvent.type(within(form).getByLabelText('Break name'), 'Lunch');
    const to = within(form).getByLabelText('To');
    await userEvent.clear(to);
    await userEvent.type(to, '12:00');
    await userEvent.click(within(form).getByRole('button', { name: 'Save' }));
    expect(
      await screen.findByText('Choose an end time after the start (5-minute steps)'),
    ).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();

    await userEvent.clear(to);
    await userEvent.type(to, '13:45');
    await userEvent.click(within(form).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByTestId('break-form')).not.toBeInTheDocument());
  });
});

describe('PauseCard (D-013)', () => {
  it('asks for confirmation, sends the optional reason, and shows the paused state', async () => {
    api.POST.mockResolvedValue(ok({ paused: true, pausedAt: '2026-10-04T07:00:00Z', reason: 'ازدحام' }));
    const live = renderWithIntl(<PauseCard paused={false} pausedAt={null} canPause />);
    expect(screen.getByText('إيقاف الحجز الإلكتروني مؤقتاً')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'إيقاف مؤقت' }));
    await userEvent.type(screen.getByLabelText(/السبب/), 'ازدحام');
    await userEvent.click(screen.getByRole('button', { name: 'إيقاف الآن' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/online-booking/pause', {
        body: { reason: 'ازدحام' },
      }),
    );
    expect(await screen.findByText('أُوقف الحجز الإلكتروني مؤقتاً')).toBeInTheDocument();

    live.unmount();
    renderWithIntl(<PauseCard paused pausedAt="2026-10-04T07:00:00Z" canPause />);
    expect(screen.getByTestId('pause-card')).toHaveAttribute('data-paused', 'true');
    expect(screen.getByText('الحجز الإلكتروني موقوف مؤقتاً')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إعادة تفعيل الحجز' })).toBeInTheDocument();
  });

  it('shows no actions without the pause permission', () => {
    renderWithIntl(<PauseCard paused={false} pausedAt={null} canPause={false} />);
    expect(screen.queryByRole('button', { name: 'إيقاف مؤقت' })).not.toBeInTheDocument();
  });
});
