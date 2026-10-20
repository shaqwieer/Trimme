import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { LoyaltyOffer, LoyaltyProgress, LoyaltyRewardRow } from './LoyaltyParts';
import { LoyaltyStamps } from './LoyaltyStamps';

const program = {
  isActive: true,
  requiredVisits: 6,
  firstVisitStamps: 1,
  rewardKind: 'FreeService' as const,
  discountPercent: null,
  rewardTitleAr: 'حلاقة مجانية',
  rewardTitleEn: 'Free haircut',
  rewardValidityDays: null,
};

describe('loyalty card (D-132)', () => {
  it('fills one green circle per completed visit and leaves the rest empty', async () => {
    const { container } = renderWithIntl(<LoyaltyStamps required={6} stamps={2} />);
    const card = screen.getByTestId('loyalty-stamps');
    expect(card).toHaveAttribute('role', 'img');
    expect(card.getAttribute('aria-label')).toContain('2');
    expect(container.querySelectorAll('[data-stamp="filled"]')).toHaveLength(2);
    expect(container.querySelectorAll('[data-stamp="empty"]')).toHaveLength(4);
    await expectNoAxeViolations(container);
  });

  it('never shows more filled circles than the card has', () => {
    const { container } = renderWithIntl(<LoyaltyStamps required={3} stamps={7} />);
    expect(container.querySelectorAll('[data-stamp="filled"]')).toHaveLength(3);
    expect(container.querySelectorAll('[data-stamp="empty"]')).toHaveLength(0);
  });

  it('states the offer and what is left, in each language', () => {
    renderWithIntl(
      <>
        <LoyaltyOffer program={program} />
        <LoyaltyProgress program={program} stamps={4} />
      </>,
    );
    expect(screen.getByText(/حلاقة مجانية/)).toBeInTheDocument();
    expect(screen.getByText(/باقي زيارتان/)).toBeInTheDocument();
    expect(screen.queryByTestId('loyalty-first-visit')).not.toBeInTheDocument();
  });

  it('with a first-visit bonus, counts circles and says what the first visit fills (D-133)', () => {
    const bonus = { ...program, firstVisitStamps: 3 };
    const { unmount } = renderWithIntl(
      <>
        <LoyaltyOffer program={bonus} />
        <LoyaltyProgress program={bonus} stamps={0} />
      </>,
    );
    expect(screen.getByText(/^اجمع .* دوائر واحصل على: حلاقة مجانية$/)).toBeInTheDocument();
    expect(screen.getByTestId('loyalty-first-visit')).toHaveTextContent(
      /أول زيارة لك تملأ .* دوائر، ثم دائرة مع كل زيارة/,
    );
    // An empty card may be a newcomer's, whose first visit fills three: no visit count is guessed.
    expect(screen.queryByText(/باقي/)).not.toBeInTheDocument();
    unmount();

    renderWithIntl(<LoyaltyProgress program={bonus} stamps={3} />, { locale: 'en' });
    expect(screen.getByText('3 more visits to your coupon')).toBeInTheDocument();
    expect(screen.getByRole('img')).toHaveAccessibleName('3 of 6 circles filled');
  });

  it('uses the English title in Turkish and names the coupon code', () => {
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
