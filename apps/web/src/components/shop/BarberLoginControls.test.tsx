import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { BarberLoginControls } from './BarberLoginControls';

const api = vi.hoisted(() => ({ PUT: vi.fn(), DELETE: vi.fn() }));
const router = vi.hoisted(() => ({ refresh: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => router,
  Link: ({ href, children }: { href: string; children: ReactNode }) => <a href={href}>{children}</a>,
}));

const ok = <T,>(data: T, status = 200) => ({ data, response: new Response(null, { status }) });
const off = { professionalId: 'p1', enabled: false, mustChangePassword: false, hasNumber: true };

beforeEach(() => {
  api.PUT.mockReset();
  api.DELETE.mockReset();
  router.refresh.mockReset();
});

const issued = (password: string) =>
  ok({ login: { ...off, enabled: true, mustChangePassword: true }, temporaryPassword: password });

describe('BarberLoginControls (D-145, D-146)', () => {
  it('turns a barber login on and shows the password the server generated, once, to copy', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(issued('k7m3p9x2qa4b'));
    renderWithIntl(<BarberLoginControls scope="shop" professionalId="p1" name="فيصل" login={off} />);

    expect(screen.getByText('الدخول غير مفعّل')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'تفعيل الدخول' }));

    // No password is typed: the request has no body, and the answer's password is shown in a dialog.
    expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/professionals/{professionalId}/login', {
      params: { path: { professionalId: 'p1' } },
    });
    const dialog = await screen.findByRole('dialog', { name: 'كلمة المرور المؤقتة لـ فيصل' });
    expect(screen.getByTestId('temporary-password-value')).toHaveTextContent('k7m3p9x2qa4b');
    await expectNoAxeViolations(document.body);
    await user.click(screen.getByRole('button', { name: 'نسخ' }));
    expect(await screen.findByRole('button', { name: 'تم النسخ' })).toBeInTheDocument();
    await expect(navigator.clipboard.readText()).resolves.toBe('k7m3p9x2qa4b');
    expect(router.refresh).toHaveBeenCalled();

    // Closed, it is gone for good.
    await user.click(screen.getByRole('button', { name: 'تم' }));
    await waitFor(() => expect(dialog).not.toBeInTheDocument());
    expect(screen.queryByText('k7m3p9x2qa4b')).not.toBeInTheDocument();
    expect(screen.getByText('تم تفعيل دخول فيصل. أعطه كلمة المرور المؤقتة.')).toBeInTheDocument();
  });

  it('asks before a new temporary password, which signs the barber out', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(issued('n4w8pass2ttx'));
    renderWithIntl(
      <BarberLoginControls
        scope="admin"
        professionalId="p1"
        name="فيصل"
        login={{ ...off, enabled: true, mustChangePassword: false }}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'كلمة مرور مؤقتة جديدة' }));
    expect(await screen.findByText('كلمة مرور مؤقتة جديدة لـ فيصل؟')).toBeInTheDocument();
    expect(api.PUT).not.toHaveBeenCalled();
    await user.click(screen.getAllByRole('button', { name: 'كلمة مرور مؤقتة جديدة' }).at(-1)!);

    expect(await screen.findByTestId('temporary-password-value')).toHaveTextContent('n4w8pass2ttx');
    expect(api.PUT).toHaveBeenCalledWith('/api/v1/admin/professionals/{professionalId}/login', {
      params: { path: { professionalId: 'p1' } },
    });
  });

  it('cannot turn it on without a WhatsApp number, and says to add it', () => {
    renderWithIntl(
      <BarberLoginControls
        scope="shop"
        professionalId="p1"
        name="فيصل"
        login={{ ...off, hasNumber: false }}
      />,
    );
    expect(screen.getByRole('button', { name: 'تفعيل الدخول' })).toBeDisabled();
    expect(screen.getByText('أضف رقم واتساب الحلاق أولاً؛ هو رقم دخوله.')).toBeInTheDocument();
  });

  it('turns an admin-side login off after asking, through the admin endpoint', async () => {
    const user = userEvent.setup();
    api.DELETE.mockResolvedValue(ok({ ...off }));
    renderWithIntl(
      <BarberLoginControls
        scope="admin"
        professionalId="p1"
        name="فيصل"
        login={{ ...off, enabled: true, mustChangePassword: false }}
      />,
    );

    expect(screen.getByText('الدخول مفعّل')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'كلمة مرور مؤقتة جديدة' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'إيقاف الدخول' }));
    expect(await screen.findByText('إيقاف دخول فيصل؟')).toBeInTheDocument();
    await user.click(screen.getAllByRole('button', { name: 'إيقاف الدخول' }).at(-1)!);

    await waitFor(() =>
      expect(api.DELETE).toHaveBeenCalledWith('/api/v1/admin/professionals/{professionalId}/login', {
        params: { path: { professionalId: 'p1' } },
      }),
    );
    expect(await screen.findByText('أُوقف دخول فيصل.')).toBeInTheDocument();
  });
});
