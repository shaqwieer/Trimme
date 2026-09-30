import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { bookingSource } from '@/lib/booking/format';
import { displayUrl, earliestSlots, lastDays, qrImageUrl, qrPeriod } from '@/lib/qr';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { CreateQrCodeDialog, QrCodeToggle } from './QrAdminActions';
import { type QrCode, QrCodeCard, QrMaterials } from './QrParts';
import { QrVisitRecorder } from './QrVisitRecorder';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
const router = vi.hoisted(() => ({ refresh: vi.fn(), push: vi.fn() }));
vi.mock('@/i18n/navigation', async () => {
  const { createElement } = await import('react');
  return {
    useRouter: () => router,
    usePathname: () => '/admin/qr',
    Link: ({ href, children, ...props }: { href: string; children: React.ReactNode }) =>
      createElement('a', { href, ...props }, children),
  };
});

const ok = <T,>(data: T, status = 200) => ({ data, response: new Response(null, { status }) });

const code = (overrides: Partial<QrCode> = {}): QrCode => ({
  id: 'c1',
  code: 'aswn7qkd',
  url: 'https://trimme.sa/q/aswn7qkd',
  shopId: 's1',
  shopSlug: 'al-asala',
  shopNameAr: 'صالون الأصالة',
  shopNameEn: 'Al Asala',
  targetType: 'Professional',
  professionalId: 'p1',
  professionalNameAr: 'سلطان الحربي',
  professionalNameEn: 'Sultan Al-Harbi',
  label: 'مرآة سلطان',
  isActive: true,
  createdAt: '2026-09-01T09:00:00Z',
  deactivatedAt: null,
  visits: 42,
  bookings: 7,
  version: 3,
  ...overrides,
});

beforeEach(() => {
  api.GET.mockReset();
  api.POST.mockReset();
  router.refresh.mockReset();
});

describe('QR helpers', () => {
  it('builds same-origin file URLs, strips the scheme for display, and picks a known period', () => {
    expect(qrImageUrl('shop', 'c1', 'pdf')).toBe('/api/v1/shop/qr/codes/c1/image?format=pdf');
    expect(qrImageUrl('admin', 'c1', 'png', 20)).toBe('/api/v1/admin/qr/codes/c1/image?format=png&size=20');
    expect(displayUrl('https://trimme.sa/q/aswn7qkd')).toBe('trimme.sa/q/aswn7qkd');
    expect(lastDays('2026-10-02', 7)).toEqual({ from: '2026-09-26', to: '2026-10-02' });
    expect(qrPeriod('90')).toBe(90);
    expect(qrPeriod('45')).toBe(30);
    expect(qrPeriod(undefined)).toBe(30);
  });

  it('merges the earliest day across professionals into its first three distinct times', () => {
    const offer = { id: 'svc', isPackage: false };
    const result = earliestSlots([
      {
        professionalId: 'a',
        date: '2026-10-01',
        offer,
        slots: [
          { startsAt: '2026-10-01T14:00:00Z', localTime: '17:00' },
          { startsAt: '2026-10-01T16:00:00Z', localTime: '19:00' },
        ],
      },
      {
        professionalId: 'b',
        date: '2026-10-01',
        offer,
        slots: [
          { startsAt: '2026-10-01T13:00:00Z', localTime: '16:00' },
          { startsAt: '2026-10-01T14:00:00Z', localTime: '17:00' },
        ],
      },
      {
        professionalId: 'c',
        date: '2026-10-02',
        offer,
        slots: [{ startsAt: '2026-10-02T06:00:00Z', localTime: '09:00' }],
      },
      { professionalId: 'd', date: null, offer: null, slots: [] },
    ]);
    expect(result.date).toBe('2026-10-01');
    expect(result.slots.map((s) => [s.localTime, s.professionalId])).toEqual([
      ['16:00', 'b'],
      ['17:00', 'a'],
      ['19:00', 'a'],
    ]);
    expect(earliestSlots([])).toEqual({ date: null, slots: [] });
  });

  it('labels an online booking credited to a scan as a QR booking, never a walk-in', () => {
    expect(bookingSource({ channel: 'Online', viaQr: true })).toBe('Qr');
    expect(bookingSource({ channel: 'Online', viaQr: false })).toBe('Online');
    expect(bookingSource({ channel: 'WalkIn', viaQr: false })).toBe('WalkIn');
  });
});

