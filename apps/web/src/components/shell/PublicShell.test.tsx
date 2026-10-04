import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { PublicShell } from './PublicShell';

describe('PublicShell landing navigation', () => {
  it('keeps customer discovery primary; the short home page has no sections to jump to (D-126)', () => {
    renderWithIntl(
      <PublicShell variant="landing">
        <p>Landing content</p>
      </PublicShell>,
      { locale: 'en' },
    );

    const navigation = screen.getByRole('navigation', { name: 'Main navigation' });
    expect(within(navigation).getByRole('link', { name: 'Shops' })).toHaveAttribute('href', '/en/shops');
    expect(within(navigation).getByRole('link', { name: 'Nearby' })).toHaveAttribute('href', '/en/discover');
    expect(within(navigation).queryByRole('link', { name: 'How it works' })).not.toBeInTheDocument();
    expect(within(navigation).queryByRole('link', { name: 'For shop owners' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Find a barber' })).toHaveAttribute('href', '/en/shops');
  });

  it('does not show landing-only navigation on standard marketing pages', () => {
    renderWithIntl(
      <PublicShell variant="marketing">
        <p>Shop content</p>
      </PublicShell>,
      { locale: 'en' },
    );

    expect(screen.queryByRole('link', { name: 'For shop owners' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Find a barber' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Nearby' })).toHaveAttribute('href', '/en/discover');
  });
});
