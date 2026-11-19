import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '@/lib/api/schema';
import { DURATION_OPTIONS, parsePrice } from '@/lib/forms/price';
import { localizedName } from '@/lib/i18n/localized';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
// After the render helper, which mocks Next's router before anything imports it.
import { AdminServiceEditor } from '@/components/admin/AdminCatalog';
import { EditProfessionalForm } from '@/components/admin/ProfessionalForms';
import { SaveGroup } from '@/components/ui/SaveGroup';
import { ServiceForm } from './ServiceForm';
import { ShopServiceEditor, ShopServiceList } from './ShopServices';
import { ProfessionalServicesForm } from './ProfessionalServicesForm';
import { ServiceSections } from './ServiceSections';

const api = vi.hoisted(() => ({ PUT: vi.fn(), POST: vi.fn(), DELETE: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

type Service = components['schemas']['ShopServiceResponse'];

const service = (id: string, nameAr: string, order: number, professionalIds: string[] = []): Service => ({
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
  assignedProfessionalCount: professionalIds.length,
  version: 3,
  professionalIds,
});

beforeEach(() => {
  api.PUT.mockReset();
  api.POST.mockReset();
  api.DELETE.mockReset();
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
    renderWithIntl(<ServiceForm sections={[]} onSubmitValues={async () => {}} />);
    await userEvent.type(screen.getByLabelText('اسم الخدمة بالعربية'), 'حلاقة');
    await userEvent.type(screen.getByLabelText('السعر (⃁)'), '10.005');
    await userEvent.click(screen.getByRole('button', { name: 'إنشاء الخدمة' }));
    expect(
      await screen.findByText('أدخل سعراً من ٠ إلى ١٠٠٬٠٠٠ بمنزلتين عشريتين على الأكثر'),
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
      <AdminServiceEditor shopId="shop-1" sections={[]} professionals={BARBERS} />,
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
    await userEvent.type(screen.getByLabelText('السعر (⃁)'), '90');
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
        sections={[]}
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

describe('the owner’s services (D-137)', () => {
  const OWNER_BARBERS = [
    { id: 'faisal', name: 'فيصل' },
    { id: 'omar', name: 'عمر' },
  ];

  it('creates a service with the shop’s barbers, every active one picked at first, and opens it', async () => {
    api.POST.mockResolvedValue({ data: { id: 'new-1' }, response: new Response(null, { status: 201 }) });
    renderWithIntl(
      <main>
        <ShopServiceEditor sections={[]} professionals={OWNER_BARBERS} />
      </main>,
    );

    expect(screen.getByRole('checkbox', { name: 'فيصل' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'عمر' })).toBeChecked();
    await userEvent.type(screen.getByLabelText('اسم الخدمة بالعربية'), 'حلاقة أطفال');
    await userEvent.type(screen.getByLabelText('السعر (⃁)'), '٤٠');
    await userEvent.click(screen.getByRole('checkbox', { name: 'عمر' }));
    await expectNoAxeViolations();
    await userEvent.click(screen.getByRole('button', { name: 'إنشاء الخدمة' }));

    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    const [path, init] = api.POST.mock.calls[0]!;
    expect(path).toBe('/api/v1/shop/services');
    expect(init.body).toMatchObject({ nameAr: 'حلاقة أطفال', price: 40, professionalIds: ['faisal'] });
  });

  it('edits through the shop’s own endpoint with the barbers who do it now', async () => {
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <ShopServiceEditor
        sections={[]}
        professionals={OWNER_BARBERS}
        service={service('svc-1', 'حلاقة', 1, ['omar'])}
      />,
    );

    expect(screen.getByRole('checkbox', { name: 'فيصل' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'عمر' })).toBeChecked();
    await userEvent.click(screen.getByRole('checkbox', { name: 'فيصل' }));
    await userEvent.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    const [path, init] = api.PUT.mock.calls[0]!;
    expect(path).toBe('/api/v1/shop/services/{serviceId}');
    expect(init.params).toEqual({ path: { serviceId: 'svc-1' } });
    expect(init.body).toMatchObject({ version: 3, professionalIds: ['omar', 'faisal'] });
  });

  it('says the platform team adds barbers when the shop has none', () => {
    renderWithIntl(<ShopServiceEditor sections={[]} professionals={[]} />);
    expect(screen.getByText(/يضيفهم فريق Groom Here/)).toBeInTheDocument();
  });

  it('moves a service with the buttons, saves the full order and announces the new position', async () => {
    api.PUT.mockResolvedValue({ data: [], response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <ShopServiceList items={[service('a', 'حلاقة', 1, ['faisal']), service('b', 'لحية', 2)]} />,
    );

    expect(screen.getByTestId('catalog-row-a')).toHaveTextContent('حلاق واحد مسند');
    expect(screen.getByRole('button', { name: 'تحريك حلاقة للأعلى' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'تحريك لحية للأعلى' }));

    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/services/order', {
        body: { orderedIds: ['b', 'a'] },
      }),
    );
    expect(await screen.findByText('أصبحت لحية في الموضع ١')).toBeInTheDocument();
    const rows = screen.getAllByTestId(/catalog-row-/);
    expect(rows.map((row) => row.dataset.testid)).toEqual(['catalog-row-b', 'catalog-row-a']);
  });

  it('says the state on the badge and the action on the button: «متوقفة» with «تفعيل الحجز», «متاحة للحجز» with «إيقاف الحجز» (D-149)', async () => {
    api.POST.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <ShopServiceList
        items={[service('a', 'حلاقة', 1, ['faisal']), { ...service('b', 'لحية', 2), isActive: false }]}
      />,
    );
    const on = screen.getByTestId('catalog-row-a');
    expect(on).toHaveTextContent('متاحة للحجز');
    expect(within(on).getByRole('button', { name: 'إيقاف الحجز: حلاقة' })).toHaveTextContent('إيقاف الحجز');
    const off = screen.getByTestId('catalog-row-b');
    expect(off).toHaveTextContent('متوقفة');
    expect(off).not.toHaveTextContent('متاحة للحجز');
    await userEvent.click(within(off).getByRole('button', { name: 'تفعيل الحجز: لحية' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalled());
    expect(String(api.POST.mock.calls[0]![0])).toMatch(/activate$/);
    expect(String(api.POST.mock.calls[0]![0])).not.toMatch(/deactivate$/);
  });

  it('groups the tiles by the shop’s sections and saves a tile’s price and length with its version (D-151, 3-2)', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <ShopServiceList
        items={[
          { ...service('a', 'حلاقة', 1, ['faisal']), sectionId: 'hair' },
          { ...service('b', 'تحديد لحية', 2), sectionId: 'beard' },
          service('c', 'تنظيف بشرة', 3),
        ]}
        sections={[
          { id: 'hair', nameAr: 'الشعر', nameEn: 'Hair' },
          { id: 'beard', nameAr: 'اللحية', nameEn: null },
        ]}
      />,
    );

    expect(within(screen.getByTestId('service-group-hair')).getByRole('heading')).toHaveTextContent('الشعر');
    expect(
      within(screen.getByTestId('service-group-beard')).getByTestId('catalog-row-b'),
    ).toBeInTheDocument();
    expect(within(screen.getByTestId('service-group-other')).getByRole('heading')).toHaveTextContent('أخرى');

    const tile = screen.getByTestId('catalog-row-a');
    expect(within(tile).queryByRole('button', { name: 'حفظ سعر ومدة حلاقة' })).not.toBeInTheDocument();
    const price = within(tile).getByLabelText('السعر (⃁)');
    await user.clear(price);
    await user.type(price, '٧٥');
    await user.click(within(tile).getByRole('button', { name: 'زيادة المدة' }));
    expect(within(tile).getByRole('group', { name: 'المدة' })).toHaveTextContent('٣٥');
    await user.click(within(tile).getByRole('button', { name: 'حفظ سعر ومدة حلاقة' }));

    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    const [path, init] = api.PUT.mock.calls[0]!;
    expect(path).toBe('/api/v1/shop/services/{serviceId}');
    expect(init.body).toMatchObject({
      nameAr: 'حلاقة',
      price: 75,
      durationMinutes: 35,
      version: 3,
      sectionId: 'hair',
      professionalIds: ['faisal'],
    });
  });

  it('rolls the move back when the save fails', async () => {
    api.PUT.mockResolvedValue({
      error: { errorCode: 'server.unexpected' },
      response: new Response(null, { status: 500 }),
    });
    renderWithIntl(<ShopServiceList items={[service('a', 'حلاقة', 1), service('b', 'لحية', 2)]} />);

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

describe('the shop’s own sections (D-149, the owner’s item 1)', () => {
  const SECTIONS = [
    { id: 'hair', nameAr: 'الشعر', nameEn: 'Hair', displayOrder: 1, serviceCount: 3 },
    { id: 'beard', nameAr: 'اللحية', nameEn: null, displayOrder: 2, serviceCount: 0 },
  ];

  it('adds, moves and deletes a section, the services staying under «أخرى»', async () => {
    const user = userEvent.setup();
    const ok = { data: {}, response: new Response(null, { status: 200 }) };
    api.POST.mockResolvedValue(ok);
    api.PUT.mockResolvedValue(ok);
    api.DELETE.mockResolvedValue({ data: undefined, response: new Response(null, { status: 204 }) });
    renderWithIntl(
      <main>
        <ServiceSections scope={{ kind: 'shop' }} sections={SECTIONS} />
      </main>,
    );

    expect(screen.getByTestId('section-row-hair')).toHaveTextContent('٣ خدمات');
    expect(screen.getByTestId('section-row-beard')).toHaveTextContent('لا توجد خدمات');
    await expectNoAxeViolations();

    // The Arabic name is required; the English one is optional.
    await user.click(screen.getByRole('button', { name: 'إضافة قسم' }));
    expect(screen.getByLabelText('اسم القسم بالعربية')).toHaveAccessibleDescription(/مطلوب/);
    expect(api.POST).not.toHaveBeenCalled();
    await user.type(screen.getByLabelText('اسم القسم بالعربية'), 'العناية');
    await user.click(screen.getByRole('button', { name: 'إضافة قسم' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/service-sections', {
        body: { nameAr: 'العناية', nameEn: null },
      }),
    );

    await user.click(screen.getByRole('button', { name: 'تحريك قسم اللحية للأعلى' }));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/service-sections/order', {
        body: { orderedIds: ['beard', 'hair'] },
      }),
    );

    await user.click(screen.getByRole('button', { name: 'حذف قسم الشعر' }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('تحت «أخرى»');
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'حذف' }));
    await waitFor(() =>
      expect(api.DELETE).toHaveBeenCalledWith('/api/v1/shop/service-sections/{sectionId}', {
        params: { path: { sectionId: 'hair' } },
      }),
    );
  });

  it('renames a section for the admin through the shop’s admin endpoint', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    renderWithIntl(<ServiceSections scope={{ kind: 'admin', shopId: 'shop-1' }} sections={SECTIONS} />);

    await user.click(screen.getByRole('button', { name: 'إعادة تسمية قسم اللحية' }));
    const name = screen.getByDisplayValue('اللحية');
    await user.clear(name);
    await user.type(name, 'الذقن');
    await user.click(screen.getByRole('button', { name: 'حفظ الاسم' }));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/admin/shops/{shopId}/service-sections/{sectionId}', {
        params: { path: { shopId: 'shop-1', sectionId: 'beard' } },
        body: { nameAr: 'الذقن', nameEn: null },
      }),
    );
  });

  it('places a service in a section and sends the section on every save, the old category kept', async () => {
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <ShopServiceEditor
        sections={SECTIONS}
        professionals={[]}
        service={{ ...service('svc-1', 'حلاقة', 1), categoryId: 'cat-hair', sectionId: 'hair' }}
      />,
    );

    const select = screen.getByLabelText(/القسم/);
    expect(select).toHaveValue('hair');
    // No platform category on the form any more (D-149).
    expect(screen.queryByLabelText(/التصنيف/)).not.toBeInTheDocument();
    await userEvent.selectOptions(select, 'beard');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    expect(api.PUT.mock.calls[0]![1].body).toMatchObject({ sectionId: 'beard', categoryId: 'cat-hair' });

    await userEvent.selectOptions(select, '');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(2));
    expect(api.PUT.mock.calls[1]![1].body).toMatchObject({ sectionId: null });
  });
});

