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
import { CancelBookingButton } from './BookingDetailClient';
import { BookingPolicy, CreatedBanner } from './BookingDetailParts';
import { HourMinutePicker } from '@/components/ui/booking';
import { BookingWizard, type WizardPro } from './BookingWizard';
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
    expect(review).toHaveTextContent('85 ر.س');
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
    });
  }, 15_000);

  it('sends a time taken meanwhile back to fresh slots with the "just taken" notice, and keeps the key on a plain retry', async () => {
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

    await waitFor(() => expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر اليوم والوقت'));
    expect(screen.getByText('حُجز هذا الوقت للتو')).toBeInTheDocument();
    expect(window.location.search).toContain('notice=conflict');
    expect(window.location.search).not.toContain('time=');
    const keys = api.POST.mock.calls.map(([, init]) => init.params.header['Idempotency-Key']);
    expect(keys[0]).toBe(keys[1]);
    // The slots are asked for again.
    expect(api.GET.mock.calls.filter(([path]) => String(path).endsWith('/slots')).length).toBeGreaterThan(1);
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
    expect(screen.getByText('125 ر.س')).toBeInTheDocument();
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
    expect(review).toHaveTextContent('125 ر.س');
    const datesCall = api.GET.mock.calls.find(([path]) => String(path).endsWith('/dates'))!;
    expect(datesCall[1].params.query).toMatchObject({ serviceIds: ['svc-1', 'svc-2'], serviceId: undefined });
    await user.click(screen.getByRole('button', { name: 'تأكيد الحجز' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({
      serviceId: null,
      serviceIds: ['svc-1', 'svc-2'],
    });
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
    expect(screen.getByText(/لا يوجد مختص واحد يقدّم كل هذه الخدمات/)).toBeInTheDocument();
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
    await user.click(screen.getByRole('radio', { name: '4 من 5' }));
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
    await user.click(screen.getByRole('radio', { name: '5 من 5' }));
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
  it('says "confirmed" only for a confirmed booking, and "request sent" for one the shop must confirm', () => {
    const { unmount } = renderWithIntl(<CreatedBanner status="Confirmed" />);
    expect(screen.getByText('تم تأكيد حجزك')).toBeInTheDocument();
    unmount();
    renderWithIntl(<CreatedBanner status="Pending" />);
    expect(screen.getByText('تم إرسال طلب الحجز')).toBeInTheDocument();
    expect(screen.queryByText('تم تأكيد حجزك')).not.toBeInTheDocument();
  });

  it('before the cutoff says until when; after it says the window closed, with no salon phone (D-129)', () => {
    const { unmount } = renderWithIntl(
      <BookingPolicy startsAt="2026-10-04T14:00:00Z" cutoffMinutes={120} canChange />,
    );
    expect(screen.getByText(/مجاني عبر تريمي حتى/)).toHaveTextContent('٣:٠٠');
    expect(screen.queryByTestId('cutoff-passed')).not.toBeInTheDocument();
    unmount();

    renderWithIntl(<BookingPolicy startsAt="2026-10-04T14:00:00Z" cutoffMinutes={120} canChange={false} />);
    expect(screen.getByTestId('cutoff-passed')).toHaveTextContent('ساعتين قبل الموعد');
    expect(screen.queryByRole('link', { name: /اتصل/ })).not.toBeInTheDocument();
    expect(document.querySelector('a[href^="tel:"]')).toBeNull();
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

    expect(screen.getByRole('radio', { name: /١٠ ص/ })).toHaveAccessibleName(/وقتان/);
    expect(screen.queryByRole('radio', { name: /١٠:٣٠/ })).not.toBeInTheDocument();
    expect(screen.getByText('اختر ساعة لعرض الدقائق المتاحة فيها.')).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /١١ ص/ }));
    expect(screen.queryByRole('radio', { name: /١٠:٣٠/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /١١:١٥/ }));
    expect(onValueChange).toHaveBeenCalledWith('2026-10-01T08:15:00Z');
    await expectNoAxeViolations(container);
  });
});
