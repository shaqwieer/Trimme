import { useTranslations } from 'next-intl';
import { Badge, type BadgeTone } from '@/components/ui/Badge';
import type { components } from '@/lib/api/schema';

type PlanStatus = components['schemas']['PlanStatus'];

const tones: Record<PlanStatus, BadgeTone> = {
  Draft: 'neutral',
  Published: 'success',
  Inactive: 'warning',
  Archived: 'neutral',
};

export function PlanStatusBadge({ status }: { status: PlanStatus }) {
  const t = useTranslations('subscriptionPlans.statuses');
  return <Badge tone={tones[status]}>{t(status)}</Badge>;
}
