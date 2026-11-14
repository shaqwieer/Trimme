import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { formatLocalDate, todayLocal } from '@/lib/i18n/localDate';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { AppointmentDrawer } from './AppointmentDrawer';
import { MoveDialog, RescheduleDialog } from './BookingChangeDialogs';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api, refreshSession: vi.fn() }));

const ok = <T,>(data: T, status = 200) => ({ data, response: new Response(null, { status }) });
const fail = (status: number, errorCode: string) => ({
  error: { status, errorCode, title: errorCode },
  response: new Response(null, { status }),
});

function renderWithQuery(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const addDays = (day: string, days: number) => {
  const d = new Date(`${day}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

const LATER = '2030-01-10T14:30:00Z';
const TARGET = { id: 'b1', version: 4, startsAt: LATER, customerName: 'عبدالله الشمري' };

const detail = (status: string, startsAt: string) => ({
  booking: {
    id: 'b1',
    reference: 'TR-48219',
    customerName: 'عبدالله الشمري',
    channel: 'Online',
    professional: { id: 'p1', nameAr: 'فيصل القحطاني', nameEn: 'Faisal' },
    item: {
      serviceId: 's1',
      packageId: null,
      nameAr: 'قص شعر',
      nameEn: null,
      price: 60,
      currency: 'SAR',
      durationMinutes: 30,
      packageItems: [],
    },
    startsAt,
    endsAt: startsAt,
    status,
    note: null,
    cancellationReason: null,
    allowedTransitions: [],
    outsideSchedule: false,
    visit: 'New',
    version: 4,
  },
  history: [
    {
      kind: 'ProfessionalChanged',
      fromStatus: 'Confirmed',
      toStatus: 'Confirmed',
      previousStartsAt: null,
      actorType: 'ShopUser',
      reason: null,
      occurredAt: '2026-10-06T18:41:00Z',
      previousProfessional: { id: 'p9', nameAr: 'عمر', nameEn: 'Omar' },
    },
  ],
  notes: [],
});

beforeEach(() => {
  api.GET.mockReset();
  api.POST.mockReset();
});

describe('AppointmentDrawer changes (D-145)', () => {
  it('offers a new time and another barber for a booking that has not started, with the permissions only', async () => {
    api.GET.mockResolvedValue(ok(detail('Confirmed', LATER)));
    const { unmount } = renderWithQuery(
      <AppointmentDrawer
        bookingId="b1"
        timeZone="Asia/Riyadh"
        canUpdate
        canReschedule
        canMove
        onClose={() => {}}
      />,
    );
    expect(await screen.findByRole('button', { name: 'تغيير الموعد' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'نقل الحجز إلى حلاق آخر' })).toBeInTheDocument();
    expect(screen.getByText('نُقل الحجز من عمر')).toBeInTheDocument();
    unmount();

    renderWithQuery(<AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />);
    await screen.findByText('TR-48219');
    expect(screen.queryByRole('button', { name: 'تغيير الموعد' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'نقل الحجز إلى حلاق آخر' })).toBeNull();
  });

  it('offers no change for a booking that has ended', async () => {
    api.GET.mockResolvedValue(ok(detail('Completed', '2026-01-10T14:30:00Z')));
    renderWithQuery(
      <AppointmentDrawer
        bookingId="b1"
        timeZone="Asia/Riyadh"
        canUpdate
        canReschedule
        canMove
        onClose={() => {}}
      />,
    );
    await screen.findByText('TR-48219');
    expect(screen.queryByRole('button', { name: 'تغيير الموعد' })).toBeNull();
  });
});

describe('MoveDialog («نقل الحجز إلى حلاق آخر», D-145)', () => {
  const options = ok({
    startsAt: LATER,
    professionals: [
      { id: 'p2', nameAr: 'عمر', nameEn: 'Omar', free: true },
      { id: 'p3', nameAr: 'سالم', nameEn: 'Salem', free: false },
    ],
  });

  it('lists the colleagues, busy ones disabled, and gives the booking to a free one with a key and the version', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(options);
    api.POST.mockResolvedValue(ok({}));
    const onClose = vi.fn();
    const onChanged = vi.fn();
    renderWithQuery(
      <MoveDialog booking={TARGET} timeZone="Asia/Riyadh" onClose={onClose} onChanged={onChanged} />,
    );

    expect(await screen.findByRole('radio', { name: /سالم/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'نقل الحجز' })).toBeDisabled();
    await expectNoAxeViolations(document.body);
    await user.click(screen.getByRole('radio', { name: /عمر/ }));
    await user.click(screen.getByRole('button', { name: 'نقل الحجز' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(onChanged).toHaveBeenCalled();
    expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/professional', {
      params: { path: { bookingId: 'b1' }, header: { 'Idempotency-Key': expect.any(String) } },
      body: { professionalId: 'p2', version: 4 },
    });
  });

  it('says when the colleague just became busy, refreshes the list and stays open', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(options);
    api.POST.mockResolvedValue(fail(409, 'booking.professional_busy'));
    const onClose = vi.fn();
    renderWithQuery(
      <MoveDialog booking={TARGET} timeZone="Asia/Riyadh" onClose={onClose} onChanged={vi.fn()} />,
    );

    await user.click(await screen.findByRole('radio', { name: /عمر/ }));
    await user.click(screen.getByRole('button', { name: 'نقل الحجز' }));

    expect(await screen.findByText('أصبح هذا الحلاق مشغولاً في هذا الوقت. اختر غيره.')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(api.GET).toHaveBeenCalledTimes(2);
  });
});

describe('RescheduleDialog (D-145)', () => {
  it('offers the free days and times of the same barber and saves the chosen one', async () => {
    const user = userEvent.setup();
    const today = todayLocal('Asia/Riyadh');
    const day = addDays(today, 2);
    api.GET.mockImplementation(async (path: string) =>
      path.endsWith('/reschedule/dates')
        ? ok({
            bookable: true,
            blockedReason: null,
            timeZone: 'Asia/Riyadh',
            dates: [
              { date: addDays(today, 1), slotCount: 0 },
              { date: day, slotCount: 2 },
            ],
          })
        : ok({
            bookable: true,
            blockedReason: null,
            timeZone: 'Asia/Riyadh',
            date: day,
            slots: [
              { startsAt: `${day}T07:00:00Z`, localTime: '10:00', period: 'Morning' },
              { startsAt: `${day}T07:30:00Z`, localTime: '10:30', period: 'Morning' },
            ],
          }),
    );
    api.POST.mockResolvedValue(ok({}));
    const onClose = vi.fn();
    renderWithQuery(
      <RescheduleDialog booking={TARGET} timeZone="Asia/Riyadh" onClose={onClose} onChanged={vi.fn()} />,
    );

    const save = await screen.findByRole('button', { name: 'حفظ الموعد الجديد' });
    expect(save).toBeDisabled();
    await user.click(
      await screen.findByRole('radio', {
        name: new RegExp(formatLocalDate(day, 'ar', { day: 'numeric', month: 'long' })),
      }),
    );
    await user.click(await screen.findByRole('radio', { name: '١٠ ص' }));
    await user.click(screen.getByRole('radio', { name: /١٠:٣٠/ }));
    await user.click(save);

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/reschedule', {
      params: { path: { bookingId: 'b1' }, header: { 'Idempotency-Key': expect.any(String) } },
      body: { startsAt: `${day}T07:30:00Z`, version: 4 },
    });
  });
});
