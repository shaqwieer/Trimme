import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '@/lib/api/schema';
import { DURATION_OPTIONS, parsePrice } from '@/lib/forms/price';
import { localizedName } from '@/lib/i18n/localized';
import { renderWithIntl } from '@/test/render';
import { ServiceForm } from './ServiceForm';
import { ShopCatalogList } from './ShopCatalogLists';

const api = vi.hoisted(() => ({ PUT: vi.fn(), POST: vi.fn(), DELETE: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

type Service = components['schemas']['ShopServiceResponse'];

const service = (id: string, nameAr: string, order: number): Service => ({
  id,
  nameAr,
  nameEn: null,
  descriptionAr: null,
  descriptionEn: null,
  categoryId: null,
  price: 60,
  currency: 'SAR',
  durationMinutes: 30,
  onlineBookable: true,
  isActive: true,
  isArchived: false,
  displayOrder: order,
  moderation: 'Visible',
  moderationReason: null,
  assignedProfessionalCount: 0,
  version: 1,
});

beforeEach(() => {
  api.PUT.mockReset();
  api.POST.mockReset();
});

describe('price and duration input (D-071)', () => {
  it.each([
    ['60', 60],
    ['60.5', 60.5],
    ['٦٠٫٥', 60.5],
    ['60,25', 60.25],
    ['0', 0],
    ['100000', 100000],
  ])('reads %s as %s', (text, value) => expect(parsePrice(text)).toBe(value));

  it.each(['', '10.005', '-5', '100000.01', 'abc', '1e3'])('refuses %s', (text) =>
    expect(parsePrice(text)).toBeNull(),
  );

  it('offers 5-minute steps up to 8 hours', () => {
    expect(DURATION_OPTIONS[0]).toBe(5);
    expect(DURATION_OPTIONS.at(-1)).toBe(480);
    expect(DURATION_OPTIONS.every((minutes) => minutes % 5 === 0)).toBe(true);
  });
});

describe('localizedName (D-070)', () => {
  it('shows English when present in English, Arabic otherwise', () => {
    expect(localizedName('en', 'حلاقة', 'Haircut')).toBe('Haircut');
    expect(localizedName('en', 'حلاقة', null)).toBe('حلاقة');
    expect(localizedName('ar', 'حلاقة', 'Haircut')).toBe('حلاقة');
  });
});

describe('ServiceForm', () => {
  it('validates the price in the active language before calling the API', async () => {
    renderWithIntl(<ServiceForm categories={[]} />);
    await userEvent.type(screen.getByLabelText('اسم الخدمة بالعربية'), 'حلاقة');
    await userEvent.type(screen.getByLabelText('السعر (ر.س)'), '10.005');
    await userEvent.click(screen.getByRole('button', { name: 'إنشاء الخدمة' }));
    expect(
      await screen.findByText('أدخل سعراً من 0 إلى 100000 بمنزلتين عشريتين على الأكثر'),
    ).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();
  });
});

describe('ShopCatalogList keyboard reorder', () => {
  it('moves an item with the buttons, saves the full order and announces the new position', async () => {
    api.PUT.mockResolvedValue({ data: [], response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <ShopCatalogList
        kind="services"
        canManage
        items={[service('a', 'حلاقة', 1), service('b', 'لحية', 2)]}
      />,
      { locale: 'ar' },
    );

    expect(screen.getByRole('button', { name: 'تحريك حلاقة للأعلى' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'تحريك لحية للأعلى' }));

    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/services/order', {
        body: { orderedIds: ['b', 'a'] },
      }),
    );
    expect(await screen.findByText('أصبحت لحية في الموضع 1')).toBeInTheDocument();
    const rows = screen.getAllByTestId(/catalog-row-/);
    expect(rows.map((row) => row.dataset.testid)).toEqual(['catalog-row-b', 'catalog-row-a']);
  });

  it('rolls the move back when the save fails', async () => {
    api.PUT.mockResolvedValue({
      error: { errorCode: 'server.unexpected' },
      response: new Response(null, { status: 500 }),
    });
    renderWithIntl(
      <ShopCatalogList
        kind="services"
        canManage
        items={[service('a', 'حلاقة', 1), service('b', 'لحية', 2)]}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'تحريك لحية للأعلى' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getAllByTestId(/catalog-row-/).map((row) => row.dataset.testid)).toEqual([
        'catalog-row-a',
        'catalog-row-b',
      ]),
    );
  });
});
