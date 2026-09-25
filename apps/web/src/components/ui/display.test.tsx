import { fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { initials } from './Avatar';
import { AppointmentCard, KpiTile, ProfessionalOption, ServiceOption, ShopCard } from './cards';
import { BarChart, QrCard, RatingDistribution } from './charts';
import { Breadcrumb, Pagination, pageWindow, ResponsiveTable, Timeline } from './data';
import { ErrorState, ExpiredSession, InlineAlert, PermissionDenied, SkeletonList } from './states';
import { UploadDropZone, validateUpload } from './UploadDropZone';

const shop = {
  href: '/shops/al-asala',
  name: 'صالون الأصالة للحلاقة',
  verified: true,
  rating: 4.8,
  reviewCount: 346,
  district: 'الملقا',
  distanceKm: 2.44,
  openingLabel: 'مفتوح حتى ١١:٠٠ م',
  isOpen: true,
  fromPrice: 35,
  tags: ['حلاقة', 'لحية'],
};

describe('cards', () => {
  it('ShopCard links the name, marks verification and formats quantities with Latin digits', async () => {
    const { container } = renderWithIntl(<ShopCard shop={shop} />);
    expect(screen.getByRole('link', { name: 'صالون الأصالة للحلاقة' })).toHaveAttribute(
      'href',
      '/ar/shops/al-asala',
    );
    expect(screen.getByRole('img', { name: 'محل موثّق' })).toBeInTheDocument();
    expect(screen.getByText('2.4 كم')).toBeInTheDocument();
    expect(screen.getByText('من 35 ر.س')).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('ServiceOption is a radio showing the shop price and duration', () => {
    renderWithIntl(
      <ServiceOption name="service" value="s1" title="باقة شعر ولحية" price={85} durationMinutes={50} />,
    );
    const radio = screen.getByRole('radio', { name: /باقة شعر ولحية/ });
    expect(radio).toBeInTheDocument();
    expect(screen.getByText('85 ر.س')).toBeInTheDocument();
    expect(screen.getByText('٥٠ دقيقة')).toBeInTheDocument();
  });

  it('ProfessionalOption on leave is disabled and says so', () => {
    renderWithIntl(<ProfessionalOption name="pro" value="p1" displayName="ماجد العتيبي" unavailable />);
    expect(screen.getByRole('radio', { name: /ماجد العتيبي/ })).toBeDisabled();
    expect(screen.getByText('في إجازة')).toBeInTheDocument();
  });

  it('AppointmentCard shows the status badge and links to the booking', () => {
    renderWithIntl(
      <AppointmentCard
        appointment={{
          href: '/account/bookings/1',
          dayNumber: '18',
          monthLabel: 'سبتمبر',
          serviceName: 'باقة شعر ولحية',
          status: 'Confirmed',
          shopName: 'صالون الأصالة',
          professionalName: 'فيصل القحطاني',
          timeRange: '٥:٣٠ م — ٦:٢٠ م',
        }}
      />,
    );
    expect(screen.getByRole('link', { name: 'باقة شعر ولحية' })).toHaveAttribute(
      'href',
      '/ar/account/bookings/1',
    );
    expect(screen.getByText('مؤكد')).toBeInTheDocument();
  });

  it('KpiTile colours the delta by meaning, not by sign', () => {
    renderWithIntl(
      <KpiTile label="معدل عدم الحضور" value="6.2%" delta={{ text: '+1.4 نقطة', tone: 'bad' }} />,
    );
    expect(screen.getByText('+1.4 نقطة').className).toContain('text-danger-700');
  });
});

describe('Avatar initials', () => {
  it.each([
    ['محمد العنزي', 'م ع'],
    ['ماجد العتيبي', 'م ع'],
    ['صالون الأصالة', 'ص أ'],
    ['فيصل', 'ف'],
    ['Faisal Alqahtani', 'FA'],
  ])('%s → %s', (name, expected) => {
    expect(initials(name)).toBe(expected);
  });
});

describe('states', () => {
  it('ErrorState is announced with default copy', () => {
    renderWithIntl(<ErrorState />);
    expect(screen.getByRole('alert')).toHaveTextContent('حدث خطأ غير متوقع');
  });

  it('PermissionDenied and ExpiredSession offer a way forward', () => {
    renderWithIntl(
      <>
        <PermissionDenied />
        <ExpiredSession signInHref="/auth/sign-in?returnTo=%2Fshop" />
      </>,
    );
    expect(screen.getByRole('link', { name: 'العودة إلى الرئيسية' })).toHaveAttribute('href', '/ar');
    expect(screen.getByRole('link', { name: 'تسجيل الدخول' })).toHaveAttribute(
      'href',
      '/ar/auth/sign-in?returnTo=%2Fshop',
    );
  });

  it('SkeletonList exposes a single busy status', () => {
    renderWithIntl(<SkeletonList label="جارٍ تحميل المواعيد" rows={3} />);
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute('aria-busy', 'true');
    expect(status).toHaveTextContent('جارٍ تحميل المواعيد');
  });

  it('danger InlineAlert is an alert, info is a status', () => {
    renderWithIntl(
      <>
        <InlineAlert tone="danger" title="تعذّر تحميل الأوقات المتاحة" />
        <InlineAlert tone="info" title="الاشتراك ينتهي بعد ١٤ يوماً" />
      </>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('تعذّر تحميل الأوقات المتاحة');
    expect(screen.getByRole('status')).toHaveTextContent('الاشتراك ينتهي بعد ١٤ يوماً');
  });
});

type ShopRow = { id: string; name: string; district: string; pros: number };
const rows: ShopRow[] = [
  { id: '1', name: 'صالون الأصالة', district: 'الملقا', pros: 6 },
  { id: '2', name: 'باربر هاوس', district: 'حطين', pros: 4 },
];

describe('data display', () => {
  it('ResponsiveTable renders a captioned table with row headers and a card list for phones', async () => {
    const { container } = renderWithIntl(
      <ResponsiveTable<ShopRow>
        caption="المحلات"
        rows={rows}
        rowKey={(row) => row.id}
        columns={[
          { key: 'name', header: 'المحل', cell: (row) => row.name, mobile: 'primary' },
          { key: 'district', header: 'الحي', cell: (row) => row.district },
          { key: 'pros', header: 'الحلاقون', cell: (row) => row.pros, align: 'end' },
        ]}
      />,
    );
    const table = screen.getByRole('table', { name: 'المحلات' });
    expect(within(table).getAllByRole('rowheader')).toHaveLength(2);
    expect(screen.getByRole('list', { name: 'المحلات' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('pageWindow keeps first, last and neighbours with gaps', () => {
    expect(pageWindow(1, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(pageWindow(6, 20)).toEqual([1, 'gap', 5, 6, 7, 'gap', 20]);
  });

  it('Pagination marks the current page and disables previous on the first page', () => {
    renderWithIntl(
      <Pagination page={1} pageSize={4} total={128} hrefForPage={(p) => `/admin/shops?page=${p}`} />,
    );
    expect(screen.getByRole('link', { name: 'الصفحة 1' })).toHaveAttribute('aria-current', 'page');
    expect(screen.queryByRole('link', { name: 'الصفحة السابقة' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'الصفحة التالية' })).toHaveAttribute(
      'href',
      '/ar/admin/shops?page=2',
    );
    expect(screen.getByText('عرض 1–4 من 128')).toBeInTheDocument();
  });

  it('Breadcrumb marks the current page', () => {
    renderWithIntl(
      <Breadcrumb
        items={[
          { label: 'الإدارة', href: '/admin' },
          { label: 'المحلات', href: '/admin/shops' },
          { label: 'صالون الأصالة' },
        ]}
      />,
    );
    expect(screen.getByRole('navigation', { name: 'مسار التنقل' })).toBeInTheDocument();
    expect(screen.getByText('صالون الأصالة')).toHaveAttribute('aria-current', 'page');
  });

  it('Timeline renders an ordered list', () => {
    renderWithIntl(<Timeline items={[{ id: 'a', title: 'تم إنشاء الحجز', meta: '٥:١٢ م' }]} />);
    expect(screen.getByRole('listitem')).toHaveTextContent('تم إنشاء الحجز');
  });
});

describe('UploadDropZone', () => {
  const png = new File(['x'], 'cover.png', { type: 'image/png' });
  const pdf = new File(['x'], 'menu.pdf', { type: 'application/pdf' });

  it('validates type and size before uploading', () => {
    expect(validateUpload(png, ['image/png'], 10)).toBeNull();
    expect(validateUpload(pdf, ['image/png'], 10)).toBe('type');
    expect(
      validateUpload(new File(['x'.repeat(20)], 'big.png', { type: 'image/png' }), ['image/png'], 10),
    ).toBe('size');
  });

  it('announces a localized error for unsupported files and accepts images', () => {
    const onFileSelected = vi.fn();
    renderWithIntl(<UploadDropZone label="صورة الغلاف" onFileSelected={onFileSelected} />);
    const input = screen.getByLabelText('صورة الغلاف');

    fireEvent.change(input, { target: { files: [pdf] } });
    expect(screen.getByRole('alert')).toHaveTextContent('نوع الملف غير مدعوم');
    expect(onFileSelected).not.toHaveBeenCalled();

    fireEvent.change(input, { target: { files: [png] } });
    expect(onFileSelected).toHaveBeenCalledWith(png);
    expect(screen.getByRole('status')).toHaveTextContent('تم اختيار cover.png');
  });
});

describe('charts (dataviz rules)', () => {
  it('RatingDistribution exposes each row as text', () => {
    renderWithIntl(<RatingDistribution counts={{ 5: 268, 4: 52, 3: 15, 2: 7, 1: 4 }} />);
    expect(screen.getByRole('list', { name: 'توزيع التقييمات' })).toBeInTheDocument();
    expect(screen.getByText('5 نجوم: 268 تقييم')).toBeInTheDocument();
  });

  it('BarChart ships an accessible data table and labels the peak', async () => {
    const { container } = renderWithIntl(
      <BarChart
        title="Bookings by hour"
        labelHeader="Hour"
        valueHeader="Bookings"
        data={[
          { label: '4', value: 62 },
          { label: '5', value: 88 },
          { label: '6', value: 96 },
        ]}
      />,
      { locale: 'en' },
    );
    const table = screen.getByRole('table', { name: /Bookings by hour/ });
    expect(within(table).getAllByRole('row')).toHaveLength(4);
    expect(within(table).getByText('96 (Peak)')).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('QrCard shows an LTR short link and a download action', () => {
    renderWithIntl(<QrCard name="صالون الأصالة" url="trimme.sa/s/alasalah" downloadHref="/admin/qr/1.png" />);
    expect(screen.getByRole('img', { name: 'رمز QR لـ صالون الأصالة' })).toBeInTheDocument();
    expect(screen.getByText('trimme.sa/s/alasalah')).toHaveAttribute('dir', 'ltr');
    expect(screen.getByRole('link', { name: 'تنزيل' })).toHaveAttribute('href', '/ar/admin/qr/1.png');
  });
});
