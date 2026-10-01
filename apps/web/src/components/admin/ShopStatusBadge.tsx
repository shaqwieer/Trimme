import { useTranslations } from 'next-intl';
import { Badge, type BadgeTone } from '@/components/ui/Badge';

export const SHOP_STATUSES = ['Draft', 'Active', 'Suspended'] as const;
export type ShopStatus = (typeof SHOP_STATUSES)[number];

const tones: Record<ShopStatus, BadgeTone> = { Draft: 'warning', Active: 'success', Suspended: 'neutral' };

export function isShopStatus(value: string): value is ShopStatus {
  return (SHOP_STATUSES as readonly string[]).includes(value);
}

/** Shop lifecycle badge (colour dot + label, never colour alone). Server-safe. */
export function ShopStatusBadge({ status }: { status: string }) {
  const t = useTranslations('shopStatus');
  const known = isShopStatus(status) ? status : 'Draft';
  return <Badge tone={tones[known]}>{t(known)}</Badge>;
}
