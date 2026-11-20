import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { ShopCalendar } from './ShopCalendar';
import { WalkInChooser } from './WalkInChooser';

const api = vi.hoisted(() => ({ GET: vi.fn(() => new Promise(() => {})), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

describe('WalkInChooser (D-158)', () => {
  it('opens on the four choices with nothing below until one is picked', async () => {
    const { container } = renderWithIntl(<WalkInChooser />);
    const choices = screen.getByRole('navigation', { name: 'ماذا تسجّل؟' });
    expect(
      within(choices)
        .getAllByRole('link')
        .map((link) => [link.textContent, link.getAttribute('href')]),
    ).toEqual([
      ['زبون حضوري', '/ar/shop/walk-in?mode=walkIn'],
      ['حجز بالتلفون', '/ar/shop/walk-in?mode=phone'],
      ['بريك', '/ar/shop/walk-in?mode=break'],
      ['غير متوفر اليوم', '/ar/shop/walk-in?mode=unavailable'],
    ]);
    expect(within(choices).queryByRole('link', { current: 'page' })).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('اختر واحداً من الأربعة فوق');
    await expectNoAxeViolations(container);
  });

  it('marks the chosen one and keeps the others to switch to', () => {
    renderWithIntl(<WalkInChooser current="phone" />);
    const choices = screen.getByRole('navigation', { name: 'ماذا تسجّل؟' });
    expect(within(choices).getByRole('link', { current: 'page' })).toHaveTextContent('حجز بالتلفون');
    expect(within(choices).getAllByRole('link')).toHaveLength(4);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('ShopCalendar legend (D-158)', () => {
  it('has red for a customer who did not come', () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    renderWithIntl(
      <QueryClientProvider client={client}>
        <ShopCalendar today="2026-10-21" timeZone="Asia/Riyadh" canWalkIn canUpdate />
      </QueryClientProvider>,
    );
    const legend = screen.getByRole('list', { name: 'دليل الألوان' });
    const noShow = within(legend).getByText('ما حضر').closest('li')!;
    expect(noShow.querySelector('span')).toHaveClass('bg-status-cancelled-bg', 'border-status-cancelled-dot');
  });
});
