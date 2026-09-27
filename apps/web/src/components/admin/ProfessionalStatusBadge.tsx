import { useTranslations } from 'next-intl';
import { Badge } from '@/components/ui/Badge';

/** Professional status (design a-pros: نشط / معطّل). Server-safe. */
export function ProfessionalStatusBadge({ status }: { status: 'Active' | 'Disabled' }) {
  const t = useTranslations('adminProfessionals.status');
  return <Badge tone={status === 'Active' ? 'success' : 'neutral'}>{t(status)}</Badge>;
}
