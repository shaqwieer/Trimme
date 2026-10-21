import { useTranslations } from 'next-intl';
import { Badge } from '@/components/ui/Badge';
import type { components } from '@/lib/api/schema';

/**
 * «زبون جديد» or «زبون عائد» (D-135). The API fills `visit` for the shop's owner only, so for the desk and for walk-ins
 * there is nothing to show.
 */
export function CustomerVisitBadge({ visit }: { visit: components['schemas']['CustomerVisit'] | undefined }) {
  const t = useTranslations('shopBoard.visit');
  if (!visit) return null;
  return (
    <Badge tone={visit === 'New' ? 'info' : 'success'} size="sm">
      {t(visit)}
    </Badge>
  );
}
