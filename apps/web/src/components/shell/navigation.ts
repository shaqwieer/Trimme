import {
  BarChart3,
  Bell,
  BookOpen,
  CalendarDays,
  Clock,
  CreditCard,
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
 * A navigation entry. `permission` is checked against the signed-in user's permissions from Phase 04;
 * the server remains the authority — hiding an item is only a convenience (spec §7).
 */
export type NavItem<S extends keyof Messages['nav']> = {
  key: NavMessageKey<S>;
  href: string;
  icon: LucideIcon;
  permission?: string;
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
  { key: 'walkIn', href: '/shop/walk-in', icon: Plus, permission: 'Shop.Bookings.CreateWalkIn' },
  { key: 'schedule', href: '/shop/schedule', icon: Clock, permission: 'Shop.Schedule.Manage' },
  { key: 'services', href: '/shop/services', icon: Tag, permission: 'Shop.Services.Manage' },
  { key: 'subscription', href: '/shop/subscription', icon: CreditCard, permission: 'Shop.Subscription.Read' },
  { key: 'notifications', href: '/shop/notifications', icon: Bell },
  { key: 'settings', href: '/shop/settings', icon: Settings, permission: 'Shop.Profile.Edit' },
];

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
  return items.filter((item) => !item.permission || granted.has(item.permission));
}

/** Longest-prefix match so `/shop/calendar` does not also activate `/shop`. */
export function activeHref(pathname: string, hrefs: readonly string[]): string | undefined {
  return [...hrefs]
    .filter((href) => pathname === href || pathname.startsWith(`${href}/`))
    .sort((a, b) => b.length - a.length)[0];
}