describe('QR components', () => {
  it('shows a code card with the barber target, an LTR URL, the figures and plain file links', async () => {
    const { container } = renderWithIntl(
      <QrCodeCard scope="shop" code={code()} posterHref="/shop/qr/c1/poster" />,
    );
    expect(screen.getByRole('heading', { name: 'صفحة سلطان الحربي' })).toBeInTheDocument();
    expect(screen.getByText('trimme.sa/q/aswn7qkd')).toHaveAttribute('dir', 'ltr');
    expect(screen.getByText('42 مسح · 7 حجز')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'رمز QR يفتح صفحة سلطان الحربي' })).toBeInTheDocument();
    const pdf = screen.getByRole('link', { name: 'تنزيل الرمز aswn7qkd بصيغة PDF' });
    expect(pdf).toHaveAttribute('href', '/api/v1/shop/qr/codes/c1/image?format=pdf');
    expect(pdf).toHaveAttribute('download');
    expect(screen.getByRole('link', { name: 'ملصق A5' })).toHaveAttribute('href', '/shop/qr/c1/poster');
    await expectNoAxeViolations(container);
  });

  it('offers no files for a switched-off code', () => {
    renderWithIntl(
      <QrCodeCard
        scope="shop"
        code={code({ isActive: false, targetType: 'Shop', professionalId: null })}
        posterHref="/p"
      />,
    );
    expect(screen.getByText('موقوف')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /PNG/ })).not.toBeInTheDocument();
  });

  it('explains the print materials and the privacy of scans', async () => {
    const { container } = renderWithIntl(<QrMaterials namespace="adminQr" />, { locale: 'en' });
    expect(screen.getByText('One code per barber')).toBeInTheDocument();
    expect(screen.getByText(/We keep no IP address/)).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('records the scan once from the browser', async () => {
    api.POST.mockResolvedValue(ok(undefined, 204));
    renderWithIntl(<QrVisitRecorder code="aswn7qkd" locale="ar" />);
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST).toHaveBeenCalledWith('/api/v1/public/qr/{code}/visits', {
      params: { path: { code: 'aswn7qkd' } },
      body: { locale: 'ar' },
    });
  });

  it('creates a barber code only after the shop and one of its active barbers (loaded for that shop) are chosen', async () => {
    const user = userEvent.setup();
    api.GET.mockImplementation(
      (_path: string, init: { params: { query: { shopId: string; status: string } } }) =>
        Promise.resolve(
          ok({
            items:
              init.params.query.shopId === 's1' && init.params.query.status === 'Active'
                ? [{ id: 'p1', nameAr: 'سلطان الحربي', nameEn: 'Sultan Al-Harbi' }]
                : [],
          }),
        ),
    );
    api.POST.mockResolvedValue(ok({ ...code(), code: 'bhwm6twy' }, 201));
    renderWithIntl(
      <CreateQrCodeDialog
        shops={[
          { id: 's1', name: 'صالون الأصالة' },
          { id: 's2', name: 'باربر هاوس' },
        ]}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'رمز جديد' }));
    await user.click(screen.getByRole('radio', { name: 'صفحة حلاق' }));
    await user.click(screen.getByRole('button', { name: 'إنشاء الرمز' }));
    expect(screen.getByText('اختر المحل والحلاق.')).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();

    await user.selectOptions(screen.getByLabelText('المحل', { exact: true }), 's2');
    expect(await screen.findByText('لا يوجد حلاقون نشطون في هذا المحل.')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('المحل', { exact: true }), 's1');
    await waitFor(() => expect(screen.getByLabelText('الحلاق')).toBeEnabled());
    await user.selectOptions(screen.getByLabelText('الحلاق'), 'p1');
    await user.type(screen.getByLabelText(/مكان الاستخدام/), 'مرآة سلطان');
    await user.click(screen.getByRole('button', { name: 'إنشاء الرمز' }));

    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/admin/qr/codes', {
        body: { shopId: 's1', professionalId: 'p1', label: 'مرآة سلطان' },
      }),
    );
    expect(api.GET).toHaveBeenCalledWith('/api/v1/admin/professionals', {
      params: { query: { shopId: 's1', status: 'Active', page: 1, pageSize: 100 } },
    });
    expect(await screen.findByText('تم إنشاء الرمز bhwm6twy.')).toBeInTheDocument();
    expect(router.refresh).toHaveBeenCalled();
  });

  it('switches a code off only after confirming, with the version read', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue(ok(code({ isActive: false })));
    renderWithIntl(<QrCodeToggle codeId="c1" code="aswn7qkd" version={3} active />);
    await user.click(screen.getByRole('button', { name: 'إيقاف' }));
    expect(screen.getByRole('alertdialog', { name: 'إيقاف الرمز aswn7qkd؟' })).toBeInTheDocument();
    await user.click(screen.getAllByRole('button', { name: 'إيقاف' }).at(-1)!);
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/admin/qr/codes/{codeId}/deactivate', {
        params: { path: { codeId: 'c1' } },
        body: { version: 3 },
      }),
    );
    expect(router.refresh).toHaveBeenCalled();
  });
});