describe('a barber’s own length per service (D-149, the owner’s item 2)', () => {
  it('sets how long a service takes this barber and sends each service with its length', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    const option = (serviceId: string, nameAr: string, assigned: boolean, own: number | null = null) => ({
      serviceId,
      nameAr,
      nameEn: null,
      price: 60,
      currency: 'SAR',
      durationMinutes: 30,
      isActive: true,
      assigned,
      ownDurationMinutes: own,
    });
    renderWithIntl(
      <main>
        <ProfessionalServicesForm
          scope="shop"
          canAssign
          data={{
            professionalId: 'pro-1',
            shopId: 'shop-1',
            services: [
              option('cut', 'حلاقة', true, 45),
              option('beard', 'لحية', true),
              option('wash', 'غسيل', false),
            ],
          }}
        />
      </main>,
    );

    expect(screen.getByLabelText('مدة «حلاقة» عند هذا الحلاق')).toHaveValue('45');
    expect(screen.getByLabelText('مدة «لحية» عند هذا الحلاق')).toHaveValue('');
    // An unassigned service has no length to set.
    expect(screen.queryByLabelText('مدة «غسيل» عند هذا الحلاق')).not.toBeInTheDocument();
    await expectNoAxeViolations();

    await user.selectOptions(screen.getByLabelText('مدة «لحية» عند هذا الحلاق'), '20');
    await user.click(screen.getByRole('checkbox', { name: /غسيل/ }));
    await user.click(screen.getByRole('button', { name: 'حفظ الإسناد' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    const [path, init] = api.PUT.mock.calls[0]!;
    expect(path).toBe('/api/v1/shop/professionals/{professionalId}/services');
    expect(init.body).toEqual({
      serviceIds: null,
      services: [
        { serviceId: 'cut', durationMinutes: 45 },
        { serviceId: 'beard', durationMinutes: 20 },
        { serviceId: 'wash', durationMinutes: null },
      ],
    });
  });
});

describe('the owner’s barber page: one save (D-155, the client’s 3-8)', () => {
  it('saves the services and the profile together, and says while changes are unsaved', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    const option = (serviceId: string, nameAr: string, assigned: boolean) => ({
      serviceId,
      nameAr,
      nameEn: null,
      price: 60,
      currency: 'SAR',
      durationMinutes: 30,
      isActive: true,
      assigned,
      ownDurationMinutes: null,
    });
    renderWithIntl(
      <main>
        <SaveGroup testId="barber-save">
          <ProfessionalServicesForm
            scope="shop"
            canAssign
            data={{ professionalId: 'pro-1', shopId: 'shop-1', services: [option('cut', 'حلاقة', true)] }}
          />
          <EditProfessionalForm
            scope="shop"
            professional={{
              id: 'pro-1',
              slug: 'saad',
              nameAr: 'سعد',
              nameEn: 'Saad',
              specialtyAr: null,
              specialtyEn: null,
              bioAr: null,
              bioEn: null,
              version: 7,
              languages: ['ar'],
            }}
          />
        </SaveGroup>
      </main>,
    );

    // No button per form: «حفظ الإسناد» and «حفظ الملف» are gone, and nothing is unsaved yet.
    expect(screen.queryByRole('button', { name: 'حفظ الإسناد' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'حفظ الملف' })).not.toBeInTheDocument();
    const save = screen.getByRole('button', { name: 'حفظ التغييرات' });
    expect(save).toBeDisabled();

    // The client’s test: tick Turkish, change a length, then save once.
    await user.click(screen.getByRole('checkbox', { name: 'التركية' }));
    await user.selectOptions(screen.getByLabelText('مدة «حلاقة» عند هذا الحلاق'), '45');
    expect(screen.getByText('عندك تعديلات ما انحفظت')).toBeInTheDocument();
    await expectNoAxeViolations();
    await user.click(save);

    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(2));
    const calls = api.PUT.mock.calls.map(([path, init]) => [path, init.body]);
    expect(calls[0]).toEqual([
      '/api/v1/shop/professionals/{professionalId}/services',
      { serviceIds: null, services: [{ serviceId: 'cut', durationMinutes: 45 }] },
    ]);
    expect(calls[1]![0]).toBe('/api/v1/shop/professionals/{professionalId}');
    expect(calls[1]![1]).toMatchObject({ languages: ['ar', 'tr'], version: 7 });
    expect(await screen.findByText('حُفظت التغييرات.')).toBeInTheDocument();
    expect(screen.queryByText('عندك تعديلات ما انحفظت')).not.toBeInTheDocument();
  });
});
