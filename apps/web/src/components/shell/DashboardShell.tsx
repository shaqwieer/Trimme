'use client';

import { type ReactNode, useCallback, useId, useRef, useState } from 'react';
import { Bell, Menu, X } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { Link, usePathname } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { LanguageSwitcher } from './LanguageSwitcher';
import { activeHref, adminNav, shopNav, visibleItems } from './navigation';
import { useFocusTrap } from './useFocusTrap';

type DashboardShellProps = {
  variant: 'shop' | 'admin';
  title: string;
  children: ReactNode;
  /** Granted permissions of the signed-in user (Phase 04). `undefined` shows every item. */
  permissions?: readonly string[];
  /** Optional card pinned to the bottom of the sidebar (e.g. the shop name and subscription status). */
  sidebarFooter?: ReactNode;
};

/**
 * Shop and admin dashboard layout (design s-overview / a-overview):
 * ≥1200px a fixed 264px navy sidebar on the inline-start side (right in Arabic);
 * below 1200px the same navigation opens in a focus-trapped drawer.
 */
export function DashboardShell({
  variant,
  title,
  children,
  permissions,
  sidebarFooter,
}: DashboardShellProps) {
  const t = useTranslations();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const drawerRef = useRef<HTMLDivElement>(null);
  const drawerId = useId();
  const closeDrawer = useCallback(() => setDrawerOpen(false), []);
  useFocusTrap(drawerRef, drawerOpen, closeDrawer);

  const sidebar = (
    <SidebarContent
      variant={variant}
      permissions={permissions}
      footer={sidebarFooter}
      onNavigate={closeDrawer}
    />
  );

  return (
    <div className="min-h-dvh bg-bg-page lg:ps-[var(--layout-sidebar-width)]">
      <a href="#main" className="skip-link">
        {t('common.skipToContent')}
      </a>

      <aside className="fixed inset-y-0 start-0 z-40 hidden w-[var(--layout-sidebar-width)] bg-navy-900 lg:flex">
        {sidebar}
      </aside>

      {drawerOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div aria-hidden="true" className="absolute inset-0 bg-overlay" onClick={closeDrawer} />
          <div
            ref={drawerRef}
            id={drawerId}
            role="dialog"
            aria-modal="true"
            aria-label={t('shell.mainNavigation')}
            className="absolute inset-y-0 start-0 flex w-[min(var(--layout-sidebar-width),85vw)] bg-navy-900 shadow-e3"
          >
            <button
              type="button"
              onClick={closeDrawer}
              aria-label={t('shell.closeMenu')}
              className="absolute end-2 top-3 inline-flex size-11 items-center justify-center rounded-button text-on-navy-muted hover:bg-on-navy-subtle hover:text-on-navy"
            >
              <X aria-hidden="true" className="size-5" strokeWidth={1.75} />
            </button>
            {sidebar}
          </div>
        </div>
      )}

      <header className="sticky top-0 z-20 border-b border-border-subtle bg-surface">
        <div className="flex h-16 items-center gap-3 px-4 md:px-6 lg:px-8">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            aria-label={t('shell.openMenu')}
            aria-expanded={drawerOpen}
            aria-controls={drawerId}
            className="inline-flex size-11 items-center justify-center rounded-button text-text-strong hover:bg-brand-100 lg:hidden"
          >
            <Menu aria-hidden="true" className="size-5" strokeWidth={1.75} />
          </button>
          <h1 className="truncate text-page-title font-bold text-navy-900">{title}</h1>
          <div className="ms-auto flex items-center gap-2">
            <LanguageSwitcher className="hidden md:inline-flex" />
            <button
              type="button"
              aria-label={t('shell.notifications')}
              className="inline-flex size-11 items-center justify-center rounded-button text-text-strong hover:bg-brand-100"
            >
              <Bell aria-hidden="true" className="size-5" strokeWidth={1.75} />
            </button>
          </div>
        </div>
      </header>

      <main id="main" tabIndex={-1} className="px-4 py-6 outline-none md:px-6 lg:px-8">
        {children}
      </main>
    </div>
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
  onNavigate: () => void;
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
      <div className="flex flex-col items-center gap-2 pb-5">
        <Logo height={36} tone="onDark" />
        <span className="font-latin text-eyebrow font-bold tracking-[0.16em] text-on-navy-accent">
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
                      ? 'bg-on-navy-subtle font-bold text-on-navy'
                      : 'font-medium text-on-navy-muted hover:bg-on-navy-subtle hover:text-on-navy',
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
