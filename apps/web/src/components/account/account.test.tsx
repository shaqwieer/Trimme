import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RichText, safeHref } from '@/components/legal/RichText';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { DeleteAccount } from './DeleteAccount';
import { ProfileForm } from './ProfileForm';

const api = vi.hoisted(() => ({ GET: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() }));
const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => router,
  Link: ({ href, children, ...props }: { href: string; children: ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

const ok = <T,>(data: T, status = 200) => ({ data, response: new Response(null, { status }) });
const fail = (status: number, errorCode: string, errors?: Record<string, string[]>) => ({
  error: { status, errorCode, title: errorCode, errors },
  response: new Response(null, { status }),
});

const PROFILE = {
  displayName: 'هند',
  email: null,
  phoneMasked: '+966 5•• ••• •12',
  phoneChangeAllowed: true,
  preferredLocale: 'ar' as const,
};

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('ProfileForm (D-144)', () => {
  it('saves an email and a new number, after warning that the new number is the sign-in number', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(ok({}));
    const { container } = renderWithIntl(<ProfileForm profile={PROFILE} />);

    expect(within(screen.getByTestId('profile-phone')).getByText(PROFILE.phoneMasked)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/البريد الإلكتروني/), 'hind@example.com');
    await user.click(screen.getByRole('button', { name: 'تغيير الرقم' }));
    expect(screen.getByText('ستسجّل الدخول بالرقم الجديد')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/رقم الجوال الجديد/), '512345678');
    await expectNoAxeViolations(container);
    await user.click(screen.getByRole('button', { name: 'حفظ التغييرات' }));

    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/me/profile', {
        body: {
          displayName: 'هند',
          preferredLocale: 'ar',
          email: 'hind@example.com',
          phone: '+966512345678',
        },
      }),
    );
    expect(router.replace).toHaveBeenCalledWith('/account?saved=1', { locale: 'ar' });
  });

  it('keeps the number when it is not being changed, and checks the email and the new number first', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(fail(409, 'auth.phone_taken'));
    renderWithIntl(<ProfileForm profile={PROFILE} />);

    await user.type(screen.getByLabelText(/البريد الإلكتروني/), 'not-an-email');
    await user.click(screen.getByRole('button', { name: 'حفظ التغييرات' }));
    expect(await screen.findByText('أدخل بريداً إلكترونياً صحيحاً')).toBeInTheDocument();
    expect(api.PUT).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText(/البريد الإلكتروني/));
    await user.click(screen.getByRole('button', { name: 'تغيير الرقم' }));
    await user.type(screen.getByLabelText(/رقم الجوال الجديد/), '5123');
    await user.click(screen.getByRole('button', { name: 'حفظ التغييرات' }));
    expect(api.PUT).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'إلغاء التغيير' }));
    await user.click(screen.getByRole('button', { name: 'حفظ التغييرات' }));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/me/profile', {
        body: { displayName: 'هند', preferredLocale: 'ar', email: null, phone: null },
      }),
    );
    expect(await screen.findByText('هذا الرقم مسجّل بحساب آخر.')).toBeInTheDocument();
  });

  it('shows the number read-only while it cannot be changed here', () => {
    renderWithIntl(<ProfileForm profile={{ ...PROFILE, phoneChangeAllowed: false }} />);
    expect(screen.queryByRole('button', { name: 'تغيير الرقم' })).not.toBeInTheDocument();
    expect(screen.getByText('لتغيير رقم جوالك تواصل مع فريق Groom Here.')).toBeInTheDocument();
  });
});

describe('DeleteAccount (D-144)', () => {
  it('asks for confirmation, then deletes and leaves for the home page', async () => {
    const user = userEvent.setup();
    const replace = vi.fn();
    vi.stubGlobal('location', { ...window.location, replace });
    api.DELETE.mockResolvedValue(ok(undefined, 204));
    renderWithIntl(<DeleteAccount />);

    await user.click(screen.getByRole('button', { name: 'حذف حسابي' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent('حذف حسابك نهائياً؟');
    await user.click(within(dialog).getByRole('button', { name: 'نعم، احذف حسابي' }));

    await waitFor(() => expect(api.DELETE).toHaveBeenCalledWith('/api/v1/me'));
    await waitFor(() => expect(replace).toHaveBeenCalledWith('/ar'));
  });

  it('with appointments ahead, says so and links to them', async () => {
    const user = userEvent.setup();
    api.DELETE.mockResolvedValue(fail(409, 'account.has_upcoming_bookings'));
    renderWithIntl(<DeleteAccount />);

    await user.click(screen.getByRole('button', { name: 'حذف حسابي' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'نعم، احذف حسابي' }),
    );

    expect(await screen.findByText(/لديك مواعيد قادمة/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'عرض مواعيدي' })).toHaveAttribute('href', '/account/bookings');
  });
});

describe('RichText (D-144)', () => {
  it('renders the allowlisted document as elements and drops anything else', () => {
    const { container } = renderWithIntl(
      <RichText
        document={{
          type: 'doc',
          content: [
            { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'الخدمة' }] },
            {
              type: 'paragraph',
              content: [
                { type: 'text', text: 'اقرأ ' },
                {
                  type: 'text',
                  text: 'السياسة',
                  marks: [
                    { type: 'bold' },
                    { type: 'link', attrs: { href: 'https://groomhere.sa/ar/terms' } },
                  ],
                },
                {
                  type: 'text',
                  text: ' هنا',
                  marks: [{ type: 'link', attrs: { href: 'javascript:alert(1)' } }],
                },
              ],
            },
            {
              type: 'bulletList',
              content: [
                {
                  type: 'listItem',
                  content: [{ type: 'paragraph', content: [{ type: 'text', text: 'بند' }] }],
                },
              ],
            },
            { type: 'image', attrs: { src: 'https://evil.example/x.png' } },
            { type: 'paragraph', content: [{ type: 'text', text: '<script>alert(1)</script>' }] },
          ],
        }}
      />,
    );

    expect(screen.getByRole('heading', { level: 2, name: 'الخدمة' })).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'السياسة' });
    expect(link).toHaveAttribute('href', 'https://groomhere.sa/ar/terms');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    expect(screen.getAllByRole('link')).toHaveLength(1);
    expect(screen.getByRole('listitem')).toHaveTextContent('بند');
    expect(container.querySelector('img, script')).toBeNull();
    expect(screen.getByText('<script>alert(1)</script>')).toBeInTheDocument();
  });

  it('accepts only safe link targets', () => {
    expect(safeHref('https://a.example')).toBe('https://a.example');
    expect(safeHref('mailto:x@y.z')).toBe('mailto:x@y.z');
    expect(safeHref('/ar/privacy')).toBe('/ar/privacy');
    expect(safeHref('//evil.example')).toBeNull();
    expect(safeHref('javascript:alert(1)')).toBeNull();
    expect(safeHref('data:text/html,x')).toBeNull();
  });
});
