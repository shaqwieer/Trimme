import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { Logo, LOGO_HEIGHT, LOGO_WIDTH } from './Logo';

describe('Logo_renders_with_intrinsic_ratio (R-WEB-03)', () => {
  it('keeps the intrinsic aspect ratio at any height (never stretched)', () => {
    renderWithIntl(<Logo height={36} />);
    const img = screen.getByRole('img', { name: 'تريمي' });

    const width = Number(img.getAttribute('width'));
    const height = Number(img.getAttribute('height'));
    expect(height).toBe(36);
    expect(Math.abs(width / height - LOGO_WIDTH / LOGO_HEIGHT)).toBeLessThan(0.03);
    expect(img.getAttribute('src')).toContain('trimme-logo.png');
  });

  it('uses the localized alt text', () => {
    renderWithIntl(<Logo />, { locale: 'en' });
    expect(screen.getByRole('img', { name: 'TRIMME' })).toBeInTheDocument();
  });

  it('applies the design lightening only on dark surfaces', () => {
    renderWithIntl(<Logo tone="onDark" />);
    expect(screen.getByRole('img').getAttribute('data-tone')).toBe('onDark');
  });
});
