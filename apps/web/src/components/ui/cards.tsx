import Image from 'next/image';
import type { InputHTMLAttributes, ReactNode } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDistanceKm, formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { Avatar, ImagePlaceholder } from './Avatar';
import { Badge, type BookingStatus, StatusBadge } from './Badge';
import { Icon } from './icons';
import { RatingStars } from './Rating';
import { RadioCard, TagChip } from './selection';

/** White surface card (radius 14, 1px border, E1) — the base of every card in the design. */
export function Card({
  children,
  className,
  as: Tag = 'div',
}: {
  children: ReactNode;
  className?: string;
  as?: 'div' | 'article' | 'section' | 'li';
}) {
  return (
    <Tag className={cn('rounded-card border border-border bg-surface shadow-e1', className)}>{children}</Tag>
  );
}

export type ShopCardData = {
  href: string;
  name: string;
  coverUrl?: string | null;
  verified: boolean;
  rating: number;
  reviewCount: number;
  district: string;
  distanceKm?: number | null;
  /** Server-computed opening label, e.g. "مفتوح حتى ١١:٠٠ م" / "يفتح ٢:٠٠ م". */
  openingLabel: string;
  isOpen: boolean;
  /** Lowest price of this shop's own services (never a platform-wide price, D-025). */
  fromPrice?: number | null;
  tags?: string[];
};

/**
 * Shop card (design 553–575). The shop name is the link; the card is a single tap target via a
 * stretched link. `favorite` is a slot for the (client) favourite toggle.
 */
export function ShopCard({
  shop,
  favorite,
  headingLevel = 3,
}: {
  shop: ShopCardData;
  favorite?: ReactNode;
  headingLevel?: 2 | 3;
}) {
  const t = useTranslations('ui.shopCard');
  const locale = useLocale() as AppLocale;
  const Heading = `h${headingLevel}` as const;

  return (
    <article className="group relative overflow-hidden rounded-card border border-border bg-surface shadow-e1 transition-shadow hover:shadow-e2">
      <div className="relative h-[104px]">
        {shop.coverUrl ? (
          <Image
            src={shop.coverUrl}
            alt=""
            fill
            sizes="(min-width: 768px) 360px, 100vw"
            className="object-cover"
          />
        ) : (
          <ImagePlaceholder className="h-full w-full" />
        )}
        <Badge tone={shop.isOpen ? 'onImage' : 'onImageMuted'} className="absolute start-3 top-3">
          {shop.openingLabel}
        </Badge>
        {favorite && <div className="absolute end-2 top-2 z-10">{favorite}</div>}
      </div>
      <div className="flex flex-col gap-2 px-4 py-3.5">
        <Heading className="flex items-center gap-1.5 text-[0.96875rem] font-bold text-text-primary">
          <Link href={shop.href} className="after:absolute after:inset-0 after:content-['']">
            {shop.name}
          </Link>
          {shop.verified && <Icon name="shield" label={t('verified')} className="size-4 text-brand-700" />}
        </Heading>
        <p className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-helper text-text-secondary">
          <RatingStars value={shop.rating} count={shop.reviewCount} showValue />
          <span aria-hidden="true" className="text-border-strong">
            ·
          </span>
          <span>{shop.district}</span>
          {shop.distanceKm != null && (
            <>
              <span aria-hidden="true" className="text-border-strong">
                ·
              </span>
              <span className="font-latin font-semibold">{formatDistanceKm(shop.distanceKm, locale)}</span>
            </>
          )}
        </p>
        {(shop.tags?.length || shop.fromPrice != null) && (
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap gap-1.5">
              {shop.tags?.map((tag) => (
                <TagChip key={tag}>{tag}</TagChip>
              ))}
            </div>
            {shop.fromPrice != null && (
              <span className="text-helper text-text-secondary">
                {t('from', { price: formatPrice(shop.fromPrice, locale) })}
              </span>
            )}
          </div>
        )}
      </div>
    </article>
  );
}

type ServiceOptionProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'children'> & {
  title: string;
  description?: string;
  price: number;
  durationMinutes: number;
};

/** Selectable service/package row for the booking wizard (design 515–536), on a native radio. */
export function ServiceOption({ title, description, price, durationMinutes, ...input }: ServiceOptionProps) {
  const locale = useLocale() as AppLocale;
  return (
    <RadioCard
      {...input}
      aside={
        <span className="flex flex-col items-end gap-0.5">
          <span className="font-latin text-[0.9375rem] font-bold text-navy-900">
            {formatPrice(price, locale)}
          </span>
          <span className="text-badge text-text-secondary">
            {formatDurationMinutes(durationMinutes, locale)}
          </span>
        </span>
      }
    >
      <span className="block text-[0.9375rem] font-bold text-text-primary">{title}</span>
      {description && <span className="block text-helper text-text-secondary">{description}</span>}
    </RadioCard>
  );
}

type ProfessionalOptionProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'children'> & {
  name: string;
  displayName: string;
  specialty?: string;
  photoUrl?: string | null;
  rating?: number | null;
  reviewCount?: number;
  /** On leave / not bookable: the option is disabled and shows the design's "في إجازة" chip. */
  unavailable?: boolean;
};

/** Selectable professional card (design 538–549), centred avatar + name + rating, on a native radio. */
export function ProfessionalOption({
  displayName,
  specialty,
  photoUrl,
  rating,
  reviewCount,
  unavailable = false,
  disabled,
  ...input
}: ProfessionalOptionProps) {
  const t = useTranslations('ui.professional');
  return (
    <label
      className={cn(
        'relative flex cursor-pointer flex-col items-center gap-1 rounded-card border border-border bg-surface p-3.5 text-center transition-colors',
        'has-checked:border-[1.5px] has-checked:border-brand-500 has-checked:bg-brand-50 has-focus-visible:shadow-[var(--focus-ring)]',
        (unavailable || disabled) && 'cursor-not-allowed opacity-70',
      )}
    >
      <input type="radio" className="sr-only" disabled={unavailable || disabled} {...input} />
      <Avatar name={displayName} src={photoUrl} size="lg" className="mb-1.5" />
      <span className="text-[0.875rem] font-bold text-text-primary">{displayName}</span>
      {specialty && <span className="text-badge text-text-secondary">{specialty}</span>}
      {unavailable ? (
        <Badge tone="warning" dot={false} className="mt-1">
          {t('unavailable')}
        </Badge>
      ) : (
        rating != null && (
          <RatingStars value={rating} count={reviewCount} className="mt-1 flex-wrap justify-center" />
        )
      )}
    </label>
  );
}

export type AppointmentCardData = {
  href: string;
  /** Local date parts in the shop's time zone (already converted by the server). */
  dayNumber: string;
  monthLabel: string;
  serviceName: string;
  status: BookingStatus;
  shopName: string;
  professionalName: string;
  timeRange: string;
};

/** Appointment list item (design 576–590): date block, service + status, shop · professional, time. */
export function AppointmentCard({ appointment }: { appointment: AppointmentCardData }) {
  return (
    <article className="relative flex items-center gap-3.5 rounded-card border border-border bg-surface px-4 py-3.5 shadow-e1 transition-shadow hover:shadow-e2">
      <div className="flex w-[52px] shrink-0 flex-col items-center rounded-field bg-brand-100 py-2">
        <span className="font-latin text-[1.0625rem] font-bold text-navy-900">{appointment.dayNumber}</span>
        <span className="text-[0.6875rem] font-bold text-brand-700">{appointment.monthLabel}</span>
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="text-[0.90625rem] font-bold text-text-primary">
            <Link href={appointment.href} className="after:absolute after:inset-0 after:content-['']">
              {appointment.serviceName}
            </Link>
          </h3>
          <StatusBadge kind="booking" status={appointment.status} />
        </div>
        <p className="truncate text-helper text-text-secondary">
          {appointment.shopName} · {appointment.professionalName}
        </p>
        <p className="text-helper font-bold text-text-strong">{appointment.timeRange}</p>
      </div>
      <Icon name="chevR" className="size-[18px] text-decorative-300" />
    </article>
  );
}

type KpiTileProps = {
  label: string;
  value: string;
  icon?: Parameters<typeof Icon>[0]['name'];
  /** Delta text; tone is semantic (e.g. a rising no-show rate is "bad"), not based on the sign. */
  delta?: { text: string; tone: 'good' | 'bad' | 'neutral' };
};

/** KPI tile (design 623–635): label, Inter 25px value, semantic delta. */
export function KpiTile({ label, value, icon, delta }: KpiTileProps) {
  return (
    <div className="flex flex-col gap-1 rounded-button border border-border bg-surface p-4 shadow-e1">
      <div className="flex items-center justify-between gap-2">
        <span className="text-helper text-text-secondary">{label}</span>
        {icon && (
          <span
            aria-hidden="true"
            className="flex size-8 items-center justify-center rounded-sm bg-brand-100 text-brand-700"
          >
            <Icon name={icon} className="size-4" />
          </span>
        )}
      </div>
      <span className="font-latin text-kpi font-bold text-navy-900">{value}</span>
      {delta && (
        <span
          className={cn(
            'text-badge font-bold',
            delta.tone === 'good' && 'text-success-700',
            delta.tone === 'bad' && 'text-danger-700',
            delta.tone === 'neutral' && 'text-text-secondary',
          )}
        >
          {delta.text}
        </span>
      )}
    </div>
  );
}
