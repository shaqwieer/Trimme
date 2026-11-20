import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { CompleteProfileForm } from './CompleteProfileForm';

const api = vi.hoisted(() => ({ POST: vi.fn() }));
const router = vi.hoisted(() => ({ replace: vi.fn() }));

vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router }));

const ok = { data: {}, response: new Response(null) };

beforeEach(() => {
  api.POST.mockReset();
  router.replace.mockReset();
  document.cookie = 'trimme-terms=; Max-Age=0; Path=/';
});

describe('CompleteProfileForm (DV-A01, D-158)', () => {
  it('says the terms were accepted on sign-up instead of asking again', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce(ok);
    renderWithIntl(<CompleteProfileForm returnTo="/discover" termsAccepted />);

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.getByTestId('terms-accepted')).toHaveTextContent(
      'وافقت على شروط الاستخدام وسياسة الخصوصية عند التسجيل.',
    );
    await user.type(screen.getByLabelText(/الاسم/), 'سعود');
    await user.click(screen.getByRole('button', { name: 'حفظ ومتابعة' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/discover', { locale: 'ar' }));
    expect(api.POST).toHaveBeenCalledWith('/api/v1/auth/profile/complete', {
      body: { displayName: 'سعود', preferredLocale: 'ar', termsAccepted: true },
    });
    // This browser remembers it for the next sign-up.
    expect(document.cookie).toContain('trimme-terms=1');
  });

  it('still asks a customer who has not accepted them', async () => {
    const user = userEvent.setup();
    renderWithIntl(<CompleteProfileForm returnTo="/discover" />);

    await user.type(screen.getByLabelText(/الاسم/), 'سعود');
    await user.click(screen.getByRole('button', { name: 'حفظ ومتابعة' }));
    expect(await screen.findByText('يجب الموافقة على الشروط وسياسة الخصوصية')).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();
    expect(screen.getByRole('checkbox')).not.toBeChecked();
  });
});
