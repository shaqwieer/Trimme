import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';

export type BadgeTone =
  'neutral' | 'info' | 'brand' | 'success' | 'warning' | 'danger' | 'onImage' | 'onImageMuted';

const tones: Record<BadgeTone, { box: string; dot: string }> = {
  neutral: { box: 'bg-status-noshow-bg text-status-noshow-fg', dot: 'bg-status-noshow-dot' },
  info: { box: 'bg-status-confirmed-bg text-status-confirmed-fg', dot: 'bg-status-confirmed-dot' },
  brand: { box: 'bg-status-arrived-bg text-status-arrived-fg', dot: 'bg-status-arrived-dot' },
  success: { box: 'bg-status-completed-bg text-status-completed-fg', dot: 'bg-status-completed-dot' },
  warning: { box: 'bg-status-pending-bg text-status-pending-fg', dot: 'bg-status-pending-dot' },
  danger: { box: 'bg-status-cancelled-bg text-status-cancelled-fg', dot: 'bg-status-cancelled-dot' },
  onImage: { box: 'bg-navy-900/90 text-on-navy', dot: 'bg-success-500' },
  onImageMuted: { box: 'bg-text-secondary/95 text-on-navy', dot: 'bg-on-navy' },
};

type BadgeProps = {
  tone?: BadgeTone;
  children: ReactNode;
  /** Colour dot + text (design rule 442: meaning never depends on colour alone). */
  dot?: boolean;
  size?: 'md' | 'sm';
  className?: string;
};

export function Badge({ tone = 'neutral', children, dot = true, size = 'md', className }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-badge font-bold whitespace-nowrap',
        size === 'md' ? 'px-2.5 py-1 text-badge' : 'px-[7px] py-[3px] text-[0.65625rem]',
        tones[tone].box,
        className,
      )}
    >
      {dot && size === 'md' && (
        <span aria-hidden="true" className={cn('size-1.5 shrink-0 rounded-full', tones[tone].dot)} />
      )}
      {children}
    </span>
  );
}

/** Booking lifecycle (D-016). Both cancellation states share the danger colour with distinct labels. */
export const BOOKING_STATUSES = [
  'Pending',
  'Confirmed',
  'Arrived',
  'Completed',
  'CancelledByCustomer',
  'CancelledByShop',
  'NoShow',
] as const;
export type BookingStatus = (typeof BOOKING_STATUSES)[number];

export const SUBSCRIPTION_STATUSES = ['Active', 'ExpiringSoon', 'Expired', 'Suspended'] as const;
export type SubscriptionStatus = (typeof SUBSCRIPTION_STATUSES)[number];

const bookingTone: Record<BookingStatus, BadgeTone> = {
  Pending: 'warning',
  Confirmed: 'info',
  Arrived: 'brand',
  Completed: 'success',
  CancelledByCustomer: 'danger',
  CancelledByShop: 'danger',
  NoShow: 'neutral',
};

const subscriptionTone: Record<SubscriptionStatus, BadgeTone> = {
  Active: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Suspended: 'neutral',
};

type StatusBadgeProps =
  | { kind: 'booking'; status: BookingStatus; size?: 'md' | 'sm'; className?: string }
  | { kind: 'subscription'; status: SubscriptionStatus; size?: 'md' | 'sm'; className?: string };

/** Status chip bound to the API enums, with localized labels. */
export function StatusBadge(props: StatusBadgeProps) {
  const t = useTranslations('status');
  const tone = props.kind === 'booking' ? bookingTone[props.status] : subscriptionTone[props.status];
  const label = props.kind === 'booking' ? t(`booking.${props.status}`) : t(`subscription.${props.status}`);
  return (
    <Badge tone={tone} size={props.size} className={props.className}>
      {label}
    </Badge>
  );
}
