/**
 * Pure helpers of the admin operations screens (Phase 14). The API computes every figure (D-101); these only format,
 * compare and link what it returns.
 */
import { OPERATING_TIME_ZONE } from '@/lib/i18n/config';

/* ---------------------------------------------------------------- overview */

export type DeltaTone = 'good' | 'bad' | 'neutral';

/**
 * The tone of a change: a rise is good for counts such as bookings or new customers (`higherIsBetter`) and bad for
 * rates such as cancellations and no-shows. No change, or nothing to compare with, is neutral.
 */
export function deltaTone(current: number, previous: number, higherIsBetter: boolean): DeltaTone {
  if (current === previous) return 'neutral';
  return current > previous === higherIsBetter ? 'good' : 'bad';
}

/** Percentage change of a count, rounded; null when there is no previous value to compare with. */
export function percentChange(current: number, previous: number): number | null {
  if (previous === 0) return null;
  return Math.round(((current - previous) / previous) * 100);
}

/** Difference of two rates in percentage points, one decimal. */
export function pointChange(current: number, previous: number): number {
  return Math.round((current - previous) * 10) / 10;
}

/** Width (0–100) of a bar relative to the largest value of its list. */
export function share(value: number, max: number): number {
  if (max <= 0) return 0;
  return Math.max(2, Math.round((value / max) * 100));
}

export const OVERVIEW_DAYS = [1, 7, 30] as const;
export type OverviewDays = (typeof OVERVIEW_DAYS)[number];

export function overviewDays(value: string | undefined): OverviewDays {
  const days = Number(value);
  return (OVERVIEW_DAYS as readonly number[]).includes(days) ? (days as OverviewDays) : 1;
}

/* ---------------------------------------------------------------- dates */

/**
 * The instant a local calendar day starts in a time zone, as ISO 8601 with its offset (for API filters that take
 * instants). `YYYY-MM-DD` in, e.g. `2026-10-01T00:00:00+03:00` out for Riyadh.
 */
export function startOfLocalDay(date: string, timeZone = OPERATING_TIME_ZONE): string {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number];
  const guess = Date.UTC(year, month - 1, day);
  const offset = zoneOffsetMinutes(guess, timeZone);
  const sign = offset >= 0 ? '+' : '-';
  const abs = Math.abs(offset);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date}T00:00:00${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`;
}

/** The zone's offset from UTC in minutes at an instant. */
export function zoneOffsetMinutes(instant: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).formatToParts(new Date(instant));
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value);
  const local = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'));
  return Math.round((local - instant) / 60_000);
}

/** A `YYYY-MM-DD` value, or undefined for anything else (URL filters). */
export function localDateParam(value: string | undefined): string | undefined {
  return value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : undefined;
}

/* ---------------------------------------------------------------- audit */

/** Where an audited entity lives in the admin dashboard, when it has a page there. */
export function auditEntityHref(entityType: string, entityId: string): string | undefined {
  switch (entityType) {
    case 'Shop':
      return `/admin/shops/${entityId}`;
    case 'Professional':
      return `/admin/professionals/${entityId}`;
    case 'Booking':
      return `/admin/bookings/${entityId}`;
    case 'Customer':
      return `/admin/customers/${entityId}`;
    case 'ShopService':
      return `/admin/services/${entityId}`;
    case 'SubscriptionPlan':
      return `/admin/subscription-plans/${entityId}`;
    case 'ShopSubscription':
      return `/admin/shops/${entityId}?tab=subscription`;
    case 'Review':
      return `/admin/reviews?review=${entityId}`;
    case 'PlatformSettings':
      return '/admin/settings';
    case 'Role':
      return `/admin/roles/${entityId}`;
    default:
      return undefined;
  }
}

/** The audit colour family of an action (design a-roles timeline: blue, amber, green, grey, red). */
export function auditTone(action: string): 'brand' | 'warning' | 'success' | 'neutral' | 'danger' {
  if (/hidden|suspend|cancel|disabled|deleted|flag/.test(action)) return 'danger';
  if (/revealed|contact|whatsapp/.test(action)) return 'neutral';
  if (/subscription|plan/.test(action)) return 'success';
  if (/override|price|settings|permissions|roles/.test(action)) return 'warning';
  return 'brand';
}

/* ---------------------------------------------------------------- roles */

/** A permission code as a message key (next-intl nests on dots): `Admin.Shops.View` → `Admin_Shops_View`. */
export function permissionKey(code: string): string {
  return code.replace(/\./g, '_');
}

/** Admin permissions grouped by their area (`Admin.Shops.View` → `Shops`), groups and codes in catalogue order. */
export function permissionGroups(codes: readonly string[]): Array<{ area: string; codes: string[] }> {
  const groups = new Map<string, string[]>();
  for (const code of codes) {
    const [scope, area = code] = code.split('.');
    const key = scope === 'SuperAdmin' ? 'SuperAdmin' : area;
    groups.set(key, [...(groups.get(key) ?? []), code]);
  }
  return [...groups.entries()].map(([area, list]) => ({ area, codes: list }));
}

/** The permissions the caller could not grant (they do not hold them); the API refuses these with 403 `role.escalation`. */
export function ungrantable(
  wanted: readonly string[],
  current: readonly string[],
  held: readonly string[],
): string[] {
  const has = new Set(held);
  const before = new Set(current);
  return wanted.filter((code) => !before.has(code) && !has.has(code));
}

/** Seed roles keep their names and cannot be deleted; managed ones cannot be edited at all (D-051, D-106). */
export const SEED_ROLES = [
  'SuperAdmin',
  'OperationsManager',
  'Support',
  'ShopOwner',
  'ShopStaff',
  'Customer',
] as const;

export function isSeedRole(name: string): boolean {
  return (SEED_ROLES as readonly string[]).includes(name);
}
