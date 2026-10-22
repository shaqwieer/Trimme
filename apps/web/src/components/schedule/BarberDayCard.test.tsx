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
const minute = (m: number) => screen.getByTestId('barber-day-minutes').querySelector(`[data-minute="${m}"]`)!;
const dialog = () => screen.getByRole('alertdialog');
const saveWith = () =>
  api.PUT.mockImplementation((_path: string, init: { body: { date: string; breaks: unknown[] } }) =>
    Promise.resolve(ok({ ...day(init.body.date), breaks: init.body.breaks })),
  );

beforeEach(() => {
  api.GET.mockReset();
  api.PUT.mockReset();
  api.GET.mockImplementation((_path: string, init: { params: { query: { date: string } } }) =>
    Promise.resolve(ok(day(init.params.query.date))),
  );
});

describe('BarberDayCard (D-134, D-137)', () => {
  it("opens an hour's 5-minute slots, makes the picked ones a break after a confirmation and says so", async () => {
    const user = userEvent.setup();
    saveWith();
    const { container } = renderCard(<BarberDayCard barbers={barbers} today="2026-10-21" />);

    await waitFor(() => expect(screen.getByTestId('barber-day-hours').children).toHaveLength(24));
    expect(api.GET).toHaveBeenCalledWith('/api/v1/shop/schedule/barber-day', {
      params: { query: { professionalId: 'p1', date: '2026-10-21' } },
    });
    expect(hour(8)).toHaveAttribute('data-state', 'outside');
    expect(hour(8)).toBeDisabled();
    // Booked 11:00–11:30: the free half can still be a break.
    expect(hour(11)).toHaveAttribute('data-state', 'partFree');
    expect(hour(11)).toBeEnabled();
    expect(hour(12)).toHaveAttribute('data-state', 'break');
    expect(within(screen.getByTestId('barber-day')).queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'اليوم السابق' })).toBeDisabled();
    await expectNoAxeViolations(container);

    await user.click(hour(9));
    expect(hour(9)).toHaveAttribute('aria-expanded', 'true');
    const panel = screen.getByTestId('barber-day-minutes');
    expect(panel.querySelectorAll('[data-minute]')).toHaveLength(12);
    expect(screen.getByRole('button', { name: 'تأكيد الاستراحة' })).toBeDisabled();
    await user.click(minute(555));
    await user.click(minute(560));
    expect(minute(555)).toHaveAttribute('aria-pressed', 'true');
    expect(minute(560)).toHaveTextContent('مختار');
    await expectNoAxeViolations(container);

    await user.click(screen.getByRole('button', { name: 'تأكيد الاستراحة' }));
    expect(dialog()).toHaveTextContent('سلطان الحربي');
    expect(api.PUT).not.toHaveBeenCalled();
    await user.click(within(dialog()).getByRole('button', { name: 'تأكيد الاستراحة' }));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/schedule/barber-day', {
        body: { professionalId: 'p1', date: '2026-10-21', breaks: [w(555, 565), w(720, 780)] },
      }),
    );
    const done = await screen.findByText(/^تم حجز .* استراحة لـ سلطان الحربي\.$/);
    expect(done.closest('[role="status"]')).not.toBeNull();
    await waitFor(() => expect(hour(9)).toHaveAttribute('data-state', 'partBreak'));
    expect(hour(9)).toHaveTextContent('استراحة');
    expect(minute(555)).toHaveAttribute('data-state', 'break');
  });

  it('cancels a break from any of its minutes after a confirmation; going back sends nothing', async () => {
    const user = userEvent.setup();
    saveWith();
    renderCard(<BarberDayCard barbers={barbers} today="2026-10-21" />);
    await waitFor(() => expect(hour(12)).toBeEnabled());

    await user.click(hour(12));
    await user.click(minute(730));
    expect(dialog()).toHaveTextContent('إلغاء استراحة');
    await user.click(within(dialog()).getByRole('button', { name: 'تراجع' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(api.PUT).not.toHaveBeenCalled();

    await user.click(minute(730));
    await user.click(within(dialog()).getByRole('button', { name: 'إلغاء الاستراحة' }));
    await waitFor(() => expect(api.PUT.mock.calls[0]![1].body.breaks).toEqual([]));
    expect(await screen.findByText(/^أُلغيت استراحة .*، والوقت متاح للحجز\.$/)).toBeInTheDocument();
  });

  it('picks the whole hour at once, around the booked half', async () => {
    const user = userEvent.setup();
    saveWith();
    renderCard(<BarberDayCard barbers={barbers} today="2026-10-21" />);
    await waitFor(() => expect(hour(11)).toBeEnabled());

    await user.click(hour(11));
    expect(minute(660)).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'الساعة كاملة' }));
    expect(screen.getByTestId('barber-day-minutes').querySelectorAll('[aria-pressed="true"]')).toHaveLength(
      6,
    );
    expect(screen.getByRole('button', { name: 'إلغاء التحديد' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'تأكيد الاستراحة' }));
    await user.click(within(dialog()).getByRole('button', { name: 'تأكيد الاستراحة' }));
    await waitFor(() => expect(api.PUT.mock.calls[0]![1].body.breaks).toEqual([w(690, 780)]));
  });

  it('moves day by day and takes the whole day off, and clears it, after a confirmation', async () => {
    const user = userEvent.setup();
    saveWith();
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
    expect(dialog()).toHaveTextContent('سلطان الحربي');
    await user.click(within(dialog()).getByRole('button', { name: 'تأكيد الاستراحة' }));
    await waitFor(() =>
      expect(api.PUT.mock.calls[0]![1].body).toEqual({
        professionalId: 'p1',
        date: '2026-10-22',
        breaks: [w(540, 660), w(690, 780)],
      }),
    );
    expect(await screen.findByText('أصبح اليوم كله استراحة لـ سلطان الحربي.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'إلغاء كل الاستراحات' }));
    await user.click(within(dialog()).getByRole('button', { name: 'إلغاء الاستراحات' }));
    await waitFor(() => expect(api.PUT.mock.calls[1]![1].body.breaks).toEqual([]));

    await user.selectOptions(screen.getByLabelText('الحلاق'), 'p2');
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/schedule/barber-day', {
        params: { query: { professionalId: 'p2', date: '2026-10-22' } },
      }),
    );
  });

  it('explains the cap on separate breaks', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({
      error: { errorCode: 'validation.failed', errors: { breaks: ['validation.too_many'] } },
      response: new Response(null, { status: 400 }),
    });
    renderCard(<BarberDayCard barbers={barbers} today="2026-10-21" />);
    await waitFor(() => expect(hour(9)).toBeEnabled());

    await user.click(hour(9));
    await user.click(minute(540));
    await user.click(screen.getByRole('button', { name: 'تأكيد الاستراحة' }));
    await user.click(within(dialog()).getByRole('button', { name: 'تأكيد الاستراحة' }));
    expect(await screen.findByText(/أكثر من ٤٨ استراحة/)).toBeInTheDocument();
  });

  it('needs no picker for a shop with one barber', async () => {
    renderCard(<BarberDayCard barbers={[barbers[0]!]} today="2026-10-21" />);
    expect(await screen.findByText('سلطان الحربي')).toBeInTheDocument();
    expect(screen.queryByLabelText('الحلاق')).not.toBeInTheDocument();
  });
});
