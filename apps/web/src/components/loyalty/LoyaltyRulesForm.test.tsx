import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { LoyaltyRulesForm } from './LoyaltyRulesForm';

const api = vi.hoisted(() => ({ PUT: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

const services = [
  { id: 'svc-hair', nameAr: 'قص الشعر', nameEn: 'Haircut' },
  { id: 'svc-beard', nameAr: 'تهذيب اللحية', nameEn: 'Beard trim' },
];
const saved = { data: {}, response: new Response(null) };

beforeEach(() => api.PUT.mockReset());

describe('LoyaltyRulesForm (D-149)', () => {
  it('sends the owner’s rules: points per visit, a free service, and a card wiped after a period', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(saved);
    renderWithIntl(
      <LoyaltyRulesForm
        scope={{ kind: 'shop' }}
        rules={null}
        version={null}
        included={false}
        services={services}
        canEdit
      />,
    );

    // The plan has no loyalty: the owner can still set the rules (D-155: it comes with the plan).
    expect(screen.getByText(/باقتك الحالية لا تشمل برنامج الولاء/)).toBeInTheDocument();
    // − and + as in the app (D-151): 1 → 3 first-visit points, 6 → 10 for a reward.
    const firstUp = screen.getByRole('button', { name: 'زيادة نقاط أول زيارة' });
    await user.click(firstUp);
    await user.click(firstUp);
    expect(screen.getByRole('group', { name: 'نقاط أول زيارة' })).toHaveTextContent('٣');
    expect(screen.getByRole('button', { name: 'إنقاص نقاط كل زيارة بعدها' })).toBeDisabled();
    for (let i = 0; i < 4; i++)
      await user.click(screen.getByRole('button', { name: 'زيادة النقاط المطلوبة للمكافأة' }));
    await user.selectOptions(screen.getByLabelText('الخدمة المجانية'), 'svc-hair');
    // The app's ready periods, then what happens: two months, the card wiped.
    expect(screen.getByRole('radio', { name: 'ما تنتهي' })).toBeChecked();
    expect(screen.queryByRole('radio', { name: 'تُمسح البطاقة' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: 'شهرين' }));
    await user.click(screen.getByRole('radio', { name: 'تُمسح البطاقة' }));

    // The preview reads as the customers will.
    const preview = screen.getByTestId('loyalty-rules-preview');
    expect(preview).toHaveTextContent('اجمع ١٠ نقاط واحصل على قص الشعر مجاناً.');
    expect(preview).toHaveTextContent('لأول زيارة ٣ نقاط، ولكل زيارة بعدها نقطة واحدة.');
    expect(preview).toHaveTextContent('تُمسح النقاط إذا مرّ ٦٠ يوماً دون زيارة.');

    await user.click(screen.getByRole('button', { name: 'حفظ القواعد' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(1));
    expect(api.PUT.mock.calls[0]![0]).toBe('/api/v1/shop/loyalty');
    expect(api.PUT.mock.calls[0]![1].body).toEqual({
      firstVisitPoints: 3,
      visitPoints: 1,
      pointsForReward: 10,
      rewardType: 'FreeService',
      rewardServiceId: 'svc-hair',
      rewardAmount: null,
      inactivityRule: 'Wipe',
      inactivityDays: 60,
      version: null,
    });
  });

  it('catches an amount off outside 5–1,000 before saving, and the admin saves on the shop’s endpoint', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue(saved);
    renderWithIntl(
      <LoyaltyRulesForm
        scope={{ kind: 'admin', shopId: 's1' }}
        rules={{
          firstVisitPoints: 1,
          visitPoints: 1,
          pointsForReward: 6,
          rewardType: 'AmountOff',
          rewardServiceId: null,
          rewardServiceNameAr: null,
          rewardServiceNameEn: null,
          rewardAmount: 50,
          inactivityRule: 'DecayPerPeriod',
          inactivityDays: 30,
        }}
        version={4}
        included
        services={services}
        canEdit
      />,
    );

    // A saved 30 days is the ready «شهر», with a point taken off per period.
    expect(screen.getByRole('radio', { name: 'شهر' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'تنقص نقطة واحدة عن كل مدة بلا زيارة' })).toBeChecked();
    expect(screen.getByText(/يأتي مع الباقة: المحل ما يقدر يفعّله بنفسه/)).toBeInTheDocument();
    const amount = screen.getByLabelText('مبلغ الخصم (⃁)');
    await user.clear(amount);
    await user.type(amount, '4');
    await user.click(screen.getByRole('button', { name: 'حفظ القواعد' }));
    expect(api.PUT).not.toHaveBeenCalled();
    // The message names the range (the client's 3-7).
    expect(amount).toHaveAccessibleDescription(/لازم تكون من ٥ ⃁ إلى ١٬٠٠٠ ⃁/);

    // Arabic-Indic digits from an Arabic keypad count (the client's 3-7: «٣» was always refused).
    await user.clear(amount);
    await user.type(amount, '٢٥٫٥');
    expect(screen.getByTestId('loyalty-rules-preview')).toHaveTextContent(
      'تنقص نقطة واحدة عن كل ٣٠ يوماً تمرّ دون زيارة.',
    );
    await user.click(screen.getByRole('button', { name: 'حفظ القواعد' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(1));
    expect(api.PUT.mock.calls[0]![0]).toBe('/api/v1/admin/shops/{shopId}/loyalty');
    expect(api.PUT.mock.calls[0]![1]).toMatchObject({
      params: { path: { shopId: 's1' } },
      body: { rewardType: 'AmountOff', rewardAmount: 25.5, rewardServiceId: null, version: 4 },
    });
  });

  it('is read-only for a barber', () => {
    renderWithIntl(
      <LoyaltyRulesForm
        scope={{ kind: 'shop' }}
        rules={null}
        version={null}
        included
        services={services}
        canEdit={false}
      />,
    );
    expect(screen.getByText('يضبط صاحب المحل قواعد النقاط.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'حفظ القواعد' })).not.toBeInTheDocument();
  });
});
