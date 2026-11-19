import {
  BarChart3,
  Bell,
  BookOpen,
  CalendarDays,
  Clock,
  CreditCard,
  FileText,
  Heart,
  Home,
  Layers,
  List,
  MessageCircle,
  Plus,
  QrCode,
  Search,
  Settings,
  Shield,
  Star,
  Store,
  Tag,
  User,
  Users,
  type LucideIcon,
} from 'lucide-react';
import type { Messages } from 'next-intl';

type NavMessageKey<S extends keyof Messages['nav']> = keyof Messages['nav'][S] & string;

/**
 * A navigation entry. `permission` is checked against the signed-in user's permissions from Phase 04 (a list means any
 * of them); the server remains the authority — hiding an item is only a convenience (spec §7).
 */
export type NavItem<S extends keyof Messages['nav']> = {
  key: NavMessageKey<S>;
  href: string;
  icon: LucideIcon;
  permission?: string | readonly string[];
};

export const customerNav: NavItem<'customer'>[] = [
  { key: 'home', href: '/discover', icon: Home },
  { key: 'explore', href: '/search', icon: Search },
  { key: 'appointments', href: '/account/bookings', icon: CalendarDays },
  { key: 'favorites', href: '/account/favorites', icon: Heart },
  { key: 'account', href: '/account', icon: User },
];

export const shopNav: NavItem<'shop'>[] = [
  { key: 'overview', href: '/shop', icon: BarChart3, permission: 'Shop.Bookings.Read' },
  { key: 'calendar', href: '/shop/calendar', icon: CalendarDays, permission: 'Shop.Bookings.Read' },
  { key: 'appointments', href: '/shop/appointments', icon: List, permission: 'Shop.Bookings.Read' },
  // The owner's reports and expected income (D-147); barbers see no figures.
  { key: 'reports', href: '/shop/reports', icon: FileText, permission: 'Shop.Insights.View' },
  // A barber's own walk-ins; the owner books none (D-146).
  { key: 'walkIn', href: '/shop/walk-in', icon: Plus, permission: 'Shop.Bookings.CreateWalkIn' },
  // The owner's hours and weekly breaks, or a barber's own breaks and the pause (D-147).
  {
    key: 'schedule',
    href: '/shop/schedule',
    icon: Clock,
    permission: ['Shop.Schedule.Manage', 'Shop.Breaks.Manage'],
  },
  // The owner's own services again (D-137; D-130 had given them to the admin only). A barber's login has no permission.
  { key: 'services', href: '/shop/services', icon: Tag, permission: 'Shop.Services.Manage' },
  // The owner adds and edits the shop's barbers and their logins (D-145, D-146).
  { key: 'barbers', href: '/shop/barbers', icon: Users, permission: 'Shop.Barbers.Manage' },
  { key: 'subscription', href: '/shop/subscription', icon: CreditCard, permission: 'Shop.Subscription.Read' },
  { key: 'qr', href: '/shop/qr', icon: QrCode, permission: 'Shop.Qr.View' },
  { key: 'notifications', href: '/shop/notifications', icon: Bell },
  { key: 'settings', href: '/shop/settings', icon: Settings, permission: 'Shop.Profile.Edit' },
  // «حسابي» for everyone in the shop (D-151): name, mobile, calendar, language, sign-out and, for a barber, deletion.
  { key: 'account', href: '/shop/account', icon: User },
];

/**
 * The shop's four everyday tabs, as a bottom bar on phones and tablets (D-130), in reading order: the operational
 * overview first (right in Arabic), then hours and breaks, walk-in for a barber or the barbers for the owner (D-146:
 * each role has one of the two), and the appointments (left in Arabic).
 */
export const shopTabKeys = ['overview', 'schedule', 'walkIn', 'barbers', 'appointments'] as const;

export const adminNav: NavItem<'admin'>[] = [
  { key: 'overview', href: '/admin', icon: BarChart3, permission: 'Admin.Dashboard.View' },
  { key: 'shops', href: '/admin/shops', icon: Store, permission: 'Admin.Shops.View' },
  { key: 'professionals', href: '/admin/professionals', icon: Users, permission: 'Admin.Professionals.View' },
  { key: 'services', href: '/admin/services', icon: Tag, permission: 'Admin.ShopServices.View' },
  { key: 'bookings', href: '/admin/bookings', icon: CalendarDays, permission: 'Admin.Bookings.View' },
  { key: 'customers', href: '/admin/customers', icon: User, permission: 'Admin.Customers.View' },
  {
    key: 'subscriptions',
    href: '/admin/subscriptions',
    icon: CreditCard,
    permission: 'Admin.Subscriptions.View',
  },
  {
    key: 'plans',
    href: '/admin/subscription-plans',
    icon: Layers,
    permission: 'SuperAdmin.SubscriptionPlans.Manage',
  },
  { key: 'reviews', href: '/admin/reviews', icon: Star, permission: 'Admin.Reviews.View' },
  { key: 'qr', href: '/admin/qr', icon: QrCode, permission: 'Admin.Qr.View' },
  {
    key: 'whatsapp',
    href: '/admin/whatsapp/templates',
    icon: MessageCircle,
    permission: 'Admin.WhatsApp.View',
  },
  { key: 'roles', href: '/admin/roles', icon: Shield, permission: 'Admin.Roles.View' },
  { key: 'audit', href: '/admin/audit', icon: BookOpen, permission: 'Admin.Audit.View' },
  { key: 'settings', href: '/admin/settings', icon: Settings, permission: 'Admin.Settings.View' },
  { key: 'legal', href: '/admin/legal', icon: FileText, permission: 'Admin.Settings.View' },
];

/** Filters items by permission. `undefined` permissions means "not yet known" (pre-Phase 04 dev shells): show all. */
export function visibleItems<S extends keyof Messages['nav']>(
  items: NavItem<S>[],
  permissions: readonly string[] | undefined,
): NavItem<S>[] {
  if (!permissions) {
    return items;
  }
  const granted = new Set(permissions);
  return items.filter(
    (item) =>
      !item.permission ||
      (typeof item.permission === 'string'
        ? granted.has(item.permission)
        : item.permission.some((p) => granted.has(p))),
  );
}

/** Longest-prefix match so `/shop/calendar` does not also activate `/shop`. */
export function activeHref(pathname: string, hrefs: readonly string[]): string | undefined {
  return [...hrefs]
    .filter((href) => pathname === href || pathname.startsWith(`${href}/`))
    .sort((a, b) => b.length - a.length)[0];
}
