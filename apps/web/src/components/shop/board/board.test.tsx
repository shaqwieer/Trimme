import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { AppointmentDrawer } from './AppointmentDrawer';
import { WalkInFlow, type WalkInOffer } from './WalkInFlow';

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

const detail = (
  status = 'Confirmed',
  allowedTransitions: string[] = ['Arrived', 'NoShow', 'CancelledByShop'],
) => ({
  booking: {
    id: 'b1',
    reference: 'TR-48219',
    customerName: 'عبدالله الشمري',
    channel: 'Online',
    professional: { id: 'p1', nameAr: 'فيصل القحطاني', nameEn: 'Faisal' },
    item: {
      serviceId: 's1',
      packageId: null,
      nameAr: 'باقة شعر ولحية',
      nameEn: null,
      price: 85,
      currency: 'SAR',
      durationMinutes: 50,
      packageItems: [],
    },
    startsAt: '2026-10-08T14:30:00Z',
    endsAt: '2026-10-08T15:20:00Z',
    status,
    note: null,
    cancellationReason: null,
    allowedTransitions,
    outsideSchedule: false,
    version: 4,
  },
  history: [
    {
      kind: 'Created',
      fromStatus: null,
      toStatus: 'Confirmed',
      previousStartsAt: null,
      actorType: 'Customer',
      reason: null,
      occurredAt: '2026-10-06T18:41:00Z',
    },
  ],
  notes: [],
});

beforeEach(() => {
  api.GET.mockReset();
  api.POST.mockReset();
});

describe('AppointmentDrawer (s-appointments drawer, DV-S08)', () => {
  it('shows the booking without any phone, offers only the allowed transitions, and applies one optimistically', async () => {
    const user = userEvent.setup();
    let status = 'Confirmed';
    api.GET.mockImplementation(async () =>
      ok(detail(status, status === 'Confirmed' ? ['Arrived', 'NoShow', 'CancelledByShop'] : ['Completed'])),
    );
    api.POST.mockImplementation(async () => {
      status = 'Arrived';
      return ok({});
    });
    renderWithQuery(<AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />);

    const drawer = await screen.findByTestId('appointment-drawer');
    await within(drawer).findByText('عبدالله الشمري');
    expect(drawer).toHaveTextContent('85 ر.س — يُدفع في المحل');
    expect(within(drawer).getByTestId('no-phone-note')).toHaveTextContent('رقم العميل غير متاح');
    expect(drawer.textContent).not.toMatch(/\+966|05\d{8}/);
    expect(
      within(drawer)
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(expect.arrayContaining(['حضر العميل', 'لم يحضر', 'إلغاء الموعد']));
    expect(within(drawer).queryByRole('button', { name: 'مكتمل' })).not.toBeInTheDocument();
    await expectNoAxeViolations(drawer);

    await user.click(within(drawer).getByRole('button', { name: 'حضر العميل' }));
    await waitFor(() =>
      expect(within(drawer).queryByRole('button', { name: 'حضر العميل' })).not.toBeInTheDocument(),
    );
    expect(within(drawer).getAllByText('حضر العميل').length).toBeGreaterThan(0);
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: 'b1' } },
        body: { to: 'Arrived', reason: null, version: 4 },
      }),
    );
    expect(await within(drawer).findByRole('button', { name: 'مكتمل' })).toBeInTheDocument();
  });

  it('rolls back and says so when the booking changed meanwhile (409)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(detail()));
    api.POST.mockResolvedValue(fail(409, 'resource.concurrency_conflict'));
    renderWithQuery(<AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />);

    const drawer = await screen.findByTestId('appointment-drawer');
    await user.click(await within(drawer).findByRole('button', { name: 'لم يحضر' }));
    expect(await within(drawer).findByRole('alert')).toHaveTextContent('تغيّر الموعد قبل قليل');
    expect(within(drawer).getByRole('button', { name: 'لم يحضر' })).toBeEnabled();
  });

  it('asks for a reason before cancelling, and staff without the permission see no actions', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(detail()));
    api.POST.mockResolvedValue(ok({}));
    const { unmount } = renderWithQuery(
      <AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />,
    );
    await user.click(await screen.findByRole('button', { name: 'إلغاء الموعد' }));
    const dialog = screen.getByRole('dialog', { name: 'إلغاء الموعد؟' });
    expect(within(dialog).getByRole('button', { name: 'نعم، ألغِ الموعد' })).toBeDisabled();
    await user.type(within(dialog).getByLabelText('سبب الإلغاء'), 'إغلاق مبكر');
    await user.click(within(dialog).getByRole('button', { name: 'نعم، ألغِ الموعد' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: 'b1' } },
        body: { to: 'CancelledByShop', reason: 'إغلاق مبكر', version: 4 },
      }),
    );
    unmount();

    renderWithQuery(
      <AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate={false} onClose={() => {}} />,
    );
    await screen.findByText('عبدالله الشمري');
    expect(screen.queryByRole('button', { name: 'حضر العميل' })).not.toBeInTheDocument();
  });
});

