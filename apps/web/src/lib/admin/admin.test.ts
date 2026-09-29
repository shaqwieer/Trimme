import { describe, expect, it } from 'vitest';
import {
  auditEntityHref,
  auditTone,
  deltaTone,
  overviewDays,
  percentChange,
  permissionGroups,
  permissionKey,
  pointChange,
  share,
  startOfLocalDay,
  ungrantable,
} from './admin';

describe('admin overview helpers', () => {
  it('colours a change by meaning, not by sign', () => {
    expect(deltaTone(12, 10, true)).toBe('good');
    expect(deltaTone(8, 10, true)).toBe('bad');
    expect(deltaTone(7.4, 6.8, false)).toBe('bad'); // a rising cancellation rate is bad
    expect(deltaTone(5, 6, false)).toBe('good');
    expect(deltaTone(3, 3, true)).toBe('neutral');
  });

  it('gives no percentage without a previous figure, and rate changes in points', () => {
    expect(percentChange(12, 10)).toBe(20);
    expect(percentChange(5, 0)).toBeNull();
    expect(pointChange(16.7, 17.3)).toBe(-0.6);
  });

  it('keeps a visible sliver for small bars and accepts only the offered periods', () => {
    expect(share(1, 400)).toBe(2);
    expect(share(0, 0)).toBe(0);
    expect(share(50, 100)).toBe(50);
    expect(overviewDays('7')).toBe(7);
    expect(overviewDays('3')).toBe(1);
    expect(overviewDays(undefined)).toBe(1);
  });
});

describe('platform calendar days', () => {
  it('starts a Riyadh day at 00:00+03:00, the instant the API filters by', () => {
    expect(startOfLocalDay('2026-10-01')).toBe('2026-10-01T00:00:00+03:00');
    expect(new Date(startOfLocalDay('2026-10-01')).toISOString()).toBe('2026-09-30T21:00:00.000Z');
  });

  it('follows a zone with daylight saving', () => {
    expect(startOfLocalDay('2026-07-01', 'America/New_York')).toBe('2026-07-01T00:00:00-04:00');
    expect(startOfLocalDay('2026-12-01', 'America/New_York')).toBe('2026-12-01T00:00:00-05:00');
  });
});

describe('audit helpers', () => {
  it('links entities that have an admin page, and nothing else', () => {
    expect(auditEntityHref('Booking', 'b1')).toBe('/admin/bookings/b1');
    expect(auditEntityHref('Customer', 'c1')).toBe('/admin/customers/c1');
    expect(auditEntityHref('Role', 'r1')).toBe('/admin/roles/r1');
    expect(auditEntityHref('Invitation', 'i1')).toBeUndefined();
  });

  it('colours actions by family', () => {
    expect(auditTone('review.hidden')).toBe('danger');
    expect(auditTone('customer.contact_revealed')).toBe('neutral');
    expect(auditTone('subscription.renewed')).toBe('success');
    expect(auditTone('role.permissions_changed')).toBe('warning');
    expect(auditTone('shop.created')).toBe('brand');
  });
});

describe('role helpers', () => {
  it('groups permissions by area and keeps SuperAdmin ones apart', () => {
    const groups = permissionGroups([
      'Admin.Shops.View',
      'Admin.Shops.Create',
      'Admin.Audit.View',
      'SuperAdmin.SubscriptionPlans.Manage',
    ]);
    expect(groups).toEqual([
      { area: 'Shops', codes: ['Admin.Shops.View', 'Admin.Shops.Create'] },
      { area: 'Audit', codes: ['Admin.Audit.View'] },
      { area: 'SuperAdmin', codes: ['SuperAdmin.SubscriptionPlans.Manage'] },
    ]);
    expect(permissionKey('Admin.WhatsApp.Templates.Edit')).toBe('Admin_WhatsApp_Templates_Edit');
  });

  it('flags only new permissions the admin does not hold (the API refuses them)', () => {
    expect(
      ungrantable(
        ['Admin.Audit.View', 'Admin.Settings.Edit', 'Admin.Shops.View'],
        ['Admin.Shops.View'],
        ['Admin.Audit.View'],
      ),
    ).toEqual(['Admin.Settings.Edit']);
  });
});
