import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement, ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FavoriteButton } from '@/components/favorites/FavoriteButton';
import type { WizardOffer } from '@/lib/booking/wizard';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { CancelBookingButton } from './BookingDetailClient';
import { BookingPolicy, CreatedBanner } from './BookingDetailParts';
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

const DATES = {
  from: '2026-10-01',
  to: '2026-10-02',
  timeZone: 'Asia/Riyadh',
  bookable: true,
  blockedReason: null,
  dates: [
    { date: '2026-10-01', slotCount: 2 },
    { date: '2026-10-02', slotCount: 0 },
  ],
};
const slotsOf = (times: Array<[string, string]>) => ({
  date: '2026-10-01',
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
  it('walks service → any professional → date → time → review, with the price paid at the shop and no payment step', async () => {
    const user = userEvent.setup();
    availability();
    api.POST.mockResolvedValue(ok({ id: 'booking-1', status: 'Confirmed' }, 201));
    const { container } = renderWizard(
      <BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />,
    );

    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الخدمة');
    expect(screen.getByRole('button', { name: 'التالي' })).toBeDisabled();
    await user.click(screen.getByRole('radio', { name: /قص وتصفيف/ }));
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    // Only the professionals assigned to the service, "any" preselected (design rule 2).
    expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الحلاق');
    expect(screen.getByRole('radio', { name: /أي حلاق متاح/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /عمر السالم/ })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /ماجد/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    // Dates come from the API; a day without slots cannot be picked.
    expect(window.location.search).toContain('step=date');
    const day = await screen.findByRole('radio', { name: /1 أكتوبر|١ أكتوبر/ });
    expect(screen.getByRole('radio', { name: /2 أكتوبر|٢ أكتوبر/ })).toBeDisabled();
    await user.click(day);
    expect(screen.getByRole('status')).toHaveTextContent('وقتان متاحان');
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    await user.click(await screen.findByRole('radio', { name: /١٠:٣٠/ }));
    expect(window.location.search).toContain('time=10%3A30');
    await user.click(screen.getByRole('button', { name: 'التالي' }));

    const review = await screen.findByTestId('booking-review');
    expect(review).toHaveTextContent('الإجمالي (يُدفع في المحل)');
    expect(review).toHaveTextContent('85 ر.س');
    expect(review).toHaveTextContent('أي حلاق متاح — نحدده عند التأكيد');
    expect(review).toHaveTextContent('ساعتين');
    // R-NEG-02: nothing asks for a card or a payment.
    expect(container.querySelector('input[autocomplete^="cc-"]')).toBeNull();
    expect(screen.queryByText(/بطاقة|الدفع الآن|ادفع/)).not.toBeInTheDocument();
    await expectNoAxeViolations(container);

    await user.type(screen.getByLabelText(/ملاحظة للحلاق/), 'تدريج');
    await user.click(screen.getByRole('button', { name: 'تأكيد الحجز' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/account/bookings/booking-1?created=1'));
    const [, init] = api.POST.mock.calls[0]!;
    expect(init.params.header['Idempotency-Key']).toMatch(/[0-9a-f-]{32,36}/);
    expect(init.body).toEqual({
      shopSlug: 'barber-house',
      serviceId: 'svc-1',
      packageId: null,
      professionalId: null,
      startsAt: '2026-10-01T07:30:00Z',
      note: 'تدريج',
    });
  });

  it('sends a time taken meanwhile back to fresh slots with the "just taken" notice, and keeps the key on a plain retry', async () => {
    const user = userEvent.setup();
    availability();
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=2026-10-01&time=10:00`,
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

    await waitFor(() => expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الوقت'));
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
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&date=2026-10-01&time=10:00`,
    );
    renderWizard(<BookingWizard shop={SHOP} offers={[SERVICE]} professionals={PROS} viewer="customer" />);
    await waitFor(() => expect(screen.getByTestId('wizard-title')).toHaveTextContent('اختر الوقت'));
    expect(screen.getByText('الوقت الذي اخترته لم يعد متاحاً')).toBeInTheDocument();
  });

  it('sends a guest to sign in with the review step as returnTo (after trying a silent refresh)', async () => {
    const user = userEvent.setup();
    availability();
    session.refresh.mockResolvedValue(false);
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&pro=omar&date=2026-10-01&time=10:00`,
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
      '/shops/barber-house/book?service=svc-1&pro=omar&date=2026-10-01&time=10%3A00&step=review',
    );
    expect(api.POST).not.toHaveBeenCalled();
  });

  it('sends a customer without a name to complete the profile and back', async () => {
    const user = userEvent.setup();
    availability();
    api.POST.mockResolvedValue(fail(422, 'booking.profile_incomplete'));
    window.history.replaceState(
      null,
      '',
      `/ar/shops/barber-house/book?service=svc-1&date=2026-10-01&time=10:00`,
    );
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

  it('before the cutoff says until when; after it says the window closed and gives the shop phone', () => {
    const { unmount } = renderWithIntl(
      <BookingPolicy
        startsAt="2026-10-04T14:00:00Z"
        cutoffMinutes={120}
        canChange
        shopPhone="+966512345678"
      />,
    );
    expect(screen.getByText(/مجاني عبر تريمي حتى/)).toHaveTextContent('٣:٠٠');
    expect(screen.queryByTestId('cutoff-passed')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /اتصل بالمحل/ })).not.toBeInTheDocument();
    unmount();

    renderWithIntl(
      <BookingPolicy
        startsAt="2026-10-04T14:00:00Z"
        cutoffMinutes={120}
        canChange={false}
        shopPhone="+966512345678"
      />,
    );
    expect(screen.getByTestId('cutoff-passed')).toHaveTextContent('ساعتين قبل الموعد');
    expect(screen.getByRole('link', { name: /اتصل بالمحل/ })).toHaveAttribute('href', 'tel:+966512345678');
  });
});

describe('FavoriteButton after signing in (client-side return)', () => {
  it('asks again on the next mount instead of keeping the signed-out answer', async () => {
    const now = vi.spyOn(Date, 'now');
    now.mockReturnValue(1_000_000);
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    const first = renderWithIntl(<FavoriteButton target={{ kind: 'shop', id: 's1' }} name="باربر هاوس" />);
    expect(await screen.findByRole('link', { name: /سجّل الدخول لحفظ/ })).toBeInTheDocument();
    first.unmount();

    // Signed in meanwhile; the page comes back without a reload.
    now.mockReturnValue(1_010_000);
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
    now.mockRestore();
  });
});
