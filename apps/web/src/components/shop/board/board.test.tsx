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
    visit: 'Returning',
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
    expect(drawer).toHaveTextContent('زبون عائد');
    expect(drawer).toHaveTextContent('٨٥ \u20C1 — يُدفع في المحل');
    // D-152 (2-8): nothing at all is said about an online customer's number.
    expect(within(drawer).queryByTestId('no-phone-note')).not.toBeInTheDocument();
    expect(drawer).not.toHaveTextContent('رقم العميل');
    expect(drawer.textContent).not.toMatch(/\+966|05\d{8}/);
    expect(
      within(drawer)
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(expect.arrayContaining(['وصل', 'الزبون ما حضر (تأخر أكثر من ربع ساعة)', 'إلغاء الموعد']));
    expect(within(drawer).queryByRole('button', { name: 'انتهت الخدمة' })).not.toBeInTheDocument();
    await expectNoAxeViolations(drawer);

    await user.click(within(drawer).getByRole('button', { name: 'وصل' }));
    await waitFor(() =>
      expect(within(drawer).queryByRole('button', { name: 'وصل' })).not.toBeInTheDocument(),
    );
    expect(within(drawer).getAllByText('حضر العميل').length).toBeGreaterThan(0);
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: 'b1' } },
        body: { to: 'Arrived', reason: null, version: 4 },
      }),
    );
    expect(await within(drawer).findByRole('button', { name: 'انتهت الخدمة' })).toBeInTheDocument();
  });

  it('asks before «انتهت الخدمة» and only then finishes the visit (D-136)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(detail('Arrived', ['Completed'])));
    api.POST.mockResolvedValue(ok({}));
    renderWithQuery(<AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />);
    const drawer = await screen.findByTestId('appointment-drawer');

    await user.click(await within(drawer).findByRole('button', { name: 'انتهت الخدمة' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'انتهت خدمة عبدالله الشمري؟' });
    expect(api.POST).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole('button', { name: 'نعم، انتهت الخدمة' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: 'b1' } },
        body: { to: 'Completed', reason: null, version: 4 },
      }),
    );
  });

  it('rolls back and says so when the booking changed meanwhile (409)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(detail()));
    api.POST.mockResolvedValue(fail(409, 'resource.concurrency_conflict'));
    renderWithQuery(<AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />);

    const drawer = await screen.findByTestId('appointment-drawer');
    await user.click(await within(drawer).findByRole('button', { name: 'وصل' }));
    expect(await within(drawer).findByRole('alert')).toHaveTextContent('تغيّر الموعد قبل قليل');
    expect(within(drawer).getByRole('button', { name: 'وصل' })).toBeEnabled();
  });

  it('asks before «ما حضر», shows a walk-in’s mobile to a barber, and lets the owner only watch (D-147)', async () => {
    const user = userEvent.setup();
    const walkIn = detail();
    walkIn.booking = {
      ...walkIn.booking,
      channel: 'Phone',
      customerPhone: '+966501234567',
    } as typeof walkIn.booking;
    api.GET.mockResolvedValue(ok(walkIn));
    api.POST.mockResolvedValue(ok({}));
    const { unmount } = renderWithQuery(
      <AppointmentDrawer
        bookingId="b1"
        timeZone="Asia/Riyadh"
        canUpdate
        ownProfessionalId="p1"
        onClose={() => {}}
      />,
    );
    const drawer = await screen.findByTestId('appointment-drawer');
    expect(await within(drawer).findByTestId('customer-phone')).toHaveTextContent('+966501234567');
    await user.click(within(drawer).getByRole('button', { name: 'الزبون ما حضر (تأخر أكثر من ربع ساعة)' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'عبدالله الشمري ما حضر؟' });
    expect(api.POST).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole('button', { name: 'نعم، ما حضر' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: 'b1' } },
        body: { to: 'NoShow', reason: null, version: 4 },
      }),
    );
    unmount();

    // The owner (no update permission) sees the booking, its notes and its log, and no button to act.
    renderWithQuery(
      <AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate={false} onClose={() => {}} />,
    );
    const watched = await screen.findByTestId('appointment-drawer');
    await within(watched).findByText('عبدالله الشمري');
    expect(within(watched).queryByRole('button', { name: 'وصل' })).not.toBeInTheDocument();
    expect(within(watched).queryByRole('button', { name: 'إلغاء الموعد' })).not.toBeInTheDocument();
    expect(within(watched).queryByLabelText('ملاحظة جديدة')).not.toBeInTheDocument();
  });

  it('asks for a ready reason before cancelling, «سبب ثاني» typed, and staff without the permission see no actions (D-151, 2-7)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(detail()));
    api.POST.mockResolvedValue(ok({}));
    const { unmount } = renderWithQuery(
      <AppointmentDrawer bookingId="b1" timeZone="Asia/Riyadh" canUpdate onClose={() => {}} />,
    );
    await user.click(await screen.findByRole('button', { name: 'إلغاء الموعد' }));
    const dialog = screen.getByRole('dialog', { name: 'إلغاء الموعد؟' });
    const confirm = within(dialog).getByRole('button', { name: 'نعم، ألغِ الموعد' });
    expect(confirm).toBeDisabled();
    for (const reason of ['الموظف تعبان', 'ظرف طارئ', 'المحل مسكّر (كهرباء أو غيره)', 'سبب ثاني'])
      expect(within(dialog).getByRole('radio', { name: reason })).toBeInTheDocument();
    expect(within(dialog).queryByRole('textbox')).not.toBeInTheDocument();

    // «سبب ثاني» needs the reason typed.
    await user.click(within(dialog).getByRole('radio', { name: 'سبب ثاني' }));
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText('اكتب السبب'), 'إغلاق مبكر');
    expect(confirm).toBeEnabled();

    // A ready reason is enough on its own.
    await user.click(within(dialog).getByRole('radio', { name: 'ظرف طارئ' }));
    await user.click(confirm);
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/bookings/{bookingId}/transitions', {
        params: { path: { bookingId: 'b1' } },
        body: { to: 'CancelledByShop', reason: null, reasonCode: 'Emergency', version: 4 },
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

