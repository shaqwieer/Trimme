import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CalendarProvider } from '@/components/providers/CalendarProvider';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { ShopAccount } from './ShopAccount';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() }));
const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => router,
  usePathname: () => '/shop/account',
  Link: ({ href, locale, children, ...props }: { href: string; locale?: string; children: ReactNode }) => (
    <a href={locale ? `/${locale}${href}` : href} {...props}>
      {children}
    </a>
  ),
}));

const ok = <T,>(data: T, status = 200) => ({ data, response: new Response(null, { status }) });

const BARBER = {
  displayName: 'سعد',
  mobileMasked: '+966 5•• ••• •01',
  email: null,
  professionalId: 'p1',
  nameAr: 'سعد',
  nameEn: 'Saad',
  version: 7,
  canRename: true,
  canDelete: true,
};

beforeEach(() => {
  vi.clearAllMocks();
  document.cookie = 'trimme_calendar=; path=/; max-age=0';
});

describe('ShopAccount (D-151, the client’s 2-2)', () => {
  it('shows the barber’s name, mobile, calendar, languages and notifications, and renames them everywhere', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(ok(BARBER));
    const { container } = renderWithIntl(<ShopAccount account={BARBER} />);

    expect(screen.getByTestId('account-name-value')).toHaveTextContent('سعد');
    expect(screen.getByText('+966 5•• ••• •01')).toBeInTheDocument();
    expect(screen.getByText('تغيير الرقم بيكون متاح لاحقاً.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'ميلادي' })).toHaveAttribute('aria-pressed', 'true');
    const languages = screen.getByRole('group', { name: 'لغة شاشة المحل' });
    expect(within(languages).getByRole('link', { name: 'Türkçe' })).toHaveAttribute(
      'href',
      '/tr/shop/account',
    );
    expect(within(languages).getByRole('link', { name: 'العربية' })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('link', { name: 'فتح الإشعارات' })).toHaveAttribute(
      'href',
      '/shop/notifications',
    );
    expect(screen.getByRole('button', { name: 'تسجيل الخروج' })).toBeInTheDocument();
    await expectNoAxeViolations(container);

    await user.click(screen.getByRole('button', { name: 'تعديل الاسم' }));
    const name = screen.getByLabelText('اسمك بالعربية');
    await user.clear(name);
    await user.type(name, 'سعد العتيبي');
    await user.click(screen.getByRole('button', { name: 'حفظ الاسم' }));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/account/name', {
        body: { nameAr: 'سعد العتيبي', nameEn: 'Saad', version: 7 },
      }),
    );
    expect(await screen.findByText('حُفظ الاسم.')).toBeInTheDocument();
    expect(router.refresh).toHaveBeenCalled();
  });

  it('keeps the calendar on this device and writes dates in Hijri once chosen', async () => {
    const user = userEvent.setup();
    const { unmount } = renderWithIntl(<ShopAccount account={BARBER} />);
    await user.click(screen.getByRole('button', { name: 'هجري' }));
    expect(document.cookie).toContain('trimme_calendar=hijri');
    expect(router.refresh).toHaveBeenCalled();
    unmount();

    renderWithIntl(
      <CalendarProvider calendar="islamic-umalqura">
        <ShopAccount account={BARBER} />
      </CalendarProvider>,
    );
    expect(screen.getByRole('button', { name: 'هجري' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('calendar-preview')).toHaveTextContent('هـ');
  });

  it('deletes a barber’s account after a confirmation; the owner can neither rename nor delete here', async () => {
    const user = userEvent.setup();
    const replace = vi.fn();
    vi.stubGlobal('location', { ...window.location, replace });
    api.DELETE.mockResolvedValue({ data: undefined, response: new Response(null, { status: 204 }) });
    const { unmount } = renderWithIntl(<ShopAccount account={BARBER} />);

    await user.click(screen.getByRole('button', { name: 'حذف حسابي' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'نعم، احذف حسابي' }),
    );
    await waitFor(() => expect(api.DELETE).toHaveBeenCalledWith('/api/v1/shop/account'));
    await waitFor(() => expect(replace).toHaveBeenCalledWith('/ar/auth/staff/sign-in'));
    vi.unstubAllGlobals();
    unmount();

    renderWithIntl(
      <ShopAccount
        account={{
          ...BARBER,
          displayName: 'صاحب المحل',
          mobileMasked: null,
          email: 'owner@example.test',
          professionalId: null,
          nameAr: null,
          nameEn: null,
          version: null,
          canRename: false,
          canDelete: false,
        }}
      />,
    );
    expect(screen.getByTestId('account-name-value')).toHaveTextContent('صاحب المحل');
    expect(screen.getByText('owner@example.test')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'تعديل الاسم' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('delete-shop-account')).not.toBeInTheDocument();
  });
});
