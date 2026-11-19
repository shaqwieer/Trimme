import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { LoyaltyHistory } from './LoyaltyHistory';
import { LoyaltyRewardRow, LoyaltyRulesText } from './LoyaltyParts';
import { LoyaltyPointsCard } from './LoyaltyPointsCard';
import { LoyaltyStamps } from './LoyaltyStamps';

const rules = {
  firstVisitPoints: 2,
  visitPoints: 1,
  pointsForReward: 6,
  rewardType: 'FreeService' as const,
  rewardServiceId: 'svc-hair',
  rewardServiceNameAr: 'حلاقة',
  rewardServiceNameEn: 'Haircut',
  rewardAmount: null,
  inactivityRule: 'Wipe' as const,
  inactivityDays: 90,
};

const card = (points: number, extra: Partial<Record<string, unknown>> = {}) => ({
  points,
  rewardsReady: Math.floor(points / 6),
  currentCard: points % 6,
  pointsToNext: 6 - (points % 6),
  rewardsHeld: 0,
  history: [
    { at: '2026-11-10T10:00:00Z', kind: 'Earned' as const, points: 2, bookingId: 'b1', barberName: 'فيصل' },
  ],
  ...extra,
});

describe('loyalty card (D-149)', () => {
  it('fills one green circle per point and leaves the rest empty', async () => {
    const { container } = renderWithIntl(<LoyaltyStamps required={6} stamps={2} />);
    const stamps = screen.getByTestId('loyalty-stamps');
    expect(stamps).toHaveAttribute('role', 'img');
    expect(stamps.getAttribute('aria-label')).toContain('٢');
    expect(container.querySelectorAll('[data-stamp="filled"]')).toHaveLength(2);
    expect(container.querySelectorAll('[data-stamp="empty"]')).toHaveLength(4);
    await expectNoAxeViolations(container);
  });

  it('never shows more filled circles than the card has', () => {
    const { container } = renderWithIntl(<LoyaltyStamps required={3} stamps={7} />);
    expect(container.querySelectorAll('[data-stamp="filled"]')).toHaveLength(3);
    expect(container.querySelectorAll('[data-stamp="empty"]')).toHaveLength(0);
  });

  it('states the rules in words: the reward, the points per visit and what happens without visits', () => {
    renderWithIntl(<LoyaltyRulesText rules={rules} />);
    const text = screen.getByTestId('loyalty-rules');
    expect(text).toHaveTextContent('اجمع ٦ نقاط واحصل على حلاقة مجاناً.');
    expect(text).toHaveTextContent('لأول زيارة نقطتان، ولكل زيارة بعدها نقطة واحدة.');
    expect(text).toHaveTextContent('تُمسح النقاط إذا مرّ ٩٠ يوماً دون زيارة.');
  });

  it('writes an amount off as the app writes prices, and the same points per visit once', () => {
    renderWithIntl(
      <LoyaltyRulesText
        rules={{
          ...rules,
          firstVisitPoints: 1,
          rewardType: 'AmountOff',
          rewardAmount: 50,
          inactivityRule: 'None',
        }}
      />,
    );
    const text = screen.getByTestId('loyalty-rules');
    expect(text).toHaveTextContent('واحصل على خصم ٥٠ ⃁.');
    expect(text).toHaveTextContent('لكل زيارة نقطة واحدة. لا تنتهي نقاطك.');
  });

  it('a full card does not stop: the card stays full, the points beyond it show as «+٢»', async () => {
    const { container } = renderWithIntl(<LoyaltyPointsCard card={card(8)} rules={rules} />);
    expect(container.querySelectorAll('[data-stamp="filled"]')).toHaveLength(6);
    expect(screen.getByTestId('loyalty-extra')).toHaveTextContent('+٢');
    expect(screen.getByTestId('loyalty-extra')).toHaveTextContent('ونقطتان على البطاقة التالية');
    expect(screen.getByTestId('loyalty-ready')).toHaveTextContent('مكافأة جاهزة — حلاقة مجاناً');
    expect(screen.getByText('باقي ٤ نقاط للمكافأة التالية')).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('before a reward, counts what is left; a newcomer’s empty card guesses nothing', () => {
    const { unmount } = renderWithIntl(<LoyaltyPointsCard card={card(4)} rules={rules} />);
    expect(screen.queryByTestId('loyalty-extra')).not.toBeInTheDocument();
    expect(screen.queryByTestId('loyalty-ready')).not.toBeInTheDocument();
    expect(screen.getByText('باقي نقطتان للمكافأة')).toBeInTheDocument();
    unmount();

    renderWithIntl(<LoyaltyPointsCard card={card(0, { history: [] })} rules={rules} />);
    expect(screen.queryByText(/باقي/)).not.toBeInTheDocument();
  });

  it('draws a long card as a bar, and says in English what is held', () => {
    renderWithIntl(
      <LoyaltyPointsCard
        card={{ points: 30, rewardsReady: 0, currentCard: 30, pointsToNext: 70, rewardsHeld: 1, history: [] }}
        rules={{ ...rules, pointsForReward: 100 }}
      />,
      { locale: 'en' },
    );
    expect(screen.getByRole('progressbar')).toHaveAccessibleName('30 of 100 on the card');
    expect(screen.getByText('70 points to your reward')).toBeInTheDocument();
    expect(screen.getByText('1 reward held for an upcoming appointment')).toBeInTheDocument();
  });

  it('lists the history with the barber who closed each visit', async () => {
    const user = userEvent.setup();
    renderWithIntl(
      <LoyaltyHistory
        history={[
          { at: '2026-11-12T10:00:00Z', kind: 'Redeemed', points: -6, bookingId: 'b2', barberName: null },
          { at: '2026-11-10T10:00:00Z', kind: 'Earned', points: 2, bookingId: 'b1', barberName: 'فيصل' },
        ]}
      />,
    );
    await user.click(screen.getByText('سجل النقاط'));
    const items = within(screen.getByTestId('loyalty-history')).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('استخدمت مكافأة');
    expect(items[0]).toHaveTextContent('−٦');
    expect(items[1]).toHaveTextContent('زيارة مع فيصل');
    expect(items[1]).toHaveTextContent('+٢');
  });

  it('uses the English title in Turkish and names a D-132 coupon’s code', () => {
    renderWithIntl(
      <LoyaltyRewardRow
        reward={{
          id: 'r1',
          code: 'ABCD2345',
          kind: 'FreeService',
          discountPercent: null,
          titleAr: 'حلاقة مجانية',
          titleEn: 'Free haircut',
          status: 'Available',
          issuedAt: '2026-10-19T10:00:00Z',
          expiresAt: null,
          bookingId: null,
        }}
      />,
      { locale: 'tr' },
    );
    expect(screen.getByText('Free haircut')).toBeInTheDocument();
    expect(screen.getByText('ABCD2345')).toHaveAttribute('dir', 'ltr');
    expect(screen.getByText('Kullanıma hazır')).toBeInTheDocument();
  });
});
