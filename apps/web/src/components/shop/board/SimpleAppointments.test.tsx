import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { SimpleAppointments } from './SimpleAppointments';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api, refreshSession: vi.fn() }));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

const booking = (id: string, name: string, startsAt: string, status = 'Confirmed') => ({
  id,
  reference: `TR-${id}`,
  customerName: name,
  channel: 'Online',
  viaQr: false,
  professional: { id: 'p1', nameAr: 'سلطان الحربي', nameEn: 'Sultan' },
  item: { nameAr: 'حلاقة شعر', nameEn: 'Haircut', price: 60, currency: 'SAR', durationMinutes: 30 },
  startsAt,
  endsAt: startsAt,
  status,
  loyaltyReward: null,
  allowedTransitions: status === 'Arrived' ? ['Completed'] : ['Arrived', 'NoShow', 'CancelledByShop'],
  visit: null,
  version: 3,
});

// 2026-10-21 is today in Riyadh (UTC+3).
const week = [
  booking('1', 'سعود الرشيد', '2026-10-21T07:00:00Z', 'Arrived'),
  { ...booking('2', 'ناصر العتيبي', '2026-10-21T15:30:00Z'), visit: 'Returning' },
  booking('3', 'فهد الدوسري', '2026-10-22T09:00:00Z', 'Pending'),
  booking('4', 'ماجد القحطاني', '2026-10-24T13:00:00Z'),
];

function renderList() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(
    <QueryClientProvider client={client}>
      <SimpleAppointments today="2026-10-21" timeZone="Asia/Riyadh" canUpdate />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  api.GET.mockReset();
  api.POST.mockReset();
  window.history.replaceState(null, '', '/');
  api.GET.mockImplementation((path: string, init: { params: { query: { from: string } } }) =>
    Promise.resolve(
      path === '/api/v1/shop/bookings'
        ? ok({
            items: init.params.query.from === '2026-10-21' ? week : [],
            page: 1,
            pageSize: 100,
            total: init.params.query.from === '2026-10-21' ? week.length : 0,
          })
        : ok(null),
    ),
  );
});

describe('SimpleAppointments (D-134)', () => {
  it("lists today's bookings, then the coming days, with names only: no filters, no cancellations", async () => {
    const { container } = renderList();

    const today = await screen.findByTestId('coming-today');
    expect(api.GET).toHaveBeenCalledWith('/api/v1/shop/bookings', {
      params: {
        query: {
          from: '2026-10-21',
          to: '2026-10-27',
          status: ['Pending', 'Confirmed', 'Arrived'],
          page: 1,
          pageSize: 100,
        },
      },
    });
    expect(within(today).getByRole('heading')).toHaveTextContent('حجوزات اليوم');
    expect(
      within(today)
        .getAllByTestId('coming-appointment')
        .map((row) => row.textContent),
    ).toEqual([expect.stringContaining('سعود الرشيد'), expect.stringContaining('ناصر العتيبي')]);
    const later = screen.getByTestId('coming-later');
    expect(within(later).getByRole('heading', { level: 2 })).toHaveTextContent('المواعيد القادمة');
    expect(within(later).getByRole('heading', { name: 'غداً' })).toBeInTheDocument();
    expect(within(later).getByText('فهد الدوسري')).toBeInTheDocument();
    expect(within(later).getByText('ماجد القحطاني')).toBeInTheDocument();

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.queryByText(/ملغي/)).not.toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it("moves a week at a time, never before this week, and opens a booking's details", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByTestId('coming-today');
    expect(screen.getByRole('button', { name: 'الأسبوع السابق' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'الأسبوع التالي' }));
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/bookings', {
        params: expect.objectContaining({
          query: expect.objectContaining({ from: '2026-10-28', to: '2026-11-03' }),
        }),
      }),
    );
    expect(await screen.findByText('لا مواعيد قادمة في هذه الأيام.')).toBeInTheDocument();
    expect(screen.queryByTestId('coming-today')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'الأسبوع السابق' }));
    await user.click(await screen.findByRole('button', { name: 'تفاصيل موعد سعود الرشيد' }));
    expect(window.location.search).toContain('booking=1');
  });

  it('finishes a visit under way after a confirmation, freeing the rest of its time (D-135, D-136)', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(ok({ ...week[0], status: 'Completed' }));
    renderList();
    await screen.findByTestId('coming-today');
    expect(screen.getByText('زبون عائد')).toBeInTheDocument();

    // Only the visit under way offers it; the coming ones do not. «تراجع» sends nothing.
    expect(screen.getAllByRole('button', { name: /^انتهت خدمة/ })).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'انتهت خدمة سعود الرشيد' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'انتهت خدمة سعود الرشيد؟' });
    await user.click(within(dialog).getByRole('button', { name: 'تراجع' }));
    expect(api.POST).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'انتهت خدمة سعود الرشيد' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'نعم، انتهت الخدمة' }),
    );
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: '1' } },
        body: { to: 'Completed', reason: null, version: 3 },
      }),
    );
    expect(
      await screen.findByText('انتهت خدمة سعود الرشيد، وصار وقت الحلاق متاحاً للحجز.'),
    ).toBeInTheDocument();
  });
});
