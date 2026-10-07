'use client';

import { type ReactNode, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { IconButton } from '@/components/ui/Button';
import { Sheet } from '@/components/ui/overlays';
import { Link, usePathname } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { AdminNotificationBell, NotificationBell } from '@/components/notifications/NotificationBell';
import { ThemeMenu } from '@/components/theme/ThemeSelector';
import { LanguageSwitcher } from './LanguageSwitcher';
import { activeHref, adminNav, shopNav, shopTabKeys, visibleItems } from './navigation';

type DashboardShellProps = {
  variant: 'shop' | 'admin';
  title: string;
  children: ReactNode;
  /** Granted permissions of the signed-in user (Phase 04). `undefined` shows every item. */
  permissions?: readonly string[];
  /** Optional card pinned to the bottom of the sidebar (e.g. the shop name and subscription status). */
  sidebarFooter?: ReactNode;
  /** Shown next to the page title (e.g. the live-updates indicator). */
  titleAddon?: ReactNode;
  /** A signed-in dashboard gets the live notification bell (D-112); previews without a session keep a plain icon. */
  notifications?: boolean;
};

/**
 * Shop and admin dashboard layout (design s-overview / a-overview):
 * ≥1200px a fixed 264px navy sidebar on the inline-start side (right in Arabic);
 * below 1200px the same navigation opens in a <Sheet> drawer (Radix Dialog: focus trap, Escape,
 * focus return — D-048). The shop also gets an app-style bottom tab bar below 1200px with its four everyday pages
 * (D-130); the drawer keeps the rest.
 */
export function DashboardShell({
  variant,
  title,
  children,
  permissions,
  sidebarFooter,
  titleAddon,
  notifications = false,
}: DashboardShellProps) {
  const t = useTranslations();
  const [drawerOpen, setDrawerOpen] = useState(false);

  const sidebar = (onNavigate?: () => void) => (
    <SidebarContent
      variant={variant}
      permissions={permissions}
      footer={sidebarFooter}
      onNavigate={onNavigate}
    />
  );

  return (
    <div className="min-h-dvh bg-bg-page lg:ps-[var(--layout-sidebar-width)]">
      <a href="#main" className="skip-link">
        {t('common.skipToContent')}
      </a>

      <aside className="fixed inset-y-0 start-0 z-40 hidden w-[var(--layout-sidebar-width)] bg-chrome lg:flex">
        {sidebar()}
      </aside>

      <header className="sticky top-0 z-20 border-b border-border-subtle bg-surface">
        <div className="flex h-16 items-center gap-3 px-4 md:px-6 lg:px-8">
          <Sheet
            open={drawerOpen}
            onOpenChange={setDrawerOpen}
            side="start"
            tone="navy"
            title={t('shell.mainNavigation')}
            hideTitle
            closeLabel={t('shell.closeMenu')}
            className="lg:hidden"
            trigger={<IconButton icon="list" label={t('shell.openMenu')} className="lg:hidden" />}
          >
            {sidebar(() => setDrawerOpen(false))}
          </Sheet>
          <h1 className="truncate text-page-title font-bold text-navy-900">{title}</h1>
          {titleAddon}
          <div className="ms-auto flex items-center gap-2">
            <LanguageSwitcher className="hidden md:inline-flex" />
            {/* Below 768px the header has no room left (title, live status, language, bell); the drawer has it. */}
            <ThemeMenu className="hidden md:block" />
            {!notifications ? (
              <IconButton icon="bell" label={t('shell.notifications')} />
            ) : variant === 'shop' ? (
              <NotificationBell scope="shop" href="/shop/notifications" />
            ) : (
              <AdminNotificationBell />
            )}
          </div>
        </div>
      </header>

      <main
        id="main"
        tabIndex={-1}
        className={cn(
          'px-4 py-6 outline-none md:px-6 lg:px-8',
          variant === 'shop' &&
            'pb-[calc(var(--layout-bottom-nav-height)+1.5rem+env(safe-area-inset-bottom))] lg:pb-6',
        )}
      >
        {children}
      </main>
      {variant === 'shop' && <ShopTabBar permissions={permissions} />}
    </div>
  );
}

/** The shop's bottom tab bar on phones and tablets (D-130): overview, hours and breaks, walk-in, appointments. */
function ShopTabBar({ permissions }: { permissions?: readonly string[] }) {
  const t = useTranslations();
  const pathname = usePathname();
  const items = shopTabKeys
    .map((key) => shopNav.find((item) => item.key === key)!)
    .filter((item) => visibleItems([item], permissions).length > 0);
  if (items.length === 0) return null;
  const current = activeHref(
    pathname,
    visibleItems(shopNav, permissions).map((i) => i.href),
  );
  return (
    <nav
      aria-label={t('shell.shopTabs')}
      className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
      data-testid="shop-tab-bar"
    >
      <ul
        className="mx-auto grid h-[var(--layout-bottom-nav-height)] max-w-[640px]"
        style={{ gridTemplateColumns: `repeat(${items.length}, minmax(0, 1fr))` }}
      >
        {items.map(({ key, href, icon: Icon }) => {
          const active = current === href;
          return (
            <li key={key} className="flex">
              <Link
                href={href}
                aria-current={active ? 'page' : undefined}
                className={cn(
                  'flex min-h-11 flex-1 flex-col items-center justify-center gap-1 px-1 text-center text-nav leading-tight transition-colors',
                  active ? 'font-bold text-navy-900' : 'font-medium text-text-secondary',
                )}
              >
                <Icon aria-hidden="true" className="size-5 shrink-0" strokeWidth={active ? 2 : 1.75} />
                <span className="line-clamp-1">{t(`nav.shop.${key}`)}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

function SidebarContent({
  variant,
  permissions,
  footer,
  onNavigate,
}: {
  variant: 'shop' | 'admin';
  permissions?: readonly string[];
  footer?: ReactNode;
  onNavigate?: () => void;
}) {
  const t = useTranslations();
  const pathname = usePathname();

  const items =
    variant === 'shop'
      ? visibleItems(shopNav, permissions).map((i) => ({ ...i, label: t(`nav.shop.${i.key}`) }))
      : visibleItems(adminNav, permissions).map((i) => ({ ...i, label: t(`nav.admin.${i.key}`) }));
  const current = activeHref(
    pathname,
    items.map((i) => i.href),
  );

  return (
    <div className="flex w-full flex-col gap-1 overflow-y-auto px-3 py-5">
      {onNavigate && (
        // Drawer only (it is the phone and tablet navigation): mirrors the drawer's close button in the other corner.
        <div className="absolute start-2 top-3 z-10 md:hidden">
          <ThemeMenu tone="onChrome" align="start" />
        </div>
      )}
      <div className="flex flex-col items-center gap-2 pb-5">
        <Logo height={36} tone="onDark" />
        <span className="font-latin text-eyebrow font-bold tracking-[0.16em] text-on-chrome-accent">
          {variant === 'shop' ? t('shell.shopEyebrow') : t('shell.adminEyebrow')}
        </span>
      </div>
      <nav aria-label={t('shell.mainNavigation')}>
        <ul className="flex flex-col gap-1">
          {items.map(({ key, href, icon: Icon, label }) => {
            const active = current === href;
            return (
              <li key={key}>
                <Link
                  href={href}
                  onClick={onNavigate}
                  aria-current={active ? 'page' : undefined}
                  className={cn(
                    'flex min-h-11 items-center gap-3 rounded-field px-3 text-label transition-colors',
                    active
                      ? 'bg-on-chrome-subtle font-bold text-on-chrome'
                      : 'font-medium text-on-chrome-muted hover:bg-on-chrome-subtle hover:text-on-chrome',
                  )}
                >
                  <Icon aria-hidden="true" className="size-[18px] shrink-0" strokeWidth={1.75} />
                  <span className="truncate">{label}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
      {footer && <div className="mt-auto pt-6">{footer}</div>}
    </div>
  );
}
