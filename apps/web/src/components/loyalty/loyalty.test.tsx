import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { LoyaltyOffer, LoyaltyProgress, LoyaltyRewardRow } from './LoyaltyParts';
import { LoyaltyStamps } from './LoyaltyStamps';

const program = {
  isActive: true,
  requiredVisits: 6,
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
