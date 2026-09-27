import { useTranslations } from 'next-intl';
import { Badge, type BadgeTone } from '@/components/ui/Badge';

type CatalogState = { isActive: boolean; isArchived: boolean; moderation: 'Visible' | 'Hidden' };

/** Archived / hidden by the admin / on / off — colour dot plus text, never colour alone. Server-safe. */
export function CatalogStatusBadge({ item }: { item: CatalogState }) {
  const t = useTranslations('catalog.status');
  const [key, tone]: ['active' | 'inactive' | 'archived' | 'hidden', BadgeTone] = item.isArchived
    ? ['archived', 'neutral']
    : item.moderation === 'Hidden'
      ? ['hidden', 'danger']
      : item.isActive
        ? ['active', 'success']
        : ['inactive', 'warning'];
  return <Badge tone={tone}>{t(key)}</Badge>;
}
