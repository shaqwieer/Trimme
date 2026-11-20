import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement, ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FavoriteButton } from '@/components/favorites/FavoriteButton';
import type { WizardOffer } from '@/lib/booking/wizard';
import { addDays, formatLocalDate, todayLocal } from '@/lib/i18n/localDate';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { BookingListItem, type CustomerBooking } from './BookingCards';
import { CancelBookingButton } from './BookingDetailClient';
import { BookingPolicy, CreatedBanner } from './BookingDetailParts';
import { HourMinutePicker } from '@/components/ui/booking';
import { BookingWizard, type WizardPro } from './BookingWizard';
import { RescheduleForm } from './RescheduleForm';
import { ReviewForm } from './ReviewForm';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() }));
const session = vi.hoisted(() => ({ refresh: vi.fn() }));
const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));

vi.mock('@/lib/api/client', () => ({ browserApi: api, refreshSession: session.refresh }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => router,
  usePathname: () => '/shops/barber-house',
  Link: ({ href, children, ...props }: { href: string; children: ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

const ok = <T,>(data: T, status = 200) => ({ data, response: new Response(null, { status }) });
const fail = (status: number, errorCode: string) => ({
  error: { status, errorCode, title: errorCode },
  response: new Response(null, { status }),
});

const SERVICE: WizardOffer = {
  kind: 'service',
  id: 'svc-1',
  nameAr: 'قص وتصفيف',
  nameEn: 'Cut and style',
  descriptionAr: 'قص مع غسيل',
  descriptionEn: null,
  price: 85,
  currency: 'SAR',
  durationMinutes: 30,
  professionalIds: ['omar'],
};
const PROS: WizardPro[] = [
  {
    id: 'omar',
    nameAr: 'عمر السالم',
    nameEn: 'Omar',
    specialtyAr: null,
    specialtyEn: null,
    avatarUrl: null,
    rating: 4.7,
    reviewCount: 3,
    languages: ['ar', 'tr'],
  },
  {
    id: 'majed',
    nameAr: 'ماجد العتيبي',
    nameEn: 'Majed',
    specialtyAr: null,
    specialtyEn: null,
    avatarUrl: null,
    rating: 0,
    reviewCount: 0,
    languages: [],
  },
];
const SHOP = {
  slug: 'barber-house',
  nameAr: 'باربر هاوس',
  nameEn: 'Barber House',
  area: 'حطين، الرياض',
  timeZone: 'Asia/Riyadh',
  cancellationCutoffMinutes: 120,
  logoUrl: null,
};

// The strip starts at the shop's today (D-125), so the test days are today and tomorrow.
const D1 = todayLocal('Asia/Riyadh');
const D2 = addDays(D1, 1);
const dayName = (date: string) => new RegExp(formatLocalDate(date, 'ar', { day: 'numeric', month: 'long' }));
const DATES = {
  from: D1,
  to: D2,
  timeZone: 'Asia/Riyadh',
  bookable: true,
  blockedReason: null,
  dates: [
    { date: D1, slotCount: 2 },
    { date: D2, slotCount: 0 },
  ],
};
const BEARD: WizardOffer = {
  ...SERVICE,
  id: 'svc-2',
  nameAr: 'تهذيب لحية',
  nameEn: 'Beard trim',
  descriptionAr: null,
  price: 40,
  durationMinutes: 20,
  professionalIds: ['omar', 'majed'],
};
const slotsOf = (times: Array<[string, string]>) => ({
  date: D1,
  timeZone: 'Asia/Riyadh',
  bookable: true,
  blockedReason: null,
  slots: times.map(([localTime, startsAt]) => ({
    localTime,
    startsAt,
    endsAt: startsAt,
    period: 'Morning',
    professionalIds: ['omar'],
  })),
});

/** The booking draft cookie's value (D-148), decoded; empty when there is none. */
const draft = () =>
  decodeURIComponent(
    document.cookie
      .split('; ')
      .find((c) => c.startsWith('gh-booking-draft='))
      ?.slice('gh-booking-draft='.length) ?? '',
  );

function renderWizard(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function availability(
  slots = slotsOf([
    ['10:00', '2026-10-01T07:00:00Z'],
    ['10:30', '2026-10-01T07:30:00Z'],
  ]),
) {
  api.GET.mockImplementation(async (path: string) => (path.endsWith('/dates') ? ok(DATES) : ok(slots)));
}

beforeEach(() => {
  for (const fn of [
    api.GET,
    api.POST,
    api.PUT,
    api.DELETE,
    session.refresh,
    router.push,
    router.replace,
    router.refresh,
  ]) {
    fn.mockReset();
  }
  window.history.replaceState(null, '', '/ar/shops/barber-house/book');
});

describe('BookingWizard (c-booking, D-028, D-096)', () => {
  // The longest walk in the suite (five steps and an axe pass), hence the longer timeout.
  it('walks service → any specialist → day and time → review, with the price paid at the salon and no payment step', async () => {
    const user = userEvent.setup();
    availability();
    api.POST.mockResolvedValue(ok({ id: 'booking-1', status: 'Confirmed' }, 201));
    const { container } = renderWizard(
      <BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />,
    );

    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الخدمات');
    expect(screen.getByRole('navigation', { name: 'خطوات الحجز' })).toHaveTextContent(
      /الصالون.*الخدمة.*الوقت.*التأكيد/,
    );
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: /قص وتصفيف/ }));
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    // Only the professionals assigned to the service, "any" preselected (design rule 2).
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر المختص');
    expect(screen.getByRole('radio', { name: /أي مختص متاح/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /عمر السالم/ })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /ماجد/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    // Dates come from the API; a day without slots cannot be picked.
    expect(window.location.search).toContain('step=date');
    const day = await screen.findByRole('radio', { name: dayName(D1) });
    expect(screen.getByRole('radio', { name: dayName(D2) })).toBeDisabled();
    await user.click(day);
    expect(screen.getByRole('status')).toHaveTextContent('وقتان متاحان');
    // The day's times show right under the days, without a Next (D-129); the month is on each day.
    expect(day.closest('label')).toHaveTextContent(formatLocalDate(D1, 'ar', { month: 'short' }));
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر اليوم والوقت');
    // A day alone is not enough: Next waits for a time.
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();

    // Hour first, then that hour's minutes (one hour here, so its minutes are already shown).
    expect(await screen.findByRole('radio', { name: /١٠ ص/ })).toBeChecked();
    await user.click(await screen.findByRole('radio', { name: /١٠:٣٠/ }));
    expect(window.location.search).toContain('time=10%3A30');
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    const review = await screen.findByTestId('booking-review');
    expect(review).toHaveTextContent('الإجمالي (يُدفع في الصالون)');
    expect(review).toHaveTextContent('٨٥ \u20C1');
    expect(review).toHaveTextContent('أي مختص متاح — نحدده عند التأكيد');
    expect(review).toHaveTextContent('ساعتين');
    // R-NEG-02: nothing asks for a card or a payment.
    expect(container.querySelector('input[autocomplete^="cc-"]')).toBeNull();
    expect(screen.queryByText(/بطاقة|الدفع الآن|ادفع/)).not.toBeInTheDocument();
    await expectNoAxeViolations(container);

    await user.type(screen.getByLabelText(/ملاحظة للمختص/), 'تدريج');
    await user.click(screen.getByRole('button', { name: 'تأكيد الحجز' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/account/bookings/booking-1?created=1'));
    const [, init] = api.POST.mock.calls[0]!;
    expect(init.params.header['Idempotency-Key']).toMatch(/[0-9a-f-]{32,36}/);
    expect(init.body).toEqual({
      shopSlug: 'barber-house',
      serviceId: 'svc-1',
      serviceIds: null,
      packageId: null,
      professionalId: null,
      startsAt: '2026-10-01T07:30:00Z',
      note: 'تدريج',
      useLoyaltyReward: false,
      loyaltyRewardId: null,
    });
  }, 15_000);

  it('offers the customer their loyalty coupon on the review step and sends the chosen one (D-132)', async () => {
    const user = userEvent.setup();
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    renderWizard(
      <BookingWizard
        shop={SHOP}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
        rewards={[
          {
            id: 'reward-1',
            titleAr: 'حلاقة مجانية',
            titleEn: 'Free haircut',
            kind: 'FreeService',
            discountPercent: null,
          },
        ]}
      />,
    );

    const picker = await screen.findByTestId('loyalty-picker');
    expect(within(picker).getByRole('radio', { name: /لا أريد استخدامها الآن/ })).toBeChecked();
    await user.click(within(picker).getByRole('radio', { name: /استخدم الكوبون: حلاقة مجانية/ }));
    // D-152 (the user's answer): an old free-service coupon makes the booking free, in the total and the footer.
    expect(screen.getByTestId('loyalty-due')).toHaveTextContent('٨٥ ⃁');
    expect(screen.getByTestId('loyalty-due').lastElementChild).toHaveTextContent('٠ ⃁');
    expect(screen.getByTestId('wizard-footer-price')).toHaveTextContent('٠ ⃁');
    // The hint no longer says the shop applies it at the visit (the client's 1-3 screenshot).
    expect(picker).toHaveTextContent('يُستخدم الكوبون عند انتهاء الخدمة، ويعود إليك إذا أُلغي الموعد.');
    expect(picker).not.toHaveTextContent('يطبّقه الصالون');
    const confirm = screen.getByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({
      loyaltyRewardId: 'reward-1',
      useLoyaltyReward: false,
    });
  });

  it('«استخدم مكافأتي»: the points reward comes off the total, one reward per booking (D-149)', async () => {
    const user = userEvent.setup();
    availability();
    api.POST.mockResolvedValue(ok({ id: 'booking-1', status: 'Confirmed' }, 201));
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    renderWizard(
      <BookingWizard
        shop={SHOP}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
        rewards={[
          {
            id: 'reward-1',
            titleAr: 'حلاقة مجانية',
            titleEn: 'Free haircut',
            kind: 'FreeService',
            discountPercent: null,
          },
        ]}
        pointsReward={{
          rewardType: 'AmountOff',
          rewardServiceId: null,
          rewardServiceNameAr: null,
          rewardServiceNameEn: null,
          rewardAmount: 20,
        }}
      />,
    );

    const picker = await screen.findByTestId('loyalty-picker');
    expect(screen.queryByTestId('loyalty-discount')).not.toBeInTheDocument();
    await user.click(within(picker).getByRole('radio', { name: /استخدم مكافأتي: خصم ٢٠ ⃁/ }));
    expect(screen.getByTestId('loyalty-discount')).toHaveTextContent('−٢٠ ⃁');
    expect(screen.getByTestId('loyalty-due')).toHaveTextContent('٦٥ ⃁');
    expect(picker).toHaveTextContent('تُخصم النقاط عند انتهاء الخدمة، وتعود إليك إذا أُلغي الموعد.');

    const confirm = screen.getByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    // The points reward, not the coupon: the API refuses both together.
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({ useLoyaltyReward: true, loyaltyRewardId: null });
  });

  it('a free-service reward waits for that service, and no reward goes with a package (D-149)', async () => {
    availability();
    const freeBeard = {
      rewardType: 'FreeService' as const,
      rewardServiceId: 'svc-beard',
      rewardServiceNameAr: 'تهذيب اللحية',
      rewardServiceNameEn: 'Beard trim',
      rewardAmount: null,
    };
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    const { unmount } = renderWizard(
      <BookingWizard
        shop={SHOP}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
        pointsReward={freeBeard}
      />,
    );
    const option = await screen.findByRole('radio', { name: /استخدم مكافأتي: تهذيب اللحية مجاناً/ });
    expect(option).toBeDisabled();
    expect(screen.getByTestId('loyalty-picker')).toHaveTextContent(
      'أضف «تهذيب اللحية» إلى حجزك لتستخدم المكافأة.',
    );
    unmount();

    const PACKAGE: WizardOffer = {
      ...SERVICE,
      kind: 'package',
      id: 'pkg-1',
      nameAr: 'باقة',
      price: 110,
      itemIds: ['svc-1'],
    };
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?package=pkg-1&pro=omar&date=${D1}&time=10:00`,
    );
    renderWizard(
      <BookingWizard
        shop={SHOP}
        offers={[SERVICE, PACKAGE]}
        professionals={PROS}
        viewer="customer"
        pointsReward={{ ...freeBeard, rewardType: 'AmountOff', rewardServiceId: null, rewardAmount: 20 }}
      />,
    );
    expect(await screen.findByRole('radio', { name: /استخدم مكافأتي/ })).toBeDisabled();
    expect(screen.getByTestId('loyalty-picker')).toHaveTextContent('لا تُستخدم المكافأة مع الباقات.');
  });

  it("from a barber's page or QR code: only their services, then straight to the day and time (D-133)", async () => {
    const user = userEvent.setup();
    availability();
    const COLOUR: WizardOffer = {
      ...SERVICE,
      id: 'svc-3',
      nameAr: 'صبغة',
      nameEn: 'Colour',
      professionalIds: ['majed'],
    };
    window.history.replaceState(null, '', '/ar/shops/barber-house/book?pro=omar');
    renderWizard(
      <BookingWizard shop={SHOP} offers={[SERVICE, BEARD, COLOUR]} professionals={PROS} viewer="customer" />,
    );

    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الخدمات');
    expect(screen.getByTestId('wizard-chosen-pro')).toHaveTextContent('الحجز مع عمر السالم');
    expect(screen.getByRole('checkbox', { name: /قص وتصفيف/ })).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /صبغة/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: /تهذيب لحية/ }));
    expect(window.location.search).toContain('pro=omar');
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    // No specialist step: the barber is already chosen.
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر اليوم والوقت');
    expect(window.location.search).toContain('step=date');
    await waitFor(() =>
      expect(api.GET).toHaveBeenCalledWith(
        '/api/v1/public/shops/{slug}/availability/dates',
        expect.objectContaining({
          params: expect.objectContaining({ query: expect.objectContaining({ professionalId: 'omar' }) }),
        }),
      ),
    );

    // Back reaches the specialist step, where the barber can still be changed.
    await user.click(screen.getByRole('button', { name: 'الخطوة السابقة' }));
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر المختص');
    expect(screen.getByRole('radio', { name: /عمر السالم/ })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'الخطوة السابقة' }));

    // «تغيير المختص» shows the whole salon again, with the specialist step after the services.
    await user.click(screen.getByRole('button', { name: 'تغيير المختص' }));
    expect(screen.queryByTestId('wizard-chosen-pro')).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /صبغة/ })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'التالي' }));
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر المختص');
  });

  it('keeps a time taken meanwhile on the review and offers the nearest times there, keeping the key on a plain retry (D-152)', async () => {
    const user = userEvent.setup();
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    api.POST.mockResolvedValueOnce(fail(500, 'server.unexpected')).mockResolvedValueOnce(
      fail(409, 'booking.slot_unavailable'),
    );
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);

    const confirm = await screen.findByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'تأكيد الحجز' }));

    const panel = await screen.findByTestId('booking-conflict');
    expect(panel).toHaveAttribute('data-kind', 'taken');
    expect(within(panel).getByText('حُجز هذا الوقت للتو')).toBeInTheDocument();
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('مراجعة الحجز');
    const keys = api.POST.mock.calls.map(([, init]) => init.params.header['Idempotency-Key']);
    expect(keys[0]).toBe(keys[1]);
    // The slots are asked for again, and one tap takes another time without leaving the review.
    expect(api.GET.mock.calls.filter(([path]) => String(path).endsWith('/slots')).length).toBeGreaterThan(1);
    await user.click(await within(panel).findByRole('button', { name: /١٠:٣٠/ }));
    await waitFor(() => expect(screen.queryByTestId('booking-conflict')).not.toBeInTheDocument());
    expect(window.location.search).toContain('time=10%3A30');
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('مراجعة الحجز');
  });

  it('says when a time became too close instead of blaming another customer (D-152, 1-2)', async () => {
    const user = userEvent.setup();
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    api.POST.mockResolvedValueOnce(fail(409, 'booking.slot_too_soon'));
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);

    const confirm = await screen.findByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    const panel = await screen.findByTestId('booking-conflict');
    expect(panel).toHaveAttribute('data-kind', 'tooSoon');
    expect(panel).toHaveTextContent('الوقت صار قريب مرة');
    expect(panel).not.toHaveTextContent('سبقك عميل آخر');
  });

  it('warns of an upcoming booking at any shop with a link to it, and on a 409 at the confirm (D-155, the client’s 3-2)', async () => {
    const user = userEvent.setup();
    const active = {
      id: 'b-old',
      reference: 'TRMOLD01',
      shop: { id: 's2', slug: 'al-asala', nameAr: 'صالون الأصالة', nameEn: 'Al Asala' },
      startsAt: '2026-10-02T09:00:00Z',
      endsAt: '2026-10-02T09:30:00Z',
    };
    const { unmount } = renderWizard(
      <BookingWizard
        shop={SHOP}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
        activeBooking={active}
      />,
    );
    const warning = screen.getByTestId('active-booking');
    expect(warning).toHaveTextContent('عندك حجز قادم');
    expect(warning).toHaveTextContent('في صالون الأصالة');
    expect(within(warning).getByRole('link', { name: 'عرض الحجز' })).toHaveAttribute(
      'href',
      '/account/bookings/b-old',
    );
    unmount();

    // Booked elsewhere meanwhile: the server refuses, and the wizard names the booking.
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    api.POST.mockResolvedValueOnce(fail(409, 'booking.active_booking_exists'));
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);
    expect(screen.queryByTestId('active-booking')).not.toBeInTheDocument();
    api.GET.mockImplementation(async (path: string) =>
      path === '/api/v1/me/bookings/active'
        ? ok(active)
        : path.endsWith('/dates')
          ? ok(DATES)
          : ok(slotsOf([['10:00', '2026-10-01T07:00:00Z']])),
    );
    const confirm = await screen.findByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    expect(await screen.findByTestId('active-booking')).toHaveTextContent('صالون الأصالة');
    expect(router.push).not.toHaveBeenCalled();
  });

  it('opens with the points reward ticked from the loyalty card, its service free on its own line (D-152, 3-5 and 1-3)', async () => {
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00&reward=points`,
    );
    renderWizard(
      <BookingWizard
        shop={SHOP}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
        pointsReward={{
          rewardType: 'FreeService',
          rewardServiceId: 'svc-1',
          rewardServiceNameAr: 'قص وتصفيف',
          rewardServiceNameEn: 'Cut and style',
          rewardAmount: null,
        }}
      />,
    );

    const picker = await screen.findByTestId('loyalty-picker');
    expect(within(picker).getByRole('radio', { name: /استخدم مكافأتي/ })).toBeChecked();
    const line = within(screen.getByTestId('booking-lines')).getByTestId('free-line');
    expect(line).toHaveTextContent('٨٥ ⃁');
    expect(line).toHaveTextContent('مجانية');
    expect(screen.queryByTestId('loyalty-discount')).not.toBeInTheDocument();
    expect(screen.getByTestId('loyalty-due').lastElementChild).toHaveTextContent('٠ ⃁');
    expect(screen.getByTestId('wizard-footer-price')).toHaveTextContent('٠ ⃁');
  });

  it('checks a time from the URL against fresh slots and says when it is gone', async () => {
    availability(slotsOf([['11:00', '2026-10-01T08:00:00Z']]));
    window.history.replaceState(null, '', `/ar/shops/barber-house/book?service=svc-1&date=${D1}&time=10:00`);
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);
    await waitFor(() => expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر اليوم والوقت'));
    expect(screen.getByText('الوقت الذي اخترته لم يعد متاحاً')).toBeInTheDocument();
  });

  it('sends a guest to sign in with the review step as returnTo (after trying a silent refresh)', async () => {
    const user = userEvent.setup();
    availability();
    session.refresh.mockResolvedValue(false);
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10:00`,
    );
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="guest" />);

    expect(await screen.findByText(/نطلب رقم جوالك/)).toBeInTheDocument();
    const confirm = screen.getByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    await waitFor(() => expect(router.push).toHaveBeenCalled());
    const target = String(router.push.mock.calls[0]![0]);
    expect(target.startsWith('/auth/sign-in?returnTo=')).toBe(true);
    const returnTo = decodeURIComponent(target.split('returnTo=')[1]!);
    expect(returnTo).toBe(
      `/shops/barber-house/book?service=svc-1&pro=omar&date=${D1}&time=10%3A00&step=review`,
    );
    expect(api.POST).not.toHaveBeenCalled();
  });

  it('sends a customer without a name to complete the profile and back', async () => {
    const user = userEvent.setup();
    availability();
    api.POST.mockResolvedValue(fail(422, 'booking.profile_incomplete'));
    window.history.replaceState(null, '', `/ar/shops/barber-house/book?service=svc-1&date=${D1}&time=10:00`);
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);
    const confirm = await screen.findByRole('button', { name: 'تأكيد الحجز' });
    await waitFor(() => expect(confirm).toBeEnabled());
    await user.click(confirm);
    await waitFor(() =>
      expect(router.push).toHaveBeenCalledWith(
        expect.stringMatching(/^\/auth\/complete-profile\?returnTo=%2Fshops%2Fbarber-house%2Fbook/),
      ),
    );
  });

  it('books several services together: one barber for all, durations and prices added, sent as serviceIds', async () => {
    const user = userEvent.setup();
    availability();
    api.POST.mockResolvedValue(ok({ id: 'booking-2', status: 'Confirmed' }, 201));
    renderWizard(
      <BookingWizard
        shop={{ ...SHOP, slug: 'multi-shop' }}
        offers={[SERVICE, BEARD]}
        professionals={PROS}
        viewer="customer"
      />,
    );

    await user.click(screen.getByRole('checkbox', { name: /قص وتصفيف/ }));
    await user.click(screen.getByRole('checkbox', { name: /تهذيب لحية/ }));
    expect(screen.getByTestId('wizard-footer-note')).toHaveTextContent('خدمتان');
    expect(screen.getByText('١٢٥ \u20C1')).toBeInTheDocument();
    expect(window.location.search).toBe('?service=svc-1&service=svc-2&step=service');
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    // Only Omar does both services.
    expect(screen.getByRole('radio', { name: /عمر السالم/ })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /ماجد/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'التالي' }));
    await user.click(await screen.findByRole('radio', { name: dayName(D1) }));
    await user.click(await screen.findByRole('radio', { name: /١٠:٠٠/ }));
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    const review = await screen.findByTestId('booking-review');
    expect(review).toHaveTextContent('قص وتصفيف');
    expect(review).toHaveTextContent('تهذيب لحية');
    expect(review).toHaveTextContent('١٢٥ \u20C1');
    const datesCall = api.GET.mock.calls.find(([path]) => String(path).endsWith('/dates'))!;
    expect(datesCall[1].params.query).toMatchObject({ serviceIds: ['svc-1', 'svc-2'], serviceId: undefined });
    await user.click(screen.getByRole('button', { name: 'تأكيد الحجز' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({
      serviceId: null,
      serviceIds: ['svc-1', 'svc-2'],
    });
  });

  it('shows the services in the shop section tabs with packages last, counts the chosen ones, and books a package with a service (D-148)', async () => {
    const user = userEvent.setup();
    availability();
    const HAIR = { ...SERVICE, sectionId: 'hair' };
    const TRIM = { ...BEARD, sectionId: 'beard' };
    const WASH = {
      ...BEARD,
      id: 'svc-4',
      nameAr: 'غسيل شعر',
      price: 15,
      durationMinutes: 10,
      sectionId: null,
    };
    const COLOR = { ...BEARD, id: 'svc-3', nameAr: 'صبغة', professionalIds: ['majed'], sectionId: null };
    const PACKAGE: WizardOffer = {
      ...SERVICE,
      kind: 'package',
      id: 'pkg-1',
      nameAr: 'باقة قص ولحية',
      descriptionAr: null,
      price: 110,
      durationMinutes: 50,
      professionalIds: ['omar'],
      itemIds: ['svc-1', 'svc-2'],
    };
    renderWizard(
      <BookingWizard
        shop={{ ...SHOP, slug: 'catalog-tabs' }}
        offers={[HAIR, TRIM, WASH, COLOR, PACKAGE]}
        sections={[
          { id: 'hair', nameAr: 'الشعر', nameEn: 'Hair' },
          { id: 'beard', nameAr: 'اللحية', nameEn: 'Beard' },
        ]}
        professionals={PROS}
        viewer="customer"
      />,
    );

    // «٤-١»: the shop's sections, then «أخرى», then the packages; no «كل الخدمات» list and no «٠» on a tab.
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
      'الشعر',
      'اللحية',
      'أخرى',
      'الباقات',
    ]);
    expect(screen.queryByRole('button', { name: 'كل الخدمات' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: /قص وتصفيف/ }));
    expect(screen.getByRole('tab', { name: /^الشعر.*خدمة مختارة$/ })).toHaveTextContent('١');

    // «٤-٢»: the package says what it saves (١٢٥ for both services against ١١٠) and replaces the service inside it.
    await user.click(screen.getByRole('tab', { name: 'الباقات' }));
    expect(screen.getByText('توفّر ١٥ \u20C1')).toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: /باقة قص ولحية/ }));
    expect(screen.getByRole('status')).toHaveTextContent('شلنا قص وتصفيف لأنها داخل «باقة قص ولحية»');
    expect(screen.getByRole('tab', { name: 'الشعر' })).toHaveTextContent(/^الشعر$/);

    // A service already in the package is not added twice, and says why.
    await user.click(screen.getByRole('tab', { name: 'اللحية' }));
    await user.click(screen.getByRole('checkbox', { name: /تهذيب لحية/ }));
    expect(screen.getByRole('checkbox', { name: /تهذيب لحية/ })).not.toBeChecked();
    expect(screen.getByRole('status')).toHaveTextContent('«تهذيب لحية» موجودة في «باقة قص ولحية»');

    // Another service goes with the package: the prices and lengths add up, sent as one booking.
    await user.click(screen.getByRole('tab', { name: 'أخرى' }));
    await user.click(screen.getByRole('checkbox', { name: /غسيل شعر/ }));
    expect(screen.getByText('١٢٥ \u20C1')).toBeInTheDocument();
    expect(window.location.search).toBe('?package=pkg-1&service=svc-4&step=service');
    expect(screen.getByRole('button', { name: 'التالي' })).toBeEnabled();

    // «٤-٨»: a service no one does with the rest is named.
    await user.click(screen.getByRole('checkbox', { name: /صبغة/ }));
    expect(screen.getByText(/ما فيه مختص واحد يسوي «صبغة» مع باقي خدماتك/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();
  });

  it('keeps the choices in a first-party draft cookie (ids and step only) and restores them on a bare link, on the service step (D-148, D-157)', async () => {
    const user = userEvent.setup();
    availability();
    const first = renderWizard(
      <BookingWizard
        shop={{ ...SHOP, slug: 'draft-shop' }}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
      />,
    );
    await user.click(screen.getByRole('checkbox', { name: /قص وتصفيف/ }));
    expect(draft()).toBe('draft-shop|service=svc-1&step=service');
    first.unmount();

    // Another shop's draft, left by an earlier visit (only the cookie knows it): a bare link picks it up.
    document.cookie = `gh-booking-draft=${encodeURIComponent('cookie-shop|service=svc-1&step=service')}; Path=/`;
    window.history.replaceState(null, '', '/ar/shops/cookie-shop/book');
    renderWizard(
      <BookingWizard
        shop={{ ...SHOP, slug: 'cookie-shop' }}
        offers={[SERVICE]}
        professionals={PROS}
        viewer="customer"
      />,
    );
    await waitFor(() => expect(window.location.search).toBe('?service=svc-1&step=service'));
    // The earlier choice is shown ticked on the service step, never past it (the client's «بدون ما اختار», D-157).
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الخدمات');
    expect(screen.getByRole('checkbox', { name: /قص وتصفيف/ })).toBeChecked();
    expect(draft()).not.toMatch(/note|phone|name/);
  });

  it('«احجز الآن» on the shop page starts a new booking: the service step on the tab it showed, nothing chosen, the old draft gone (D-157)', async () => {
    const user = userEvent.setup();
    availability();
    const HAIR = { ...SERVICE, sectionId: 'hair' };
    const KIDS = { ...BEARD, id: 'svc-kids', nameAr: 'حلاقة أطفال', price: 45, sectionId: 'kids' };
    const PACKAGE: WizardOffer = {
      ...SERVICE,
      kind: 'package',
      id: 'pkg-1',
      nameAr: 'باقة شعر ولحية',
      descriptionAr: null,
      price: 85,
      durationMinutes: 50,
      itemIds: ['svc-1'],
    };
    const wizard = () => (
      <BookingWizard
        shop={{ ...SHOP, slug: 'fresh-shop' }}
        offers={[HAIR, KIDS, PACKAGE]}
        sections={[
          { id: 'hair', nameAr: 'قص الشعر', nameEn: 'Hair' },
          { id: 'kids', nameAr: 'الأطفال', nameEn: 'Kids' },
        ]}
        professionals={PROS}
        viewer="customer"
      />
    );

    // The client's video: the package chosen a minute earlier, with a day and a time (tab memory and the cookie).
    window.history.replaceState(
      null,
      '',
      `/ar/shops/fresh-shop/book?package=pkg-1&date=${D1}&time=10:00&step=review`,
    );
    const earlier = renderWizard(wizard());
    await waitFor(() => expect(draft()).toContain('fresh-shop|package=pkg-1'));
    earlier.unmount();

    // Back on the shop page, on «الأطفال», then the big «احجز الآن»: its form sends the tab and `step=service`.
    window.history.replaceState(null, '', '/ar/shops/fresh-shop/book?section=kids&step=service');
    const fresh = renderWizard(wizard());
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الخدمات');
    expect(screen.getByRole('tab', { name: 'الأطفال' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('checkbox', { name: /حلاقة أطفال/ })).not.toBeChecked();
    expect(screen.queryByTestId('wizard-footer-price')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();
    expect(draft()).toBe('');
    expect(window.location.search).toBe('?section=kids&step=service');

    // The kids' haircut alone, at its own price.
    await user.click(screen.getByRole('checkbox', { name: /حلاقة أطفال/ }));
    expect(window.location.search).toBe('?service=svc-kids&step=service');
    expect(screen.getByTestId('wizard-footer-price')).toHaveTextContent('٤٥');
    expect(draft()).toBe('fresh-shop|service=svc-kids&step=service');
    fresh.unmount();

    // A tab the booking does not have (only walk-in services there) opens the first one.
    window.history.replaceState(null, '', '/ar/shops/fresh-shop/book?section=walk-in-only&step=service');
    renderWizard(wizard());
    expect(screen.getByRole('tab', { name: 'قص الشعر' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('checkbox', { name: /قص وتصفيف/ })).not.toBeChecked();
  });

  it("a service's «احجز» on the shop page books that service alone, whatever the draft held (D-157)", async () => {
    availability();
    document.cookie = `gh-booking-draft=${encodeURIComponent('tile-shop|service=svc-1&step=service')}; Path=/`;
    window.history.replaceState(null, '', '/ar/shops/tile-shop/book?service=svc-2&step=service');
    renderWizard(
      <BookingWizard
        shop={{ ...SHOP, slug: 'tile-shop' }}
        offers={[SERVICE, BEARD]}
        professionals={PROS}
        viewer="customer"
      />,
    );
    expect(screen.getByRole('checkbox', { name: /تهذيب لحية/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /قص وتصفيف/ })).not.toBeChecked();
    expect(screen.getByTestId('wizard-footer-price')).toHaveTextContent('٤٠');
    expect(draft()).toBe('tile-shop|service=svc-2&step=service');
  });

  it('says when no single specialist does every chosen service', async () => {
    const user = userEvent.setup();
    availability();
    const COLOR = { ...BEARD, id: 'svc-3', nameAr: 'صبغة', professionalIds: ['majed'] };
    renderWizard(
      <BookingWizard
        shop={{ ...SHOP, slug: 'no-common' }}
        offers={[SERVICE, COLOR]}
        professionals={PROS}
        viewer="customer"
      />,
    );
    await user.click(screen.getByRole('checkbox', { name: /قص وتصفيف/ }));
    await user.click(screen.getByRole('checkbox', { name: /صبغة/ }));
    expect(screen.getByText(/ما فيه مختص واحد يسوي «صبغة» مع باقي خدماتك/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();
  });

  it('offers the nearest free time before the days and hours, and books it in one tap', async () => {
    const user = userEvent.setup();
    availability();
    window.history.replaceState(null, '', '/ar/shops/barber-house/book?service=svc-1&step=date');
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);

    const nearest = await screen.findByTestId('nearest-slot');
    expect(nearest).toHaveTextContent('أقرب موعد متاح');
    expect(nearest).toHaveTextContent('اليوم');
    expect(nearest).toHaveTextContent('١٠:٠٠');
    await user.click(within(nearest).getByRole('button', { name: 'احجز هذا الموعد' }));
    expect(await screen.findByTestId('booking-review')).toBeInTheDocument();
    expect(window.location.search).toContain(`date=${D1}`);
    expect(window.location.search).toContain('time=10%3A00');
  });

  it('keeps every choice when the customer goes back, with the app or the browser', async () => {
    const user = userEvent.setup();
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&service=svc-2&pro=omar&date=${D1}&time=10:30&step=time`,
    );
    renderWizard(
      <BookingWizard shop={SHOP} offers={[SERVICE, BEARD]} professionals={PROS} viewer="customer" />,
    );
    expect(await screen.findByRole('radio', { name: /١٠:٣٠/ })).toBeChecked();

    // The app's back button: the specialist is still chosen, and so are the day and time when coming forward again.
    await user.click(screen.getByRole('button', { name: 'الخطوة السابقة' }));
    expect(await screen.findByRole('radio', { name: /عمر السالم/ })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'التالي' }));
    expect(await screen.findByRole('radio', { name: dayName(D1) })).toBeChecked();
    expect(await screen.findByRole('radio', { name: /١٠:٣٠/ })).toBeChecked();

    // The browser's back button lands on an entry from before the professional and time were chosen.
    act(() =>
      window.history.replaceState(
        null,
        '',
        '/ar/shops/barber-house/book?service=svc-1&service=svc-2&step=service',
      ),
    );
    await waitFor(() => expect(window.location.search).toContain('time=10%3A30'));
    expect(window.location.search).toContain('pro=omar');
    expect(screen.getByRole('checkbox', { name: /قص وتصفيف/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /تهذيب لحية/ })).toBeChecked();
  });
});

describe('ReviewForm (c-rate, R-CUS-09)', () => {
  it('needs stars, names them, sends the chosen tags and locks after publishing', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(ok({ id: 'r1' }, 201));
    const { container } = renderWithIntl(<ReviewForm bookingId="b1" />);

    expect(screen.getByRole('button', { name: 'اختر عدد النجوم أولاً' })).toBeDisabled();
    await user.click(screen.getByRole('radio', { name: '٤ من ٥' }));
    expect(screen.getByTestId('star-label')).toHaveTextContent('ممتازة');
    await user.click(screen.getByRole('button', { name: 'النظافة' }));
    await user.click(screen.getByRole('button', { name: 'الالتزام بالوقت' }));
    await user.click(screen.getByRole('button', { name: 'النظافة' }));
    await user.type(screen.getByLabelText(/تعليق/), 'ممتاز');
    await expectNoAxeViolations(container);
    await user.click(screen.getByRole('button', { name: 'إرسال التقييم' }));

    expect(await screen.findByText('شكراً لتقييمك')).toBeInTheDocument();
    expect(api.POST).toHaveBeenCalledWith('/api/v1/me/bookings/{bookingId}/review', {
      params: { path: { bookingId: 'b1' } },
      body: { rating: 4, tags: ['Punctuality'], comment: 'ممتاز' },
    });
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
  });

  it('says so when the visit was already rated', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(fail(409, 'review.already_exists'));
    renderWithIntl(<ReviewForm bookingId="b1" />);
    await user.click(screen.getByRole('radio', { name: '٥ من ٥' }));
    await user.click(screen.getByRole('button', { name: 'إرسال التقييم' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('قيّمت هذه الزيارة من قبل.');
    expect(screen.getByRole('button', { name: 'إرسال التقييم' })).toBeDisabled();
  });
});

describe('CancelBookingButton (c-rate cancel sheet, D-015)', () => {
  it('confirms, sends the optional reason with the version read, and returns to the booking', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(ok({ id: 'b1', status: 'CancelledByCustomer' }));
    renderWithIntl(<CancelBookingButton bookingId="b1" version={7} when="الخميس ١ أكتوبر ١٠:٠٠ ص" />);

    await user.click(screen.getByRole('button', { name: 'إلغاء الموعد' }));
    const dialog = screen.getByRole('dialog', { name: /إلغاء موعد الخميس/ });
    expect(within(dialog).getByRole('button', { name: 'إبقاء الموعد' })).toHaveFocus();
    await user.click(within(dialog).getByRole('button', { name: 'تغيّر الوقت' }));
    await user.click(within(dialog).getByRole('button', { name: 'نعم، ألغِ الموعد' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/account/bookings/b1?cancelled=1'));
    expect(api.POST).toHaveBeenCalledWith('/api/v1/me/bookings/{bookingId}/cancel', {
      params: { path: { bookingId: 'b1' } },
      body: { reason: 'تغيّر الوقت', version: 7 },
    });
  });

  it('offers «إلغاء الموعد» on a «مواعيدي» card beside «تغيير الموعد», in its style, opening the cancel dialog (D-152, 2-2)', () => {
    const later = new Date(Date.now() + 2 * 86_400_000).toISOString();
    renderWithIntl(
      <ul>
        <BookingListItem
          booking={
            {
              id: 'b2',
              reference: 'GH-ABCDE',
              shop: { id: 's1', slug: 'barber-house', nameAr: 'باربر هاوس', nameEn: 'Barber House' },
              professional: { id: 'omar', nameAr: 'عمر', nameEn: 'Omar' },
              item: {
                nameAr: 'قص وتصفيف',
                nameEn: 'Cut and style',
                serviceId: 'svc-1',
                packageId: null,
                packageItems: [],
              },
              startsAt: later,
              endsAt: later,
              status: 'Confirmed',
              allowedActions: ['Reschedule', 'Cancel'],
              reviewRating: null,
              version: 4,
            } as unknown as CustomerBooking
          }
        />
      </ul>,
    );
    const card = screen.getByTestId('booking-card');
    const change = within(card).getByRole('link', { name: 'تغيير الموعد' });
    const cancel = within(card).getByRole('link', { name: 'إلغاء الموعد' });
    expect(cancel.className).toBe(change.className);
    expect(cancel).toHaveAttribute('href', '/account/bookings/b2?cancel=1');
  });

  it('offers «إعادة الحجز» on a completed visit before it is rated, at the barber with the same service (D-155, 5-4)', () => {
    const before = new Date(Date.now() - 3 * 86_400_000).toISOString();
    renderWithIntl(
      <ul>
        <BookingListItem
          booking={
            {
              id: 'b3',
              reference: 'GH-QWERT',
              shop: { id: 's1', slug: 'barber-house', nameAr: 'باربر هاوس', nameEn: 'Barber House' },
              professional: { id: 'omar', nameAr: 'عمر', nameEn: 'Omar' },
              item: {
                nameAr: 'قص وتصفيف',
                nameEn: 'Cut and style',
                serviceId: 'svc-1',
                packageId: null,
                packageItems: [],
              },
              startsAt: before,
              endsAt: before,
              status: 'Completed',
              allowedActions: ['Review'],
              reviewRating: null,
              version: 4,
            } as unknown as CustomerBooking
          }
        />
      </ul>,
    );
    expect(screen.getByRole('link', { name: 'إعادة الحجز' })).toHaveAttribute(
      'href',
      '/shops/barber-house/book?service=svc-1&pro=omar',
    );
  });

  it('opens the cancel dialog at once from the card’s link, and closing it forgets the link (D-152)', async () => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', '/ar/account/bookings/b1?cancel=1');
    renderWithIntl(<CancelBookingButton bookingId="b1" version={7} when="اليوم" openAtStart />);
    const dialog = screen.getByRole('dialog', { name: /إلغاء موعد اليوم/ });
    await user.click(within(dialog).getByRole('button', { name: 'إبقاء الموعد' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(window.location.search).toBe('');
  });

  it('shows the cutoff error when it is too late', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(fail(422, 'booking.cancellation_cutoff_passed'));
    renderWithIntl(<CancelBookingButton bookingId="b1" version={7} when="اليوم" />);
    await user.click(screen.getByRole('button', { name: 'إلغاء الموعد' }));
    await user.click(screen.getByRole('button', { name: 'نعم، ألغِ الموعد' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('انتهت مهلة الإلغاء');
  });
});

describe('FavoriteButton (R-CUS-10, D-098)', () => {
  it('links a signed-out visitor to sign in and back, without the session client', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }));
    renderWithIntl(<FavoriteButton target={{ kind: 'shop', id: 's1' }} name="باربر هاوس" />);
    const link = await screen.findByRole('link', { name: 'سجّل الدخول لحفظ باربر هاوس في المفضلة' });
    expect(link).toHaveAttribute('href', '/auth/sign-in?returnTo=%2Fshops%2Fbarber-house');
    expect(api.GET).not.toHaveBeenCalled();
    fetchMock.mockRestore();
  });

  it('without a session cookie, is the sign-in link at once and asks the API nothing (Phase 17)', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    renderWithIntl(
      <FavoriteButton target={{ kind: 'shop', id: 's1' }} name="باربر هاوس" mayBeSignedIn={false} />,
    );
    expect(screen.getByRole('link', { name: 'سجّل الدخول لحفظ باربر هاوس في المفضلة' })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    fetchMock.mockRestore();
  });

  it('toggles optimistically and rolls back when the API refuses', async () => {
    const user = userEvent.setup();
    api.DELETE.mockResolvedValue(fail(500, 'server.unexpected'));
    renderWithIntl(
      <FavoriteButton target={{ kind: 'professional', id: 'p1', shopId: 's1' }} name="عمر" initiallySaved />,
    );
    const heart = screen.getByRole('button', { name: 'أزل عمر من المفضلة' });
    expect(heart).toHaveAttribute('aria-pressed', 'true');
    await user.click(heart);
    await waitFor(() => expect(screen.getByRole('button', { name: 'أزل عمر من المفضلة' })).toBeEnabled());
    expect(screen.getByTestId('favorite-button')).toHaveAttribute('aria-pressed', 'true');

    api.DELETE.mockResolvedValue(ok(undefined, 204));
    await user.click(screen.getByTestId('favorite-button'));
    await waitFor(() => expect(screen.getByRole('button', { name: 'أضف عمر إلى المفضلة' })).toBeEnabled());
    api.PUT.mockResolvedValue(ok(undefined, 204));
    await user.click(screen.getByTestId('favorite-button'));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/me/favorites/professionals/{professionalId}', {
        params: { path: { professionalId: 'p1' } },
        body: { shopId: 's1' },
      }),
    );
  });
});

describe('booking page parts (DV-S13, DV-S10)', () => {
  it('says the booking is confirmed: every booking is, at once (D-155, the client’s 3-1)', () => {
    renderWithIntl(<CreatedBanner />);
    expect(screen.getByText('تم تأكيد حجزك')).toBeInTheDocument();
    expect(screen.queryByText('تم إرسال طلب الحجز')).not.toBeInTheDocument();
  });

  it('before the cutoff says until when; after it says the window closed, with no salon phone (D-129)', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-01T09:00:00Z'));
    const { unmount } = renderWithIntl(
      <BookingPolicy startsAt="2026-10-04T14:00:00Z" cutoffMinutes={120} canChange />,
    );
    expect(screen.getByText(/مجاني عبر Groom Here حتى/)).toHaveTextContent('٣:٠٠');
    expect(screen.queryByTestId('cutoff-passed')).not.toBeInTheDocument();
    unmount();

    renderWithIntl(<BookingPolicy startsAt="2026-10-04T14:00:00Z" cutoffMinutes={120} canChange={false} />);
    expect(screen.getByTestId('cutoff-passed')).toHaveTextContent('ساعتين قبل الموعد');
    expect(screen.queryByRole('link', { name: /اتصل/ })).not.toBeInTheDocument();
    expect(document.querySelector('a[href^="tel:"]')).toBeNull();
    vi.useRealTimers();
  });

  it('past the cutoff but still allowed (the shop gave it a new time), says until it starts (D-155, the client’s 4-4)', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-04T13:00:00Z'));
    renderWithIntl(<BookingPolicy startsAt="2026-10-04T14:00:00Z" cutoffMinutes={120} canChange />);
    expect(screen.getByText(/مجاني عبر Groom Here حتى يبدأ الموعد/)).toHaveTextContent('٥:٠٠');
    expect(screen.queryByTestId('cutoff-passed')).not.toBeInTheDocument();
    vi.useRealTimers();
  });

  it('after the shop gave the booking to another barber, says it can change until it starts (D-145)', () => {
    renderWithIntl(
      <BookingPolicy startsAt="2026-10-04T14:00:00Z" cutoffMinutes={120} canChange movedTo="عمر" />,
    );
    expect(screen.getByTestId('moved-policy')).toHaveTextContent('نُقل حجزك إلى عمر في نفس الموعد');
    expect(screen.getByTestId('moved-policy')).toHaveTextContent('٥:٠٠');
    expect(screen.queryByText(/مجاني عبر Groom Here حتى/)).not.toBeInTheDocument();
  });
});

