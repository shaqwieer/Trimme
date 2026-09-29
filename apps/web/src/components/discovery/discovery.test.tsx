import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ShopTabs } from '@/components/shops/public/ShopTabs';
import type { ShopSearchItem } from '@/lib/api/public-types';
import { LOCATION_COOKIE, parseLocation } from '@/lib/discovery/location';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { FilterSheet } from './FilterSheet';
import { LocationChooser } from './LocationChooser';
import { ShopResultCard } from './ShopCards';

const api = vi.hoisted(() => ({ GET: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

const shop = (overrides: Partial<ShopSearchItem> = {}): ShopSearchItem => ({
  id: '0199a0de-5a10-7000-8000-000000000001',
  slug: 'al-asala',
  nameAr: 'صالون الأصالة للحلاقة',
  nameEn: 'Al Asala Barbershop',
  category: 'Barbershop',
  isVerified: true,
  district: 'الملقا',
  city: 'الرياض',
  latitude: 24.77,
  longitude: 46.63,
  distanceKm: 2.4,
  rating: 4.8,
  reviewCount: 12,
  minPrice: 35,
  pinPrice: 35,
  currency: 'SAR',
  matchedOffer: {
    id: 'x',
    isPackage: false,
    nameAr: 'تهذيب لحية',
    nameEn: 'Beard trim',
    price: 35,
    currency: 'SAR',
    durationMinutes: 20,
  },
  isOpenNow: true,
  closesAt: '2026-10-04T20:00:00Z',
  nextOpensAt: null,
  acceptsOnlineBookings: true,
  earliestSlotAt: '2026-10-04T13:00:00Z',
  timeZone: 'Asia/Riyadh',
  coverUrl: null,
  logoUrl: null,
  ...overrides,
});

beforeEach(() => {
  api.GET.mockReset();
  document.cookie = `${LOCATION_COOKIE}=; Path=/; Max-Age=0`;
});

describe('ShopResultCard (c-home search results)', () => {
  it('shows the matched service with its price and duration, the distance and the shop link, in Arabic', async () => {
    const { container } = renderWithIntl(<ShopResultCard shop={shop()} />);
    expect(screen.getByRole('link', { name: 'صالون الأصالة للحلاقة' })).toHaveAttribute(
      'href',
      '/ar/shops/al-asala',
    );
    expect(screen.getByText(/تهذيب لحية · 35 ر.س · ٢٠ دقيقة/)).toBeInTheDocument();
    expect(screen.getByText('2.4 كم')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'محل موثّق' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('uses the English name in English and says when online booking is paused', () => {
    renderWithIntl(<ShopResultCard shop={shop({ acceptsOnlineBookings: false, earliestSlotAt: null })} />, {
      locale: 'en',
    });
    expect(screen.getByRole('link', { name: 'Al Asala Barbershop' })).toHaveAttribute(
      'href',
      '/en/shops/al-asala',
    );
    expect(screen.getByText('Online booking paused')).toBeInTheDocument();
  });
});

describe('ShopTabs (every panel in the HTML, one visible)', () => {
  it('renders all panels, shows the initial one, and switches with the keyboard', async () => {
    const user = userEvent.setup();
    renderWithIntl(
      <ShopTabs
        label="أقسام المحل"
        initial="reviews"
        tabs={[
          { value: 'services', label: 'الخدمات', content: <p>services panel</p> },
          { value: 'reviews', label: 'التقييمات', content: <p>reviews panel</p> },
        ]}
      />,
    );
    expect(screen.getByText('services panel')).not.toBeVisible();
    expect(screen.getByText('reviews panel')).toBeVisible();
    await user.click(screen.getByRole('tab', { name: 'الخدمات' }));
    expect(screen.getByText('services panel')).toBeVisible();
    expect(window.location.search).toContain('tab=services');
  });
});

describe('LocationChooser (DV-A21)', () => {
  it('filters districts with Arabic spelling variants and keeps the choice in a cookie on this device', async () => {
    const user = userEvent.setup();
    const done = vi.fn();
    const { container } = renderWithIntl(
      <LocationChooser
        startManual
        onDone={done}
        areas={[
          { city: 'الرياض', district: 'الملقا', latitude: 24.8123, longitude: 46.6012, shopCount: 2 },
          { city: 'الرياض', district: 'حطين', latitude: 24.76, longitude: 46.64, shopCount: 1 },
        ]}
      />,
    );
    await user.type(screen.getByRole('searchbox', { name: 'ابحث عن الحي' }), 'ملقا');
    expect(screen.queryByText(/حطين/)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /الملقا، الرياض/ }));
    expect(done).toHaveBeenCalled();
    const raw = document.cookie
      .split('; ')
      .find((c) => c.startsWith(`${LOCATION_COOKIE}=`))
      ?.split('=')[1];
    expect(parseLocation(raw)).toEqual({ lat: 24.812, lng: 46.601, label: 'الملقا', source: 'area' });
    await expectNoAxeViolations(container);
  });

  it('asks the browser only when the customer presses allow, and falls back to the list when refused', async () => {
    const user = userEvent.setup();
    const getCurrentPosition = vi.fn((_: PositionCallback, fail: PositionErrorCallback) =>
      fail({ code: 1 } as GeolocationPositionError),
    );
    Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition }, configurable: true });
    renderWithIntl(
      <LocationChooser
        areas={[{ city: 'الرياض', district: 'الملقا', latitude: 24.8, longitude: 46.6, shopCount: 1 }]}
      />,
    );
    expect(getCurrentPosition).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'السماح بالوصول للموقع' }));
    expect(getCurrentPosition).toHaveBeenCalledOnce();
    expect(await screen.findByText('لم نتمكن من الوصول لموقعك. اختر الحي يدوياً.')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'الأحياء المتاحة' })).toBeInTheDocument();
  });
});

describe('FilterSheet (c-map filter drawer)', () => {
  it('shows the live result count in the apply button and counts the active filters on the chip', async () => {
    const user = userEvent.setup();
    api.GET.mockResolvedValue({ data: { total: 7 }, response: new Response() });
    renderWithIntl(
      <FilterSheet
        filters={{
          q: '',
          category: null,
          sort: 'nearest',
          openNow: true,
          verified: false,
          today: false,
          minPrice: null,
          maxPrice: null,
          radiusKm: null,
          view: 'list',
          page: 1,
        }}
        categories={[{ id: 'c1', name: 'اللحية' }]}
        priceRange={{ min: 25, max: 90 }}
        location={{ lat: 24.77, lng: 46.64, label: '', source: 'device' }}
        activeCount={1}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'فلاتر · 1' }));
    expect(screen.getByRole('dialog', { name: 'التصفية والترتيب' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'الأقرب مسافةً' })).toBeChecked();
    await user.click(screen.getByRole('switch', { name: 'محلات موثّقة فقط' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'عرض 7 نتائج' })).toBeInTheDocument());
    const query = api.GET.mock.calls.at(-1)?.[1]?.params?.query;
    expect(query).toMatchObject({
      lat: 24.77,
      lng: 46.64,
      openNow: true,
      verified: true,
      pageSize: 1,
      sort: 'Distance',
    });
  });
});
