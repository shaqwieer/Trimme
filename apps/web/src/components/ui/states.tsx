import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { ButtonLink } from './Button';
import { type DesignIconName, Icon } from './icons';

type Tone = 'flat' | 'warn' | 'bad';

const iconTile: Record<Tone, string> = {
  flat: 'bg-bg-subtle text-text-tertiary',
  warn: 'bg-warning-50 text-warning-700',
  bad: 'bg-danger-50 text-danger-500',
};

type StateCardProps = {
  icon: DesignIconName;
  title: ReactNode;
  body?: ReactNode;
  /** Primary action (a <ButtonLink> or client <Button>). */
  action?: ReactNode;
  tone?: Tone;
  /** Use role="alert" for errors that appear after an action. */
  live?: boolean;
  className?: string;
};

/** Centred state card (design r-states 4599–4611): 46px icon tile, title, body (≤36ch), CTA. */
export function StateCard({
  icon,
  title,
  body,
  action,
  tone = 'flat',
  live = false,
  className,
}: StateCardProps) {
  return (
    <div
      role={live ? 'alert' : undefined}
      className={cn('flex flex-col items-center gap-3 px-5 py-7 text-center', className)}
    >
      <span
        aria-hidden="true"
        className={cn('flex size-[46px] items-center justify-center rounded-card', iconTile[tone])}
      >
        <Icon name={icon} className="size-[22px]" />
      </span>
      <h2 className="text-[0.9375rem] font-bold text-text-primary">{title}</h2>
      {body && <p className="max-w-[36ch] text-label leading-[1.8] text-text-secondary">{body}</p>}
      {action && <div className="pt-1">{action}</div>}
    </div>
  );
}

/** Nothing to show yet (e.g. no upcoming appointments). */
export function EmptyState(props: Omit<StateCardProps, 'tone' | 'live'> & { tone?: 'flat' | 'warn' }) {
  return <StateCard {...props} />;
}

/** A request failed. Pass a retry control as `action` (client component). */
export function ErrorState({
  title,
  body,
  action,
  className,
}: {
  title?: ReactNode;
  body?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  const t = useTranslations('ui.states.error');
  return (
    <StateCard
      icon="alert"
      tone="bad"
      live
      title={title ?? t('title')}
      body={body ?? t('body')}
      action={action}
      className={className}
    />
  );
}

/** 403: the API refused the request for this account (spec §5, never hide it behind a blank page). */
export function PermissionDenied({ body, homeHref = '/' }: { body?: ReactNode; homeHref?: string }) {
  const t = useTranslations('ui.states.permissionDenied');
  return (
    <StateCard
      icon="eyeOff"
      title={t('title')}
      body={body ?? t('body')}
      action={
        <ButtonLink href={homeHref} variant="outline" size="sm">
          {t('action')}
        </ButtonLink>
      }
    />
  );
}

/** 401 after the refresh attempt failed: ask the user to sign in again and come back here. */
export function ExpiredSession({ signInHref }: { signInHref: string }) {
  const t = useTranslations('ui.states.expiredSession');
  return (
    <StateCard
      icon="clock"
      tone="warn"
      title={t('title')}
      body={t('body')}
      action={
        <ButtonLink href={signInHref} size="sm">
          {t('action')}
        </ButtonLink>
      }
    />
  );
}

type InlineAlertProps = {
  tone?: 'info' | 'success' | 'warning' | 'danger';
  title: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
};

const alertTones = {
  info: {
    box: 'border-brand-200 bg-brand-50',
    icon: 'info' as const,
    iconClass: 'text-brand-700',
    title: 'text-brand-700',
  },
  success: {
    box: 'border-success-500/30 bg-success-50',
    icon: 'check' as const,
    iconClass: 'text-success-700',
    title: 'text-success-700',
  },
  warning: {
    box: 'border-warning-border bg-warning-50',
    icon: 'alert' as const,
    iconClass: 'text-warning-700',
    title: 'text-warning-700',
  },
  danger: {
    box: 'border-danger-border bg-danger-surface',
    icon: 'alert' as const,
    iconClass: 'text-danger-500',
    title: 'text-danger-700',
  },
};

/** Inline message block (design error card 730–737, warn note 252). Danger alerts are announced. */
export function InlineAlert({ tone = 'info', title, children, action, className }: InlineAlertProps) {
  const style = alertTones[tone];
  return (
    <div
      role={tone === 'danger' ? 'alert' : 'status'}
      className={cn('flex items-start gap-3 rounded-button border p-4', style.box, className)}
    >
      <Icon name={style.icon} className={cn('mt-0.5 size-5', style.iconClass)} />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <p className={cn('text-[0.875rem] font-bold', style.title)}>{title}</p>
        {children && <div className="text-label leading-[1.8] text-text-strong">{children}</div>}
        {action && <div className="pt-2">{action}</div>}
      </div>
    </div>
  );
}

/** Shimmer placeholder block. Decorative: pair with an accessible busy state on the container. */
export function Skeleton({ className }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'block animate-shimmer rounded-badge bg-[linear-gradient(90deg,var(--color-bg-app)_0%,var(--color-skeleton-highlight)_40%,var(--color-bg-app)_80%)] bg-[length:400px_100%]',
        className,
      )}
    />
  );
}

/**
 * List skeleton that mirrors the incoming rows (design r-states: 52px box + two lines).
 * Announces the busy state once to assistive technology.
 */
export function SkeletonList({ rows = 3, label }: { rows?: number; label: string }) {
  return (
    <div role="status" aria-live="polite" aria-busy="true" className="flex flex-col gap-3">
      <span className="sr-only">{label}</span>
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="flex items-center gap-3 rounded-card border border-border bg-surface p-3.5">
          <Skeleton className="size-[52px] rounded-field" />
          <div className="flex flex-1 flex-col gap-2">
            <Skeleton className="h-3" />
            <Skeleton className="h-3 w-2/5" />
          </div>
        </div>
      ))}
    </div>
  );
}
