import { describe, expect, it } from 'vitest';
import ar from '../../../messages/ar.json';
import { activeHref, adminNav, customerNav, shopNav, visibleItems } from './navigation';

describe('navigation config', () => {
  it('matches the longest prefix so nested pages do not activate their parent', () => {
    const hrefs = shopNav.map((item) => item.href);
    expect(activeHref('/shop', hrefs)).toBe('/shop');
    expect(activeHref('/shop/calendar', hrefs)).toBe('/shop/calendar');
    expect(activeHref('/shop/services/123', hrefs)).toBe('/shop/services');
    expect(activeHref('/shopping', hrefs)).toBeUndefined();
  });

  it('nav_hides_items_without_permission (R-WEB-13)', () => {
    const visible = visibleItems(adminNav, ['Admin.Dashboard.View', 'Admin.Shops.View']);
    expect(visible.map((i) => i.key)).toEqual(['overview', 'shops']);
    expect(visibleItems(adminNav, undefined)).toHaveLength(adminNav.length);
  });

  it('keeps subscription-plan management behind the SuperAdmin permission', () => {
    const plans = adminNav.find((i) => i.key === 'plans');
    expect(plans?.permission).toBe('SuperAdmin.SubscriptionPlans.Manage');
    expect(visibleItems(adminNav, ['Admin.Subscriptions.View']).some((i) => i.key === 'plans')).toBe(false);
  });

  it('has no barber-transfer entry anywhere (R-NEG-01, DV-S01)', () => {
    const everything = [...customerNav, ...shopNav, ...adminNav];
    expect(everything.some((i) => /transfer/i.test(i.key) || /transfer/i.test(i.href))).toBe(false);
    expect(JSON.stringify(ar)).not.toMatch(/نقل حلاق|تنفيذ النقل|والنقل/);
  });

  it('uses five customer destinations (design bottom bar)', () => {
    expect(customerNav).toHaveLength(5);
  });
});
