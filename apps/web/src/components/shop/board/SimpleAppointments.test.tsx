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
  item: {
    nameAr: 'حلاقة شعر',
    nameEn: 'Haircut',
    price: 60,
    currency: 'SAR',
    durationMinutes: 30,
    packageItems: [],
  },
  startsAt,
  endsAt: startsAt,
  status,
  loyaltyReward: null,
  allowedTransitions: status === 'Arrived' ? ['Completed'] : ['Arrived', 'NoShow', 'CancelledByShop'],
  visit: null,
  version: 3,
});

const BARBERS = [
  { id: 'p1', nameAr: 'سلطان الحربي', nameEn: 'Sultan' },
  { id: 'p2', nameAr: 'عمر', nameEn: 'Omar' },
];

// 2026-10-21 is today in Riyadh (UTC+3).
const week = [
  booking('1', 'سعود الرشيد', '2026-10-21T07:00:00Z', 'Arrived'),
  { ...booking('2', 'ناصر العتيبي', '2026-10-21T15:30:00Z'), visit: 'Returning' },
  booking('3', 'فهد الدوسري', '2026-10-22T09:00:00Z', 'Pending'),
  booking('4', 'ماجد القحطاني', '2026-10-24T13:00:00Z'),
];

function renderList(props: Partial<Parameters<typeof SimpleAppointments>[0]> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(
    <QueryClientProvider client={client}>
      <SimpleAppointments today="2026-10-21" timeZone="Asia/Riyadh" canUpdate {...props} />
    </QueryClientProvider>,
  );
}

/** The API's list for one day: the bookings starting on it (all these start before 21:00 UTC, so the same date). */
const dayList = (from: string) => {
  const items = week.filter((b) => b.startsAt.slice(0, 10) === from);
  return ok({ items, page: 1, pageSize: 100, total: items.length });
};

beforeEach(() => {
  api.GET.mockReset();
  api.POST.mockReset();
  window.history.replaceState(null, '', '/');
  api.GET.mockImplementation((path: string, init: { params: { query: { from: string } } }) =>
    Promise.resolve(path === '/api/v1/shop/bookings' ? dayList(init.params.query.from) : ok(null)),
  );
});

