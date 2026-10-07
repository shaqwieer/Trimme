import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DURATION_OPTIONS, parsePrice } from '@/lib/forms/price';
import { localizedName } from '@/lib/i18n/localized';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
// After the render helper, which mocks Next's router before anything imports it.
import { AdminServiceEditor } from '@/components/admin/AdminCatalog';
import { ServiceForm } from './ServiceForm';

const api = vi.hoisted(() => ({ PUT: vi.fn(), POST: vi.fn(), DELETE: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

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
    renderWithIntl(<ServiceForm categories={[]} onSubmitValues={async () => {}} />);
    await userEvent.type(screen.getByLabelText('اسم الخدمة بالعربية'), 'حلاقة');
    await userEvent.type(screen.getByLabelText('السعر (ر.س)'), '10.005');
    await userEvent.click(screen.getByRole('button', { name: 'إنشاء الخدمة' }));
    expect(
      await screen.findByText('أدخل سعراً من 0 إلى 100000 بمنزلتين عشريتين على الأكثر'),
    ).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();
  });
});

describe('AdminServiceEditor (D-127)', () => {
  const BARBERS = [
    { id: 'faisal', name: 'فيصل' },
    { id: 'omar', name: 'عمر' },
  ];

  it('adds a service to the shop with every barber picked by default, and warns when none is', async () => {
    api.POST.mockResolvedValue({ data: { id: 'new' }, response: new Response(null, { status: 201 }) });
    const { container } = renderWithIntl(
      <AdminServiceEditor shopId="shop-1" categories={[]} professionals={BARBERS} />,
    );

    const faisal = screen.getByRole('checkbox', { name: 'فيصل' });
    const omar = screen.getByRole('checkbox', { name: 'عمر' });
    expect(faisal).toBeChecked();
    expect(omar).toBeChecked();
    await userEvent.click(faisal);
    await userEvent.click(omar);
    expect(screen.getByText(/لم تختر أي حلاق/)).toBeInTheDocument();
    await userEvent.click(omar);
    expect(screen.queryByText(/لم تختر أي حلاق/)).not.toBeInTheDocument();
    await expectNoAxeViolations(container);

    await userEvent.type(screen.getByLabelText('اسم الخدمة بالعربية'), 'صبغة');
    await userEvent.type(screen.getByLabelText('السعر (ر.س)'), '90');
    await userEvent.click(screen.getByRole('button', { name: 'إضافة الخدمة' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    const [path, init] = api.POST.mock.calls[0]!;
    expect(path).toBe('/api/v1/admin/shops/{shopId}/services');
    expect(init.params.path.shopId).toBe('shop-1');
    expect(init.body).toMatchObject({
      nameAr: 'صبغة',
      price: 90,
      durationMinutes: 30,
      professionalIds: ['omar'],
    });
  });

  it('edits a service with the barbers who do it now', async () => {
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <AdminServiceEditor
        shopId="shop-1"
        categories={[]}
        professionals={BARBERS}
        service={{
          id: 'svc-1',
          shopId: 'shop-1',
          shopNameAr: 'باربر',
          shopNameEn: 'Barber',
          nameAr: 'حلاقة',
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
          moderation: 'Visible',
          moderationReason: null,
          assignedProfessionalCount: 1,
          version: 7,
          professionalIds: ['faisal'],
        }}
      />,
    );

    expect(screen.getByRole('checkbox', { name: 'فيصل' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'عمر' })).not.toBeChecked();
    await userEvent.click(screen.getByRole('checkbox', { name: 'عمر' }));
    await userEvent.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    const [path, init] = api.PUT.mock.calls[0]!;
    expect(path).toBe('/api/v1/admin/services/{serviceId}');
    expect(init.body).toMatchObject({ version: 7, professionalIds: ['faisal', 'omar'] });
  });
});
