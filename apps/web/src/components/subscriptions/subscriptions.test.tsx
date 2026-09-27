import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '@/lib/api/schema';
import { renderWithIntl } from '@/test/render';
import { PlatformSettingsForm } from '../admin/PlatformSettingsForm';
import { periodEnd, priceOn } from './periods';
import { PlanForm } from './PlanForm';
import { RecordPeriodForm, SuspensionControl } from './ShopSubscriptionPanel';

const api = vi.hoisted(() => ({ PUT: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

type Plan = components['schemas']['PlanResponse'];
type Subscription = components['schemas']['AdminShopSubscriptionResponse'];

const price = (id: string, versionNumber: number, amount: number, effectiveFrom: string) => ({
  id,
  versionNumber,
  amount,
  currency: 'SAR',
  effectiveFrom,
  createdAt: '2026-01-01T00:00:00Z',
});

const plan = (overrides: Partial<Plan> = {}): Plan => ({
  id: 'plan-1',
  nameAr: 'باقة تجريبية',
  nameEn: 'Test plan',
  descriptionAr: null,
  descriptionEn: null,
  features: [],
  maxProfessionals: null,
  maxServices: null,
  intervalUnit: 'Month',
  intervalCount: 4,
  trialDays: null,
  graceDays: null,
  availableToNewShops: true,
  status: 'Published',
  displayOrder: 1,
  currentPrice: null,
  upcomingPrice: null,
  prices: [price('p1', 1, 111, '2026-01-01'), price('p2', 2, 222, '2026-11-01')],
  subscriptionCount: 0,
  version: 7,
  ...overrides,
});

const none: Subscription = {
  shopId: 'shop-1',
  shopNameAr: 'محل',
  shopNameEn: 'Shop',
  exists: false,
  status: 'None',
  planId: null,
  planNameAr: null,
  planNameEn: null,
  startDate: null,
  endDate: null,
  daysRemaining: 0,
  currentAmount: null,
  currency: null,
  isSuspended: false,
  suspensionReason: null,
  today: '2026-10-15',
  nextStart: '2026-10-15',
  expiringSoonThresholdDays: 14,
  periods: [],
  overrides: [],
  version: null,
};

beforeEach(() => {
  api.PUT.mockReset();
  api.POST.mockReset();
});

describe('period preview (mirrors SubscriptionDates.End)', () => {
  it.each([
    ['2026-10-01', 'Month', 12, '2027-09-30'],
    ['2026-01-31', 'Month', 1, '2026-02-28'],
    ['2026-03-31', 'Month', 1, '2026-04-30'],
    ['2028-02-29', 'Month', 12, '2029-02-28'],
    ['2026-12-31', 'Month', 1, '2027-01-30'],
    ['2026-09-27', 'Day', 30, '2026-10-26'],
  ] as const)('%s + %s×%s ends %s', (start, unit, count, end) =>
    expect(periodEnd(start, unit, count)).toBe(end),
  );

  it('uses the price version in force on the start date', () => {
    const prices = plan().prices;
    expect(priceOn(prices, '2025-12-31')).toBeUndefined();
    expect(priceOn(prices, '2026-10-31')?.amount).toBe(111);
    expect(priceOn(prices, '2026-11-01')?.amount).toBe(222);
  });
});

describe('RecordPeriodForm (DV-S05: durations and prices come from the plan)', () => {
  it('previews the end and the recorded price from plan data, then records the activation', async () => {
    api.POST.mockResolvedValue({
      data: { ...none, exists: true, endDate: '2027-02-14' },
      response: new Response(null, { status: 200 }),
    });
    renderWithIntl(<RecordPeriodForm subscription={none} plans={[plan()]} />, { locale: 'en' });

    expect(screen.getByText('Plan period (4 months)')).toBeInTheDocument();
    expect(screen.getByText('Ends on 14 February 2027')).toBeInTheDocument();
    expect(screen.getByText('Recorded price: SAR 111 (version 1)')).toBeInTheDocument();
    expect(screen.queryByText(/3 months|6 months|2,400/)).toBeNull();

    await userEvent.clear(screen.getByLabelText('Start date'));
    await userEvent.type(screen.getByLabelText('Start date'), '2026-11-05');
    expect(await screen.findByText('Recorded price: SAR 222 (version 2)')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Custom number of days' }));
    await userEvent.clear(screen.getByLabelText('Number of days'));
    await userEvent.type(screen.getByLabelText('Number of days'), '30');
    expect(screen.getByText('Ends on 4 December 2026')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Record activation' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST.mock.calls[0]?.[0]).toBe('/api/v1/admin/shops/{shopId}/subscription/assign');
    expect(api.POST.mock.calls[0]?.[1].body).toEqual({
      planId: 'plan-1',
      startDate: '2026-11-05',
      durationDays: 30,
      notes: null,
    });
  });

  it('offers only published plans open to new shops for an activation, and renews with the version', async () => {
    const plans = [
      plan(),
      plan({ id: 'closed', nameEn: 'Closed', availableToNewShops: false }),
      plan({ id: 'old', nameEn: 'Old', status: 'Inactive' }),
      plan({ id: 'draft', nameEn: 'Draft', status: 'Draft' }),
    ];
    const { unmount } = renderWithIntl(<RecordPeriodForm subscription={none} plans={plans} />, {
      locale: 'en',
    });
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Test plan']);
    unmount();

    api.POST.mockResolvedValue({
      data: { ...none, endDate: '2027-01-01' },
      response: new Response(null, { status: 200 }),
    });
    const existing = {
      ...none,
      exists: true,
      status: 'Active' as const,
      planId: 'old',
      nextStart: '2026-11-01',
      version: 42,
    };
    renderWithIntl(<RecordPeriodForm subscription={existing} plans={plans} />, { locale: 'en' });
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Test plan', 'Closed', 'Old']);
    expect((screen.getByLabelText('Plan') as HTMLSelectElement).value).toBe('old');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm renewal' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST.mock.calls[0]?.[0]).toBe('/api/v1/admin/shops/{shopId}/subscription/renew');
    expect(api.POST.mock.calls[0]?.[1].body).toMatchObject({
      planId: 'old',
      startDate: '2026-11-01',
      version: 42,
    });
  });

  it('shows the API field error in the active language', async () => {
    api.POST.mockResolvedValue({
      error: {
        status: 400,
        errorCode: 'validation.failed',
        errors: { startDate: ['validation.period_gap'] },
      },
      response: new Response(null, { status: 400 }),
    });
    const existing = { ...none, exists: true, status: 'Expired' as const, planId: 'plan-1', version: 1 };
    renderWithIntl(<RecordPeriodForm subscription={existing} plans={[plan()]} />);
    await userEvent.click(screen.getByRole('button', { name: 'تأكيد التجديد' }));
    expect(
      await screen.findByText('ابدأ في اليوم التالي لنهاية المدة الحالية، أو في موعد لا يتجاوز اليوم'),
    ).toBeInTheDocument();
  });
});

describe('PlanForm', () => {
  it('validates names and the billing length before calling the API', async () => {
    renderWithIntl(<PlanForm />, { locale: 'en' });
    await userEvent.clear(screen.getByLabelText('Billing length'));
    await userEvent.type(screen.getByLabelText('Billing length'), '40');
    await userEvent.click(screen.getByRole('button', { name: 'Create plan' }));
    expect(await screen.findAllByText('This field is required')).toHaveLength(2);
    expect(screen.getByText('This value is out of range')).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();
  });

  it('creates a plan with bilingual features and an optional first price', async () => {
    api.POST.mockResolvedValue({ data: plan(), response: new Response(null, { status: 201 }) });
    renderWithIntl(<PlanForm />, { locale: 'en' });
    await userEvent.type(screen.getByLabelText('Plan name in Arabic'), 'ربع سنوي');
    await userEvent.type(screen.getByLabelText('Plan name in English'), 'Quarterly');
    await userEvent.clear(screen.getByLabelText('Billing length'));
    await userEvent.type(screen.getByLabelText('Billing length'), '3');
    await userEvent.click(screen.getByRole('button', { name: 'Add feature' }));
    await userEvent.type(screen.getByLabelText('Feature 1 in Arabic'), 'ظهور');
    await userEvent.type(screen.getByLabelText('Feature 1 in English'), 'Listed');
    await userEvent.type(screen.getByLabelText(/^Price \(SAR\)/), '٥٤٩٫٥');
    await userEvent.click(screen.getByRole('button', { name: 'Create plan' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST.mock.calls[0]?.[1].body).toMatchObject({
      nameAr: 'ربع سنوي',
      nameEn: 'Quarterly',
      intervalUnit: 'Month',
      intervalCount: 3,
      features: [{ ar: 'ظهور', en: 'Listed' }],
      initialPrice: 549.5,
      maxProfessionals: null,
    });
  });
});

describe('PlatformSettingsForm', () => {
  const settings: components['schemas']['PlatformSettingsResponse'] = {
    minLeadTimeMinutes: 60,
    bookingHorizonDays: 30,
    slotStepMinutes: 5,
    cancellationCutoffMinutes: 120,
    reviewWindowDays: 7,
    reminderOffsetMinutes: 30,
    expiringSoonThresholdDays: 14,
    expiredSubscriptionEnforcement: 'HideAndBlockNewOnlineBookings',
    hidePausedShopsFromDiscovery: true,
    defaultLocale: 'ar',
    currency: 'SAR',
    timeZone: 'Asia/Riyadh',
    countryCode: 'SA',
    mapDefaultLatitude: 24.7136,
    mapDefaultLongitude: 46.6753,
    mapDefaultZoom: 11,
    updatedAt: '2026-09-27T10:00:00Z',
    version: 5,
  };

  it('is read-only without Admin.Settings.Edit', () => {
    renderWithIntl(<PlatformSettingsForm settings={settings} canEdit={false} />, { locale: 'en' });
    expect(screen.getByText('You can view these settings but not change them.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save settings' })).toBeNull();
    expect(screen.getByLabelText('Expiring-soon threshold (days)')).toHaveAttribute('readonly');
  });

  it('saves the typed values with the version', async () => {
    api.PUT.mockResolvedValue({ data: settings, response: new Response(null, { status: 200 }) });
    renderWithIntl(<PlatformSettingsForm settings={settings} canEdit />, { locale: 'en' });
    await userEvent.clear(screen.getByLabelText('Expiring-soon threshold (days)'));
    await userEvent.type(screen.getByLabelText('Expiring-soon threshold (days)'), '21');
    await userEvent.selectOptions(screen.getByLabelText(/When a subscription is not in force/), 'None');
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(1));
    expect(api.PUT.mock.calls[0]?.[1].body).toMatchObject({
      expiringSoonThresholdDays: 21,
      expiredSubscriptionEnforcement: 'None',
      slotStepMinutes: 5,
      version: 5,
    });
    expect(await screen.findByText('Settings saved.')).toBeInTheDocument();
  });
});

describe('SuspensionControl', () => {
  it('shows a stale-version conflict inside the open dialog, where the admin is looking', async () => {
    api.POST.mockResolvedValue({
      error: { status: 409, errorCode: 'resource.concurrency_conflict' },
      response: new Response(null, { status: 409 }),
    });
    const active = { ...none, exists: true, status: 'Active' as const, version: 3 };
    renderWithIntl(<SuspensionControl subscription={active} />, { locale: 'en' });
    await userEvent.click(screen.getByRole('button', { name: 'Suspend subscription' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Contract under review');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Suspend subscription' }));
    expect(
      await within(dialog).findByText(/changed by someone else|saved at the same time|Reload/i),
    ).toBeInTheDocument();
    expect(api.POST.mock.calls[0]?.[1].body).toEqual({ reason: 'Contract under review', version: 3 });
  });
});
