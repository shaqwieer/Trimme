import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { renderWithIntl, setPathname } from '@/test/render';
import { DashboardShell } from './DashboardShell';

describe('DashboardShell', () => {
  beforeEach(() => setPathname('/ar/shop/calendar'));

  it('marks the current page in the sidebar and localizes labels', () => {
    renderWithIntl(
      <DashboardShell variant="shop" title="التقويم">
        <p>content</p>
      </DashboardShell>,
    );

    const current = screen.getAllByRole('link', { current: 'page' });
    expect(current).toHaveLength(1);
    expect(current[0]).toHaveTextContent('التقويم');
    expect(screen.getByRole('heading', { level: 1, name: 'التقويم' })).toBeInTheDocument();
  });

  it('opens an accessible navigation drawer, traps focus and closes on Escape', async () => {
    const user = userEvent.setup();
    renderWithIntl(
      <DashboardShell variant="admin" title="Overview">
        <p>content</p>
      </DashboardShell>,
      { locale: 'en' },
    );

    const menuButton = screen.getByRole('button', { name: 'Open menu' });
    expect(menuButton).toHaveAttribute('aria-expanded', 'false');

    await user.click(menuButton);
    const drawer = screen.getByRole('dialog', { name: 'Main navigation' });
    expect(drawer).toHaveAttribute('aria-modal', 'true');
    expect(menuButton).toHaveAttribute('aria-expanded', 'true');
    expect(drawer).toContainElement(document.activeElement as HTMLElement);
    expect(within(drawer).getByRole('link', { name: 'Shops' })).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(menuButton).toHaveFocus();
  });

  it('hides items the user has no permission for', () => {
    renderWithIntl(
      <DashboardShell
        variant="admin"
        title="Overview"
        permissions={['Admin.Dashboard.View', 'Admin.Bookings.View']}
      >
        <p>content</p>
      </DashboardShell>,
      { locale: 'en' },
    );

    expect(screen.getByRole('link', { name: 'Bookings' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Subscription plans' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Shops' })).not.toBeInTheDocument();
  });
});