describe('FavoriteButton after signing in (client-side return)', () => {
  it('asks again on the next mount instead of keeping the signed-out answer, however fast the return', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    const first = renderWithIntl(<FavoriteButton target={{ kind: 'shop', id: 's1' }} name="باربر هاوس" />);
    expect(await screen.findByRole('link', { name: /سجّل الدخول لحفظ/ })).toBeInTheDocument();
    first.unmount();

    // Signed in meanwhile (a fast round trip, no time passes); the page comes back without a reload.
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ shopIds: ['s1'], professionalIds: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    renderWithIntl(<FavoriteButton target={{ kind: 'shop', id: 's1' }} name="باربر هاوس" />);
    expect(await screen.findByRole('button', { name: 'أزل باربر هاوس من المفضلة' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(fetchMock).toHaveBeenCalledTimes(2);
    fetchMock.mockRestore();
  });

  it('hearts that mount together share one request', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ shopIds: [], professionalIds: ['p1'] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    renderWithIntl(
      <>
        <FavoriteButton target={{ kind: 'shop', id: 's1' }} name="باربر هاوس" />
        <FavoriteButton target={{ kind: 'professional', id: 'p1', shopId: 's1' }} name="عمر" />
      </>,
    );
    expect(await screen.findByRole('button', { name: 'أضف باربر هاوس إلى المفضلة' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'أزل عمر من المفضلة' })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    fetchMock.mockRestore();
  });
});

describe('HourMinutePicker (D-125)', () => {
  it("shows the hours first, then only the chosen hour's minutes", async () => {
    const user = userEvent.setup();
    const onValueChange = vi.fn();
    const { container } = renderWithIntl(
      <HourMinutePicker
        name="time"
        timeZone="Asia/Riyadh"
        slots={[
          { localTime: '10:00', start: '2026-10-01T07:00:00Z' },
          { localTime: '10:30', start: '2026-10-01T07:30:00Z' },
          { localTime: '11:15', start: '2026-10-01T08:15:00Z' },
        ]}
        onValueChange={onValueChange}
      />,
    );

    // The hour alone, without how many minutes it has free (D-144).
    expect(screen.getByRole('radio', { name: /١٠ ص/ })).toHaveAccessibleName('١٠ ص');
    expect(screen.queryByText(/وقتان|أوقات/)).not.toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /١٠:٣٠/ })).not.toBeInTheDocument();
    expect(screen.getByText('اختر ساعة لعرض الدقائق المتاحة فيها.')).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /١١ ص/ }));
    expect(screen.queryByRole('radio', { name: /١٠:٣٠/ })).not.toBeInTheDocument();
    // Every 5 minutes of the hour is shown (D-130); the taken ones are greyed out and cannot be picked.
    const minutesGroup = screen.getByRole('group', { name: 'اختر الدقيقة' });
    expect(minutesGroup.querySelectorAll('label, [aria-disabled="true"]')).toHaveLength(12);
    expect(within(minutesGroup).getAllByRole('radio')).toHaveLength(1);
    const taken = [...minutesGroup.querySelectorAll('[aria-disabled="true"]')];
    expect(taken.some((el) => el.textContent?.includes('١١:٢٠'))).toBe(true);
    await user.click(screen.getByRole('radio', { name: /١١:١٥/ }));
    expect(onValueChange).toHaveBeenCalledWith('2026-10-01T08:15:00Z');
    await expectNoAxeViolations(container);
  });
});

