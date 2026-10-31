import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { StaffSignInForm } from './StaffForms';

const api = vi.hoisted(() => ({ POST: vi.fn() }));
const router = vi.hoisted(() => ({ replace: vi.fn() }));

vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => router,
  Link: ({ href, children, ...props }: { href: string; children: ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

const fail = (status: number, errorCode: string, errors?: Record<string, string[]>) => ({
  error: { status, errorCode, title: errorCode, errors },
  response: new Response(null, { status }),
});
const signedIn = { data: { isNewUser: false, user: { userType: 'ShopUser' } }, response: new Response(null) };

beforeEach(() => {
  api.POST.mockReset();
  router.replace.mockReset();
});

describe('StaffSignInForm with a temporary password (D-139)', () => {
  it('asks for a new password, then signs in with it', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce(fail(403, 'auth.password_change_required'));
    renderWithIntl(<StaffSignInForm />);

    await user.type(screen.getByLabelText('البريد الإلكتروني'), 'owner@shop.sa');
    await user.type(screen.getByLabelText('كلمة المرور'), 'temporary pass');
    await user.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));

    expect(await screen.findByRole('heading', { name: 'اختر كلمة مرور جديدة' })).toBeInTheDocument();
    expect(router.replace).not.toHaveBeenCalled();

    // The API refuses to keep the temporary password.
    api.POST.mockResolvedValueOnce(
      fail(400, 'validation.failed', { password: ['validation.password_unchanged'] }),
    );
    await user.type(screen.getByLabelText('كلمة المرور الجديدة'), 'temporary pass');
    await user.type(screen.getByLabelText('تأكيد كلمة المرور'), 'temporary pass');
    await user.click(screen.getByRole('button', { name: 'حفظ وتسجيل الدخول' }));
    expect(await screen.findByText('اختر كلمة مرور مختلفة عن المؤقتة.')).toBeInTheDocument();

    api.POST.mockResolvedValueOnce(signedIn);
    await user.clear(screen.getByLabelText('كلمة المرور الجديدة'));
    await user.type(screen.getByLabelText('كلمة المرور الجديدة'), 'my own passphrase');
    await user.clear(screen.getByLabelText('تأكيد كلمة المرور'));
    await user.type(screen.getByLabelText('تأكيد كلمة المرور'), 'my own passphrase');
    await user.click(screen.getByRole('button', { name: 'حفظ وتسجيل الدخول' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/shop'));
    expect(api.POST).toHaveBeenLastCalledWith('/api/v1/auth/password/change-temporary', {
      body: { email: 'owner@shop.sa', password: 'temporary pass', newPassword: 'my own passphrase' },
    });
  });

  it('goes back to the sign-in form without the temporary password', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce(fail(403, 'auth.password_change_required'));
    renderWithIntl(<StaffSignInForm returnTo="/shop/appointments" />);

    await user.type(screen.getByLabelText('البريد الإلكتروني'), 'desk@shop.sa');
    await user.type(screen.getByLabelText('كلمة المرور'), 'temporary pass');
    await user.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));
    await user.click(await screen.findByRole('button', { name: 'رجوع' }));

    expect(screen.getByLabelText('البريد الإلكتروني')).toHaveValue('desk@shop.sa');
    expect(screen.getByLabelText('كلمة المرور')).toHaveValue('');
  });
});
