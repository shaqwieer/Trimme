'use client';

import { type ReactNode, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { IconButton } from '@/components/ui/Button';
import { Sheet } from '@/components/ui/overlays';
import { Link, usePathname } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { LanguageSwitcher } from './LanguageSwitcher';
import { activeHref, adminNav, shopNav, visibleItems } from './navigation';

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
 * below 1200px the same navigation opens in a <Sheet> drawer (Radix Dialog: focus trap, Escape,
 * focus return — D-048).
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

      <aside className="fixed inset-y-0 start-0 z-40 hidden w-[var(--layout-sidebar-width)] bg-navy-900 lg:flex">
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
          <div className="ms-auto flex items-center gap-2">
            <LanguageSwitcher className="hidden md:inline-flex" />
            <IconButton icon="bell" label={t('shell.notifications')} />
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