describe('RescheduleForm (D-144)', () => {
  const BOOKING = {
    id: 'bk-1',
    version: 3,
    startsAt: '2030-01-01T07:00:00Z',
    timeZone: 'Asia/Riyadh',
    shopName: 'باربر هاوس',
    area: 'حطين، الرياض',
    itemName: 'قص وتصفيف',
    professionalName: 'عمر السالم',
    durationMinutes: 30,
    price: 85,
    currency: 'SAR',
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  function wrap(node: ReactElement) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return renderWithIntl(<QueryClientProvider client={client}>{node}</QueryClientProvider>);
  }

  it('moves a booking through the same screens as booking: nearest time, days, hour, minute, then a review', async () => {
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
    const { container } = wrap(<RescheduleForm booking={BOOKING} />);

    expect(screen.getByTestId('reschedule-title')).toHaveTextContent('تغيير الموعد');
    expect(await screen.findByTestId('nearest-slot')).toHaveTextContent('أقرب موعد متاح');
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();

    await user.click(screen.getByRole('radio', { name: dayName(day) }));
    expect(await screen.findByText('وقتان متاحان')).toBeInTheDocument();
    await user.click(await screen.findByRole('radio', { name: '١٠ ص' }));
    await user.click(screen.getByRole('radio', { name: /١٠:٣٠/ }));
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    const review = await screen.findByTestId('reschedule-review');
    expect(screen.getByTestId('reschedule-title')).toHaveTextContent('مراجعة الموعد الجديد');
    expect(review).toHaveTextContent('الموعد الحالي');
    expect(review).toHaveTextContent('عمر السالم');
    await expectNoAxeViolations(container);

    await user.click(screen.getByRole('button', { name: 'تأكيد الموعد الجديد' }));
    expect(api.POST).toHaveBeenCalledWith(
      '/api/v1/me/bookings/{bookingId}/reschedule',
      expect.objectContaining({ body: { startsAt: `${day}T07:30:00Z`, professionalId: null, version: 3 } }),
    );
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/account/bookings/bk-1?rescheduled=1'));
  });

  it('a time taken meanwhile sends the customer back to the days with a notice', async () => {
    const user = userEvent.setup();
    const day = addDays(todayLocal('Asia/Riyadh'), 1);
    api.GET.mockImplementation(async (path: string) =>
      path.endsWith('/reschedule/dates')
        ? ok({
            bookable: true,
            blockedReason: null,
            timeZone: 'Asia/Riyadh',
            dates: [{ date: day, slotCount: 1 }],
          })
        : ok({
            bookable: true,
            blockedReason: null,
            timeZone: 'Asia/Riyadh',
            date: day,
            slots: [{ startsAt: `${day}T07:00:00Z`, localTime: '10:00', period: 'Morning' }],
          }),
    );
    api.POST.mockResolvedValue(fail(409, 'booking.slot_unavailable'));
    wrap(<RescheduleForm booking={BOOKING} />);

    await user.click(await screen.findByRole('button', { name: 'اختر هذا الموعد' }));
    await user.click(await screen.findByRole('button', { name: 'تأكيد الموعد الجديد' }));
    expect(await screen.findByText(/حُجز هذا الوقت للتو/)).toBeInTheDocument();
    expect(screen.getByTestId('reschedule-title')).toHaveTextContent('تغيير الموعد');
    expect(router.replace).not.toHaveBeenCalled();
  });
});

