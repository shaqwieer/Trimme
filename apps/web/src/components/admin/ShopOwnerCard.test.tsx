import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { ShopOwnerCard } from './ShopOwnerCard';

const api = vi.hoisted(() => ({ POST: vi.fn() }));
const refresh = vi.hoisted(() => vi.fn());
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ refresh }),
  Link: ({ href, children, ...props }: { href: string; children: ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

const owner = {
  userId: 'u1',
  displayName: 'محمد الأصالة',
  email: 'alasala@groomhere.sa',
  isDisabled: false,
  mustChangePassword: false,
  createdAt: '2026-10-01T09:00:00Z',
};

beforeEach(() => {
  api.POST.mockReset();
  refresh.mockReset();
});

describe('ShopOwnerCard (D-158)', () => {
  it('names the owner and gives them a new temporary password after a confirmation, shown once', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue({
      data: { account: { ...owner, mustChangePassword: true }, temporaryPassword: 'abcd2345efgh' },
      response: new Response(null),
    });
    const { container } = renderWithIntl(
      <ShopOwnerCard shopId="s1" owners={[owner]} addHref="/admin/shops/s1?tab=users" />,
    );

    const card = screen.getByTestId('shop-owner');
    expect(within(card).getByRole('heading', { name: 'المالك' })).toBeInTheDocument();
    expect(card).toHaveTextContent('محمد الأصالة');
    expect(card).toHaveTextContent('alasala@groomhere.sa');
    await expectNoAxeViolations(container);

    await user.click(screen.getByRole('button', { name: 'كلمة مرور مؤقتة جديدة' }));
    const confirm = screen.getByRole('alertdialog');
    expect(confirm).toHaveTextContent('كلمة مرور مؤقتة جديدة لـ محمد الأصالة؟');
    expect(api.POST).not.toHaveBeenCalled();
    await user.click(within(confirm).getByRole('button', { name: 'كلمة مرور مؤقتة جديدة' }));

    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith(
        '/api/v1/admin/shops/{shopId}/users/{userId}/temporary-password',
        {
          params: { path: { shopId: 's1', userId: 'u1' } },
        },
      ),
    );
    expect(await screen.findByTestId('temporary-password-value')).toHaveTextContent('abcd2345efgh');
    // How an owner signs in: by email, not a WhatsApp number like a barber.
    expect(screen.getByRole('dialog')).toHaveTextContent('ببريده alasala@groomhere.sa');
    expect(refresh).toHaveBeenCalled();
  });

  it('says when the shop has no owner yet, and cannot reset a disabled one', () => {
    const { unmount } = renderWithIntl(
      <ShopOwnerCard shopId="s1" owners={[]} addHref="/admin/shops/s1?tab=users" />,
    );
    expect(screen.getByText(/لا يوجد حساب مالك/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'إضافة حساب' })).toHaveAttribute(
      'href',
      '/admin/shops/s1?tab=users',
    );
    unmount();

    renderWithIntl(
      <ShopOwnerCard
        shopId="s1"
        owners={[{ ...owner, isDisabled: true }]}
        addHref="/admin/shops/s1?tab=users"
      />,
    );
    expect(screen.getByText('موقوف')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'كلمة مرور مؤقتة جديدة' })).toBeDisabled();
  });
});