const OFFERS: WalkInOffer[] = [
  {
    kind: 'service',
    id: 's1',
    nameAr: 'حلاقة شعر',
    nameEn: 'Haircut',
    price: 60,
    currency: 'SAR',
    durationMinutes: 30,
  },
  {
    kind: 'package',
    id: 'k1',
    nameAr: 'باقة شعر ولحية',
    nameEn: null,
    price: 85,
    currency: 'SAR',
    durationMinutes: 50,
  },
];

const options = {
  date: '2026-10-08',
  timeZone: 'Asia/Riyadh',
  now: '2026-10-08T14:12:00Z',
  durationMinutes: 30,
  slotStepMinutes: 5,
  professionals: [
    {
      id: 'p1',
      nameAr: 'فيصل القحطاني',
      nameEn: 'Faisal',
      freeNow: false,
      nextFreeAt: '2026-10-08T15:20:00Z',
      starts: ['2026-10-08T15:20:00Z', '2026-10-08T15:25:00Z'],
    },
    {
      id: 'p2',
      nameAr: 'سلطان الحربي',
      nameEn: 'Sultan',
      freeNow: true,
      nextFreeAt: '2026-10-08T14:15:00Z',
      starts: ['2026-10-08T14:15:00Z'],
    },
  ],
};

describe('WalkInFlow (s-walkin, D-035)', () => {
  it('has no phone field, starts now with the free professional, and sends an idempotent walk-in', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(options));
    api.POST.mockResolvedValue(ok({ id: 'w1', reference: 'TR-1001', version: 1 }, 201));
    const { container } = renderWithQuery(
      <WalkInFlow offers={OFFERS} today="2026-10-08" timeZone="Asia/Riyadh" />,
    );

    expect(container.querySelector('input[type="tel"], input[autocomplete="tel"]')).toBeNull();
    expect(screen.queryByLabelText(/جوال|هاتف/)).not.toBeInTheDocument();
    expect(screen.getByTestId('walk-in-no-phone')).toHaveTextContent('لا يُطلب رقم جوال العميل');

    await user.click(await screen.findByRole('radio', { name: /سلطان الحربي/ }));
    expect(screen.getByRole('radio', { name: /ابدأ الآن/ })).toBeChecked();
    expect(screen.getByRole('button', { name: 'تسجيل الحجز' })).toBeDisabled();
    await user.type(screen.getByLabelText('اسم العميل'), 'سعود الرشيد');
    await expectNoAxeViolations(container);
    await user.click(screen.getByRole('button', { name: 'تسجيل الحجز' }));

    expect(await screen.findByText('سُجل الحجز الحضوري')).toBeInTheDocument();
    const [, init] = api.POST.mock.calls[0]!;
    expect(init.body).toEqual({
      serviceId: 's1',
      packageId: null,
      professionalId: 'p2',
      startsAt: null,
      customerName: 'سعود الرشيد',
      note: null,
    });
    expect(init.params.header['Idempotency-Key']).toMatch(/[0-9a-f-]{32,36}/);
  });

  it('lists a busy professional by their next free time, and a taken time sends the desk back to fresh times', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(options));
    api.POST.mockResolvedValue(fail(409, 'booking.slot_unavailable'));
    renderWithQuery(<WalkInFlow offers={OFFERS} today="2026-10-08" timeZone="Asia/Riyadh" />);

    const faisal = await screen.findByRole('radio', { name: /فيصل القحطاني/ });
    expect(faisal.closest('label')).toHaveTextContent('متاح ٦:٢٠ م');
    await user.click(faisal);
    expect(screen.queryByRole('radio', { name: /ابدأ الآن/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: '٦:٢٥ م' }));
    await user.type(screen.getByLabelText('اسم العميل'), 'ناصر');
    await user.click(screen.getByRole('button', { name: 'تسجيل الحجز' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('لم يعد هذا الوقت متاحاً');
    expect(api.POST.mock.calls[0]![1].body.startsAt).toBe('2026-10-08T15:25:00Z');
    await waitFor(() => expect(api.GET.mock.calls.length).toBeGreaterThan(1));
  });
});

describe('ShopBanners (spec §13, §15)', () => {
  const subscription = (status: string, hidden: boolean) => ({
    status,
    planNameAr: 'سنوي',
    planNameEn: 'Annual',
    startDate: '2026-01-01',
    endDate: '2026-10-08',
    daysRemaining: 9,
    elapsedPercent: 97,
    expiringSoonThresholdDays: 30,
    hiddenFromDiscovery: hidden,
    renewals: [],
  });

  it('warns about the pause and an ending subscription, and says when the shop is hidden', async () => {
    const { ShopBanners } = await import('../ShopBanners');
    const { unmount } = renderWithIntl(
      <ShopBanners paused subscription={subscription('ExpiringSoon', false) as never} />,
    );
    expect(screen.getByText('الحجز الإلكتروني متوقف مؤقتاً')).toBeInTheDocument();
    expect(screen.getByText('ينتهي اشتراكك بعد 9 أيام')).toBeInTheDocument();
    unmount();

    renderWithIntl(<ShopBanners paused={false} subscription={subscription('Expired', true) as never} />);
    expect(screen.getByRole('alert')).toHaveTextContent('انتهى اشتراكك');
    expect(screen.getByRole('alert')).toHaveTextContent('محلك مخفي من الاكتشاف');
    expect(screen.queryByText('الحجز الإلكتروني متوقف مؤقتاً')).not.toBeInTheDocument();
  });
});