/** The barber's reward lookup (D-149) answers by path; anything else is the walk-in booking. */
const noReward = {
  rewardReady: false,
  rewardType: null,
  rewardServiceId: null,
  rewardAmount: null,
  titleAr: null,
  titleEn: null,
};
const posts = (booking: unknown, lookup: unknown = noReward) =>
  api.POST.mockImplementation(async (path: string) =>
    path.endsWith('/loyalty/lookup') ? ok(lookup) : booking,
  );
const walkInCalls = () => api.POST.mock.calls.filter(([path]) => path === '/api/v1/shop/bookings/walk-in');

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
      durationMinutes: 30,
    },
    {
      id: 'p2',
      nameAr: 'سلطان الحربي',
      nameEn: 'Sultan',
      freeNow: true,
      nextFreeAt: '2026-10-08T14:15:00Z',
      starts: ['2026-10-08T14:15:00Z'],
      durationMinutes: 30,
    },
  ],
};

describe('WalkInFlow (s-walkin, D-035, D-134, D-147)', () => {
  it('shows a barber’s own length on their card, and the chosen barber’s length in the total and the end (D-149)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(
      ok({
        ...options,
        professionals: [{ ...options.professionals[0]!, durationMinutes: 45 }, options.professionals[1]!],
      }),
    );
    renderWithQuery(<WalkInFlow offers={OFFERS} today="2026-10-08" timeZone="Asia/Riyadh" />);

    const faisal = await screen.findByRole('radio', { name: /فيصل القحطاني/ });
    const sultan = screen.getByRole('radio', { name: /سلطان الحربي/ });
    // Faisal takes 45 minutes for the haircut; Sultan the standard 30, so his card says nothing more.
    expect(within(faisal.closest('label')!).getByTestId('walk-in-own-length')).toHaveTextContent('٤٥');
    expect(within(sultan.closest('label')!).queryByTestId('walk-in-own-length')).not.toBeInTheDocument();
    expect(screen.getByTestId('walk-in-total')).toHaveTextContent('٣٠');

    await user.click(faisal);
    expect(screen.getByTestId('walk-in-total')).toHaveTextContent('٤٥');
    await user.click(screen.getByRole('radio', { name: '٦:٢٠ م' }));
    // 6:20 pm plus his 45 minutes.
    expect(screen.getByText('ينتهي').closest('div')).toHaveTextContent('٧:٠٥ م');
  });

  it('numbers its five steps whatever is chosen, adds several services up, and books a phone appointment for a colleague', async () => {
    const user = userEvent.setup();
    const offers: WalkInOffer[] = [
      ...OFFERS,
      {
        kind: 'service',
        id: 's2',
        nameAr: 'تحديد لحية',
        nameEn: 'Beard',
        price: 35,
        currency: 'SAR',
        durationMinutes: 20,
      },
    ];
    api.GET.mockResolvedValue(ok(options));
    api.POST.mockResolvedValue(ok({ id: 'w2', reference: 'TR-1002', version: 1 }, 201));
    renderWithQuery(
      <WalkInFlow offers={offers} today="2026-10-08" timeZone="Asia/Riyadh" ownProfessionalId="p1" />,
    );

    // The steps read ١ to ٥ before a barber is picked (the «١، ٢، ٤» bug).
    const legends = screen.getAllByRole('group').map((g) => g.querySelector('legend')?.textContent ?? '');
    expect(legends.filter((l) => /^[١٢٣٤٥]\./.test(l)).map((l) => l[0])).toEqual(['١', '٢', '٣', '٤', '٥']);

    await user.click(screen.getByRole('radio', { name: /بالتلفون/ }));
    await user.click(screen.getByRole('checkbox', { name: /تحديد لحية/ }));
    expect(screen.getByTestId('walk-in-total')).toHaveTextContent(/خدمتان.*٩٥/);
    expect(await screen.findByRole('radio', { name: /فيصل القحطاني.*\(أنت\)/ })).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /سلطان الحربي/ }));
    await user.click(within(screen.getByTestId('walk-in-times')).getByRole('radio', { name: /٥:١٥|5:15/ }));
    await user.type(screen.getByLabelText('اسم العميل'), 'خالد');
    await user.click(screen.getByRole('button', { name: 'تسجيل الموعد' }));
    expect(await screen.findByText('سُجّل الموعد بالتلفون')).toBeInTheDocument();
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({
      serviceIds: ['s1', 's2'],
      packageId: null,
      professionalId: 'p2',
      startsAt: '2026-10-08T14:15:00Z',
      channel: 'Phone',
    });
  });

  it('books a package together with another service, and never a service the package already holds (D-148)', async () => {
    const user = userEvent.setup();
    const offers: WalkInOffer[] = [
      OFFERS[0]!,
      {
        kind: 'service',
        id: 's2',
        nameAr: 'تحديد لحية',
        nameEn: 'Beard',
        price: 35,
        currency: 'SAR',
        durationMinutes: 20,
      },
      { ...OFFERS[1]!, itemIds: ['s1'] },
    ];
    api.GET.mockResolvedValue(ok(options));
    api.POST.mockResolvedValue(ok({ id: 'w3', reference: 'GH-4K7QM', version: 1 }, 201));
    renderWithQuery(<WalkInFlow offers={offers} today="2026-10-08" timeZone="Asia/Riyadh" />);

    // The haircut is chosen first; the package holds it, so choosing the package takes it off and says so.
    expect(screen.getByRole('checkbox', { name: /حلاقة شعر/ })).toBeChecked();
    await user.click(screen.getByRole('checkbox', { name: /باقة شعر ولحية/ }));
    expect(screen.getByRole('status')).toHaveTextContent('شلنا حلاقة شعر لأنها داخل «باقة شعر ولحية»');
    expect(screen.getByRole('checkbox', { name: /حلاقة شعر/ })).not.toBeChecked();
    await user.click(screen.getByRole('checkbox', { name: /حلاقة شعر/ }));
    expect(screen.getByRole('checkbox', { name: /حلاقة شعر/ })).not.toBeChecked();
    expect(screen.getByRole('status')).toHaveTextContent('«حلاقة شعر» موجودة في «باقة شعر ولحية»');

    // Another service goes with the package: one booking, the lengths and prices added.
    await user.click(screen.getByRole('checkbox', { name: /تحديد لحية/ }));
    expect(screen.getByTestId('walk-in-total')).toHaveTextContent(/خدمتان.*١٢٠/);
    await waitFor(() =>
      expect(api.GET).toHaveBeenLastCalledWith('/api/v1/shop/availability/walk-in', {
        params: { query: { serviceIds: ['s2'], packageId: 'k1', date: '2026-10-08' } },
      }),
    );
    await user.click(await screen.findByRole('radio', { name: /سلطان الحربي/ }));
    await user.type(screen.getByLabelText('اسم العميل'), 'خالد');
    await user.click(screen.getByRole('button', { name: 'تسجيل الحجز' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({ serviceIds: ['s2'], packageId: 'k1' });
  });

  it('takes the mobile for the platform only, starts now with the free professional, and sends an idempotent walk-in', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(options));
    posts(ok({ id: 'w1', reference: 'TR-1001', version: 1 }, 201));
    const { container } = renderWithQuery(
      <WalkInFlow offers={OFFERS} today="2026-10-08" timeZone="Asia/Riyadh" />,
    );

    // The client removed the sentence under the mobile (D-137).
    expect(screen.queryByTestId('walk-in-phone-privacy')).not.toBeInTheDocument();

    await user.click(await screen.findByRole('radio', { name: /سلطان الحربي/ }));
    expect(screen.getByRole('radio', { name: /ابدأ الآن/ })).toBeChecked();
    expect(screen.getByRole('button', { name: 'تسجيل الحجز' })).toBeDisabled();
    await user.type(screen.getByLabelText('اسم العميل'), 'سعود الرشيد');
    expect(screen.getByRole('button', { name: 'تسجيل الحجز' })).toBeEnabled();
    const mobile = screen.getByLabelText(/جوال العميل/);
    expect(mobile).toHaveAttribute('autocomplete', 'off');
    await user.type(mobile, '412');
    await user.tab();
    expect(await screen.findByText('أدخل رقم جوال سعودياً يبدأ بـ 5')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تسجيل الحجز' })).toBeDisabled();
    await user.clear(mobile);
    await user.type(mobile, '٠٥٥١٢٣٤٥٦٧');
    await expectNoAxeViolations(container);
    await user.click(screen.getByRole('button', { name: 'تسجيل الحجز' }));

    expect(await screen.findByText('سُجل الحجز الحضوري')).toBeInTheDocument();
    expect(container).not.toHaveTextContent(/551|٥٥١/);
    // The complete number was checked for a loyalty reward (D-149), in the body, never the URL; none was ready.
    expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/loyalty/lookup', {
      body: { phone: '+966551234567' },
    });
    const [, init] = walkInCalls()[0]!;
    expect(init.body).toEqual({
      serviceId: null,
      serviceIds: ['s1'],
      packageId: null,
      professionalId: 'p2',
      startsAt: null,
      customerName: 'سعود الرشيد',
      customerPhone: '+966551234567',
      note: null,
      channel: 'WalkIn',
      useLoyaltyReward: false,
    });
    expect(init.params.header['Idempotency-Key']).toMatch(/[0-9a-f-]{32,36}/);

    // Another walk-in starts with an empty mobile field, and the mobile is optional (D-134).
    await user.click(screen.getByRole('button', { name: 'حجز حضوري آخر' }));
    expect(screen.getByLabelText(/جوال العميل/)).toHaveValue('');
    await user.click(screen.getByRole('radio', { name: /ابدأ الآن/ }));
    await user.type(screen.getByLabelText('اسم العميل'), 'م');
    await user.click(screen.getByRole('button', { name: 'تسجيل الحجز' }));
    await waitFor(() => expect(walkInCalls()).toHaveLength(2));
    expect(walkInCalls()[1]![1].body).toMatchObject({ customerName: 'م', customerPhone: null });
  });

  it('offers the typed number’s ready reward, takes it off the total, and sends it with the walk-in (D-149)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(options));
    posts(ok({ id: 'w4', reference: 'GH-4K7QR', version: 1 }, 201), {
      rewardReady: true,
      rewardType: 'AmountOff',
      rewardServiceId: null,
      rewardAmount: 20,
      titleAr: 'خصم ٢٠ ⃁',
      titleEn: 'SAR 20 off',
    });
    renderWithQuery(<WalkInFlow offers={OFFERS} today="2026-10-08" timeZone="Asia/Riyadh" />);

    await user.click(await screen.findByRole('radio', { name: /سلطان الحربي/ }));
    await user.type(screen.getByLabelText('اسم العميل'), 'سعود');
    await user.type(screen.getByLabelText(/جوال العميل/), '0551234567');
    const reward = await screen.findByTestId('walk-in-reward');
    expect(reward).toHaveTextContent('لدى العميل مكافأة جاهزة: خصم ٢٠ ⃁');
    await user.click(within(reward).getByRole('checkbox', { name: /استخدم مكافأة العميل/ }));
    expect(screen.getByText('خصم المكافأة').closest('div')).toHaveTextContent('−٢٠ ⃁');
    expect(screen.getByText('المبلغ المستحق').closest('div')).toHaveTextContent('٤٠ ⃁');

    // Another number drops the reward: it belonged to the number typed before.
    await user.clear(screen.getByLabelText(/جوال العميل/));
    expect(screen.queryByTestId('walk-in-reward')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/جوال العميل/), '0551234567');
    await user.click(await screen.findByRole('checkbox', { name: /استخدم مكافأة العميل/ }));
    await user.click(screen.getByRole('button', { name: 'تسجيل الحجز' }));
    await waitFor(() => expect(walkInCalls()).toHaveLength(1));
    expect(walkInCalls()[0]![1].body).toMatchObject({
      customerPhone: '+966551234567',
      useLoyaltyReward: true,
    });
  });

  it('never offers the reward with a package (D-149)', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue(ok(options));
    posts(ok({ id: 'w5', reference: 'GH-4K7QS', version: 1 }, 201), {
      rewardReady: true,
      rewardType: 'AmountOff',
      rewardServiceId: null,
      rewardAmount: 20,
      titleAr: null,
      titleEn: null,
    });
    renderWithQuery(<WalkInFlow offers={OFFERS} today="2026-10-08" timeZone="Asia/Riyadh" />);
    await user.click(screen.getByRole('checkbox', { name: /باقة شعر ولحية/ }));
    await user.type(screen.getByLabelText(/جوال العميل/), '0551234567');
    const reward = await screen.findByTestId('walk-in-reward');
    expect(within(reward).getByRole('checkbox', { name: /استخدم مكافأة العميل/ })).toBeDisabled();
    expect(reward).toHaveTextContent('لا تُستخدم المكافأة مع الباقات.');
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
    expect(screen.getByText('ينتهي اشتراكك بعد ٩ أيام')).toBeInTheDocument();
    unmount();

    renderWithIntl(<ShopBanners paused={false} subscription={subscription('Expired', true) as never} />);
    expect(screen.getByRole('alert')).toHaveTextContent('انتهى اشتراكك');
    expect(screen.getByRole('alert')).toHaveTextContent('محلك مخفي من الاكتشاف');
    expect(screen.queryByText('الحجز الإلكتروني متوقف مؤقتاً')).not.toBeInTheDocument();
  });
});
