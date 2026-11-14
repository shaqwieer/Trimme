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

describe('BarberLoginControls (D-145)', () => {
  it('turns a barber login on with a temporary password of at least 10 characters', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(ok({ ...off, enabled: true, mustChangePassword: true }));
    renderWithIntl(<BarberLoginControls scope="shop" professionalId="p1" name="فيصل" login={off} />);

    expect(screen.getByText('الدخول غير مفعّل')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'تفعيل الدخول' }));
    const field = await screen.findByLabelText('كلمة المرور المؤقتة');
    await user.type(field, 'short');
    expect(screen.getByRole('button', { name: 'حفظ' })).toBeDisabled();
    expect(screen.getByText('استخدم 10 أحرف على الأقل')).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
    await user.type(field, '-temp-2026');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(await screen.findByText('تم حفظ دخول فيصل. أعطه كلمة المرور المؤقتة.')).toBeInTheDocument();
    expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/professionals/{professionalId}/login', {
      params: { path: { professionalId: 'p1' } },
      body: { password: 'short-temp-2026' },
    });
    expect(router.refresh).toHaveBeenCalled();
  });

  it('cannot turn it on without a WhatsApp number, and tells the owner who adds it', () => {
    renderWithIntl(
      <BarberLoginControls
        scope="shop"
        professionalId="p1"
        name="فيصل"
        login={{ ...off, hasNumber: false }}
      />,
    );
    expect(screen.getByRole('button', { name: 'تفعيل الدخول' })).toBeDisabled();
    expect(
      screen.getByText('لا يوجد رقم واتساب لهذا الحلاق. اطلب من فريق Groom Here إضافته.'),
    ).toBeInTheDocument();
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
