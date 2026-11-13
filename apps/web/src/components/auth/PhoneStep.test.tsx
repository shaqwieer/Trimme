import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { OtpFlowProvider } from './OtpFlow';
import { PhoneStep } from './PhoneStep';

const api = vi.hoisted(() => ({ POST: vi.fn() }));
const router = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn() }));

vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => router,
  Link: ({ href, children, ...props }: { href: string; children: ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

const signedIn = (profileComplete: boolean, isNewUser: boolean) => ({
  data: { isNewUser, user: { userType: 'Customer', profileComplete } },
  response: new Response(null),
});
const fail = (status: number, errorCode: string) => ({
  error: { status, errorCode, title: errorCode },
  response: new Response(null, { status }),
});

function renderStep(props: Parameters<typeof PhoneStep>[0]) {
  return renderWithIntl(
    <OtpFlowProvider>
      <PhoneStep {...props} />
    </OtpFlowProvider>,
  );
}

beforeEach(() => {
  api.POST.mockReset();
  router.replace.mockReset();
  router.push.mockReset();
});

describe('PhoneStep with codes off (the default, D-143)', () => {
  it('signs a new customer up with the number alone, then asks for their name', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce(signedIn(false, true));
    renderStep({ mode: 'signUp', returnTo: '/shops/al-asala/book', otpRequired: false });

    expect(screen.getByText('أدخل رقم جوالك لإنشاء حسابك')).toBeInTheDocument();
    expect(screen.queryByText(/واتساب/)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/رقم الجوال/), '512345678');
    await user.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));
    expect(await screen.findByText('يجب الموافقة على الشروط وسياسة الخصوصية')).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();

    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));

    await waitFor(() =>
      expect(router.replace).toHaveBeenCalledWith(
        `/auth/complete-profile?returnTo=${encodeURIComponent('/shops/al-asala/book')}`,
      ),
    );
    expect(api.POST).toHaveBeenCalledWith('/api/v1/auth/phone/sign-in', {
      body: { phone: '+966512345678', termsAccepted: true, locale: 'ar' },
    });
    expect(router.push).not.toHaveBeenCalled();
  });

  it('signs a returning customer straight in', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce(signedIn(true, false));
    renderStep({ mode: 'signIn', otpRequired: false });

    await user.type(screen.getByLabelText(/رقم الجوال/), '512345678');
    await user.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/discover'));
  });

  it('explains when the API has switched codes on since the page loaded', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce(fail(403, 'auth.otp_required'));
    renderStep({ mode: 'signIn', otpRequired: false });

    await user.type(screen.getByLabelText(/رقم الجوال/), '512345678');
    await user.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));

    expect(
      await screen.findByText('تسجيل الدخول يحتاج الآن رمز تحقق. أعد تحميل الصفحة.'),
    ).toBeInTheDocument();
    expect(router.replace).not.toHaveBeenCalled();
  });
});

describe('PhoneStep with codes on', () => {
  it('sends a code and moves to the verify step', async () => {
    const user = userEvent.setup();
    api.POST.mockResolvedValueOnce({
      data: {
        challengeId: '01990000-0000-7000-8000-000000000001',
        codeLength: 6,
        expiresAt: '2026-11-13T10:05:00Z',
        resendAvailableAt: '2026-11-13T10:00:30Z',
      },
      response: new Response(null, { status: 202 }),
    });
    renderStep({ mode: 'signIn', otpRequired: true });

    expect(screen.getByText('أدخل رقم جوالك وسنرسل لك رمز الدخول عبر واتساب')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/رقم الجوال/), '512345678');
    await user.click(screen.getByRole('button', { name: 'إرسال الرمز' }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/auth/verify'));
    expect(api.POST).toHaveBeenCalledWith('/api/v1/auth/otp/request', {
      body: { phone: '+966512345678', termsAccepted: true, locale: 'ar' },
    });
    expect(router.replace).not.toHaveBeenCalled();
  });
});
