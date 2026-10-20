import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { LoyaltyProgramForm } from './LoyaltyProgramForm';

const api = vi.hoisted(() => ({ PUT: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

const stats = { members: 0, openStamps: 0, rewardsIssued: 0, rewardsAvailable: 0, rewardsRedeemed: 0 };

beforeEach(() => api.PUT.mockReset());

describe('LoyaltyProgramForm (D-132, D-133)', () => {
  it('sends the first-visit points, which must stay below the visits on the card', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: { program: null, version: 1, stats }, response: new Response(null) });
    renderWithIntl(
      <LoyaltyProgramForm shopId="s1" loyalty={{ program: null, version: null, stats }} canEdit />,
    );

    const first = screen.getByLabelText('نقاط أول زيارة');
    expect(first).toHaveValue('1');
    // Six circles on the card: the first visit fills one to five of them.
    expect(first).toHaveAccessibleDescription(/من ١ إلى ٥|من 1 إلى 5/);
    await user.type(screen.getByLabelText('اسم المكافأة بالعربية'), 'حلاقة مجانية');
    await user.clear(first);
    await user.type(first, '6');
    await user.click(screen.getByRole('button', { name: 'حفظ البرنامج' }));
    expect(api.PUT).not.toHaveBeenCalled();
    expect(first).toHaveAccessibleDescription('هذه القيمة خارج النطاق المسموح');

    await user.clear(first);
    await user.type(first, '3');
    await user.click(screen.getByRole('button', { name: 'حفظ البرنامج' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(1));
    expect(api.PUT.mock.calls[0]![1].body).toMatchObject({ requiredVisits: 6, firstVisitStamps: 3 });
  });
});