describe("a barber's own length (D-149)", () => {
  const OWN: WizardOffer = {
    ...BEARD,
    professionalDurations: [{ professionalId: 'majed', durationMinutes: 35 }],
  };

  it('shows a barber whose length differs on their card, and their length on the review', async () => {
    availability();
    window.history.replaceState(null, '', '/ar/shops/barber-house/book?service=svc-2&step=professional');
    renderWizard(<BookingWizard shop={SHOP} offers={[OWN]} professionals={PROS} viewer="customer" />);

    const majed = await screen.findByRole('radio', { name: /ماجد العتيبي/ });
    expect(majed.closest('label')).toHaveTextContent('٣٥');
    // Omar takes the standard 20 minutes: his card says nothing about the length.
    expect(screen.getByRole('radio', { name: /عمر السالم/ }).closest('label')).not.toHaveTextContent('٢٠');
    // The languages under the name (D-155, the client's 3-9); none listed, no line.
    expect(screen.getByRole('radio', { name: /عمر السالم/ }).closest('label')).toHaveTextContent(
      'يتحدث: العربية، التركية',
    );
    expect(majed.closest('label')).not.toHaveTextContent('يتحدث');
  });

  it('times the review with the chosen barber’s own length', async () => {
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-2&pro=majed&date=${D1}&time=10:00&step=review`,
    );
    renderWizard(<BookingWizard shop={SHOP} offers={[OWN]} professionals={PROS} viewer="customer" />);

    const summary = await screen.findByText('المدة');
    expect(summary.parentElement).toHaveTextContent('٣٥');
  });
});
