import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CreateBarberForm } from '@/components/admin/ProfessionalForms';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';

const api = vi.hoisted(() => ({ POST: vi.fn() }));
const router = vi.hoisted(() => ({ push: vi.fn(), refresh: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router }));

const created = {
  barber: {
    id: 'b1',
    slug: 'saad',
    nameAr: 'سعد',
    nameEn: 'Saad',
    specialtyAr: null,
    specialtyEn: null,
    bioAr: null,
    bioEn: null,
    avatarUrl: null,
    status: 'Active',
    whatsApp: { masked: '+966 5•• ••• •01', notificationsEnabled: true, verification: 'Unverified' },
    createdAt: '2026-11-18T09:00:00Z',
    version: 1,
  },
  temporaryPassword: 'k7m3p9x2qa4b',
};

beforeEach(() => {
  api.POST.mockReset();
  router.push.mockReset();
});

describe('CreateBarberForm (D-146)', () => {
  it('needs the mobile, adds the barber with their login, shows the password once, then opens their page', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue({ data: created, response: new Response(null, { status: 201 }) });
    renderWithIntl(
      <main>
        <CreateBarberForm />
      </main>,
    );

    // No shop and no page address on the owner's form: the shop is the owner's own.
    expect(screen.queryByLabelText('المحل')).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/رابط صفحة الحلاق/)).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);

    await user.type(screen.getByLabelText('الاسم بالعربية'), 'سعد');
    await user.type(screen.getByLabelText('الاسم بالإنجليزية'), 'Saad');
    await user.click(screen.getByRole('button', { name: 'إضافة الحلاق' }));
    await waitFor(() =>
      expect(screen.getByLabelText(/رقم واتساب الحلاق/)).toHaveAccessibleDescription(/مطلوب/),
    );
    expect(api.POST).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText(/رقم واتساب الحلاق/), '512341001');
    await user.click(screen.getByRole('button', { name: 'إضافة الحلاق' }));

    expect(await screen.findByTestId('temporary-password-value')).toHaveTextContent('k7m3p9x2qa4b');
    expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/professionals', {
      body: {
        nameAr: 'سعد',
        nameEn: 'Saad',
        specialtyAr: null,
        specialtyEn: null,
        bioAr: null,
        bioEn: null,
        whatsAppNumber: '+966512341001',
        notificationsEnabled: true,
      },
    });
    expect(router.push).not.toHaveBeenCalled();

    // The password never reaches the URL: the barber's page opens only once the dialog is closed.
    await user.click(screen.getByRole('button', { name: 'تم' }));
    expect(router.push).toHaveBeenCalledWith('/shop/barbers/b1');
  });
});
