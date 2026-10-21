import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { BarberDayCard } from './BarberDayCard';

const api = vi.hoisted(() => ({ GET: vi.fn(), PUT: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api, refreshSession: vi.fn() }));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });
const w = (startMinute: number, endMinute: number) => ({ startMinute, endMinute });

/** Works 09:00–13:00; booked 11:00–11:30; already a break 12:00–13:00. */
const day = (date: string, breaks = [w(720, 780)]) => ({
  professionalId: 'p1',
  date,
  today: '2026-10-21',
  passedMinutes: 0,
  working: [w(540, 780)],
  breaks,
  shopBreaks: [],
  booked: [w(660, 690)],
  timeOff: [],
});

const barbers = [
  { id: 'p1', nameAr: 'سلطان الحربي', nameEn: 'Sultan' },
  { id: 'p2', nameAr: 'راكان المطيري', nameEn: 'Rakan' },
];

function renderCard(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const hour = (h: number) => screen.getByTestId('barber-day-hours').querySelector(`[data-hour="${h}"]`)!;

beforeEach(() => {
  api.GET.mockReset();
  api.PUT.mockReset();
  api.GET.mockImplementation((_path: string, init: { params: { query: { date: string } } }) =>
    Promise.resolve(ok(day(init.params.query.date))),
  );
});

describe('BarberDayCard (D-134)', () => {
  it("shows the barber's 24 hours and makes a free hour a break with one tap, no reason asked", async () => {
    const user = userEvent.setup();
    api.PUT.mockImplementation((_path: string, init: { body: { breaks: unknown[] } }) =>
      Promise.resolve(ok({ ...day('2026-10-21'), breaks: init.body.breaks })),
    );
    const { container } = renderCard(<BarberDayCard barbers={barbers} today="2026-10-21" />);

    await waitFor(() => expect(screen.getByTestId('barber-day-hours').children).toHaveLength(24));
    expect(api.GET).toHaveBeenCalledWith('/api/v1/shop/schedule/barber-day', {
      params: { query: { professionalId: 'p1', date: '2026-10-21' } },
    });
    expect(hour(8)).toHaveAttribute('data-state', 'outside');
    expect(hour(8)).toBeDisabled();
    expect(hour(11)).toHaveAttribute('data-state', 'booked');
    expect(hour(11)).toBeDisabled();
    expect(hour(12)).toHaveAttribute('aria-pressed', 'true');
    expect(within(screen.getByTestId('barber-day')).queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'اليوم السابق' })).toBeDisabled();
    await expectNoAxeViolations(container);

    await user.click(hour(9));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/schedule/barber-day', {
        body: { professionalId: 'p1', date: '2026-10-21', breaks: [w(540, 600), w(720, 780)] },
      }),
    );
    await waitFor(() => expect(hour(9)).toHaveAttribute('aria-pressed', 'true'));
    expect(screen.getByRole('status')).toHaveTextContent('تم الحفظ');

    await user.click(hour(12));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(2));
    expect(api.PUT.mock.calls[1]![1].body.breaks).toEqual([w(540, 600)]);
  });

  it('moves day by day and takes the whole day off in one go', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(ok(day('2026-10-22', [w(540, 660), w(720, 780)])));
    renderCard(<BarberDayCard barbers={barbers} today="2026-10-21" />);
    await screen.findByTestId('barber-day-hours');

    await user.click(screen.getByRole('button', { name: 'اليوم التالي' }));
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/schedule/barber-day', {
        params: { query: { professionalId: 'p1', date: '2026-10-22' } },
      }),
    );
    expect(screen.getByRole('button', { name: 'اليوم السابق' })).toBeEnabled();

    await user.click(await screen.findByRole('button', { name: 'استراحة طوال اليوم' }));
    await waitFor(() =>
      expect(api.PUT.mock.calls[0]![1].body).toEqual({
        professionalId: 'p1',
        date: '2026-10-22',
        breaks: [w(540, 660), w(720, 780)],
      }),
    );

    await user.selectOptions(screen.getByLabelText('الحلاق'), 'p2');
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/schedule/barber-day', {
        params: { query: { professionalId: 'p2', date: '2026-10-22' } },
      }),
    );
  });

  it('needs no picker for a shop with one barber', async () => {
    renderCard(<BarberDayCard barbers={[barbers[0]!]} today="2026-10-21" />);
    expect(await screen.findByText('سلطان الحربي')).toBeInTheDocument();
    expect(screen.queryByLabelText('الحلاق')).not.toBeInTheDocument();
  });
});
