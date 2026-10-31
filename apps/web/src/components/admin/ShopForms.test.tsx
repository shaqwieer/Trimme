import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { generateTemporaryPassword, ShopAccountForms } from './ShopForms';

const api = vi.hoisted(() => ({ POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

beforeEach(() => api.POST.mockReset());

describe('ShopAccountForms (D-139)', () => {
  it('creates an owner with a temporary password and shows it once', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue({
      data: { id: 'u1', email: 'owner@shop.sa', displayName: 'سالم', role: 'ShopOwner' },
      response: new Response(null, { status: 201 }),
    });
    renderWithIntl(<ShopAccountForms shopId="s1" />);

    // The email invitation stays the first choice.
    expect(screen.getByRole('tab', { name: 'دعوة بالبريد' })).toHaveAttribute('aria-selected', 'true');
    await user.click(screen.getByRole('tab', { name: 'إنشاء يدوي' }));

    await user.type(screen.getByLabelText('الاسم'), 'سالم');
    await user.type(screen.getByLabelText('البريد الإلكتروني'), 'owner@shop.sa');
    await user.type(screen.getByLabelText('كلمة المرور المؤقتة'), 'short');
    await user.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));
    expect(api.POST).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'توليد كلمة مرور' }));
    const generated = (screen.getByLabelText('كلمة المرور المؤقتة') as HTMLInputElement).value;
    expect(generated).toMatch(/^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$/);
    await user.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));

    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST).toHaveBeenCalledWith('/api/v1/admin/shops/{shopId}/users', {
      params: { path: { shopId: 's1' } },
      body: {
        displayName: 'سالم',
        email: 'owner@shop.sa',
        role: 'ShopOwner',
        password: generated,
        locale: 'ar',
      },
    });
    expect(await screen.findByText('تم إنشاء الحساب.')).toBeInTheDocument();
    expect(screen.getByText(generated)).toBeInTheDocument();
    expect(screen.getByLabelText('كلمة المرور المؤقتة')).toHaveValue('');
  });

  it('says when the email already has an account', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValue({
      error: { status: 409, errorCode: 'invitation.email_registered', title: 'Conflict' },
      response: new Response(null, { status: 409 }),
    });
    renderWithIntl(<ShopAccountForms shopId="s1" />);
    await user.click(screen.getByRole('tab', { name: 'إنشاء يدوي' }));

    await user.type(screen.getByLabelText('الاسم'), 'Desk');
    await user.type(screen.getByLabelText('البريد الإلكتروني'), 'desk@shop.sa');
    await user.click(screen.getByLabelText(/موظف استقبال/));
    await user.type(screen.getByLabelText('كلمة المرور المؤقتة'), 'temporary pass');
    await user.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(api.POST.mock.calls[0]![1].body).toMatchObject({ role: 'ShopStaff' });
  });
});

describe('generateTemporaryPassword', () => {
  it('gives twelve unambiguous characters in three groups', () => {
    const passwords = new Set(Array.from({ length: 50 }, generateTemporaryPassword));
    expect(passwords.size).toBe(50);
    for (const password of passwords)
      expect(password).toMatch(/^[a-hjkmnp-z2-9]{4}(-[a-hjkmnp-z2-9]{4}){2}$/);
  });
});