describe('SimpleAppointments (D-134, D-147, D-151)', () => {
  it("puts the shop's bookings to close first, with who is responsible, and who closed the late ones", async () => {
    const user = userEvent.setup();
    const red = {
      ...booking('9', 'بندر السبيعي', '2026-10-20T07:00:00Z'),
      professional: { id: 'p2', nameAr: 'عمر', nameEn: 'Omar' },
      overdue: true,
      // D-151: the usual buttons on a red booking, for any barber of the shop.
      allowedTransitions: ['Arrived', 'Completed', 'NoShow', 'CancelledByShop'],
    };
    const closed = {
      ...booking('8', 'مشعل', '2026-10-19T07:00:00Z', 'Completed'),
      closedLate: true,
      closedBy: 'سلطان',
      allowedTransitions: [],
    };
    api.GET.mockImplementation((path: string, init: { params: { query: { from: string } } }) =>
      Promise.resolve(
        path === '/api/v1/shop/bookings/attention'
          ? ok({ overdue: [red], closedLate: [closed] })
          : path === '/api/v1/shop/bookings'
            ? dayList(init.params.query.from)
            : ok(null),
      ),
    );
    api.POST.mockResolvedValue(ok({}));
    renderList();

    const overdue = await screen.findByTestId('attention-overdue');
    expect(within(overdue).getByRole('heading')).toHaveTextContent('حجوزات تحتاج إقفال (١)');
    expect(overdue).toHaveTextContent('بندر السبيعي');
    expect(overdue).toHaveTextContent('المسؤول: عمر');
    for (const action of ['وصل', 'انتهت الخدمة', 'ما حضر'])
      expect(within(overdue).getByRole('button', { name: `${action}: بندر السبيعي` })).toBeInTheDocument();
    expect(screen.getByTestId('attention-closed-late')).toHaveTextContent(
      'المسؤول: سلطان الحربي — قفله: سلطان',
    );
    // Bookings are confirmed at once (D-155): nothing waits for the shop to accept.
    expect(screen.queryByTestId('attention-pending')).not.toBeInTheDocument();

    // A red booking is closed from its row, by any barber of the shop.
    await user.click(within(overdue).getByRole('button', { name: 'وصل: بندر السبيعي' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith(
        '/api/v1/shop/bookings/{bookingId}/transitions',
        expect.objectContaining({ params: { path: { bookingId: '9' } } }),
      ),
    );
  });

  it('gives the owner nothing to press, and everyone’s day by default (D-147)', async () => {
    renderList({ canUpdate: false, barbers: BARBERS });
    await screen.findByTestId('coming-today');
    expect(
      screen.queryByRole('button', { name: /^(وصل|انتهت الخدمة|ما حضر|قبول):/ }),
    ).not.toBeInTheDocument();
    expect(screen.queryByTestId('quick-actions')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'جدولي' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'الكل' })).toHaveAttribute('aria-pressed', 'true');
    expect(api.GET).toHaveBeenCalledWith('/api/v1/shop/bookings', {
      params: { query: expect.objectContaining({ from: '2026-10-21', to: '2026-10-21', all: true }) },
    });
  });

  it("shows a barber their own day first, then everyone's or a colleague's (D-151, 1-1)", async () => {
    const user = userEvent.setup();
    const { container } = renderList({ ownProfessionalId: 'p1', barbers: BARBERS });

    const today = await screen.findByTestId('coming-today');
    await within(today).findByText('سعود الرشيد');
    expect(api.GET).toHaveBeenCalledWith('/api/v1/shop/bookings', {
      params: {
        query: {
          from: '2026-10-21',
          to: '2026-10-21',
          status: ['Pending', 'Confirmed', 'Arrived'],
          page: 1,
          pageSize: 100,
        },
      },
    });
    expect(within(today).getByRole('heading', { level: 2 })).toHaveTextContent('اليوم ·');
    expect(screen.getByRole('button', { name: 'جدولي' })).toHaveAttribute('aria-pressed', 'true');
    expect(
      within(today)
        .getAllByTestId('coming-appointment')
        .map((row) => row.textContent),
    ).toEqual([expect.stringContaining('سعود الرشيد'), expect.stringContaining('ناصر العتيبي')]);
    expect(within(today).getAllByTestId('coming-appointment')[0]).toHaveTextContent('حلاقة شعر · ');

    // The quick buttons live on «حجز حضوري» only (D-158).
    expect(screen.queryByTestId('quick-actions')).not.toBeInTheDocument();
    await expectNoAxeViolations(container);

    await user.click(screen.getByRole('button', { name: 'الكل' }));
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/bookings', {
        params: { query: expect.objectContaining({ all: true }) },
      }),
    );
    // A colleague: only the other barbers have a chip, the barber themself is «جدولي».
    expect(screen.queryByRole('button', { name: 'سلطان الحربي' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'عمر' }));
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/bookings', {
        params: { query: expect.objectContaining({ professionalId: 'p2' }) },
      }),
    );
    expect(screen.getByRole('button', { name: 'عمر' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByText(/ملغي/)).not.toBeInTheDocument();
  });

  it("moves a day at a time, never before today, and opens a booking's details", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByTestId('coming-today');
    expect(screen.getByRole('button', { name: 'اليوم السابق' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'اليوم التالي' }));
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/bookings', {
        params: expect.objectContaining({
          query: expect.objectContaining({ from: '2026-10-22', to: '2026-10-22' }),
        }),
      }),
    );
    expect(await screen.findByText('فهد الدوسري')).toBeInTheDocument();
    expect(screen.getByTestId('coming-day')).not.toHaveTextContent('اليوم ·');

    await user.click(screen.getByRole('button', { name: 'اليوم التالي' }));
    expect(await screen.findByText('لا حجوزات في هذا اليوم.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'اليوم السابق' }));
    await user.click(screen.getByRole('button', { name: 'اليوم السابق' }));
    await user.click(await screen.findByRole('button', { name: 'تفاصيل موعد سعود الرشيد' }));
    expect(window.location.search).toContain('booking=1');
  });

  it('finishes a visit under way after a confirmation, freeing the rest of its time (D-135, D-136)', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(ok({ ...week[0], status: 'Completed' }));
    renderList();
    expect(await screen.findByText('زبون عائد')).toBeInTheDocument();

    // Only the visit under way offers it; the coming ones do not. «تراجع» sends nothing.
    expect(screen.getAllByRole('button', { name: /^انتهت الخدمة:/ })).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'انتهت الخدمة: سعود الرشيد' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'انتهت خدمة سعود الرشيد؟' });
    await user.click(within(dialog).getByRole('button', { name: 'تراجع' }));
    expect(api.POST).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'انتهت الخدمة: سعود الرشيد' }));
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
