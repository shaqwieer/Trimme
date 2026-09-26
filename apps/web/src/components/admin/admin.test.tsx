import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { adminNav, visibleItems } from '@/components/shell/navigation';
import { renderWithIntl } from '@/test/render';
import { isShopStatus, ShopStatusBadge } from './ShopStatusBadge';

describe('ShopStatusBadge', () => {
  it.each([
    ['ar', 'Draft', 'مسودة'],
    ['ar', 'Suspended', 'موقوف'],
    ['en', 'Active', 'Active'],
  ] as const)('labels %s %s as %s (text, not colour alone)', (locale, status, label) => {
    renderWithIntl(<ShopStatusBadge status={status} />, { locale });
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('only accepts the API statuses', () => {
    expect(isShopStatus('Active')).toBe(true);
    expect(isShopStatus('Deleted')).toBe(false);
  });
});

describe('admin navigation from real permissions (R-WEB-13)', () => {
  it('shows Shops to an admin with Admin.Shops.View and hides plans without the SuperAdmin permission', () => {
    const keys = visibleItems(adminNav, ['Admin.Dashboard.View', 'Admin.Shops.View']).map((i) => i.key);
    expect(keys).toEqual(['overview', 'shops']);
  });
});
