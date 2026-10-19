import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';

/** «موقع سعودي ١٠٠٪» (D-132): the client's statement that the platform is fully Saudi, on the landing hero, the customer home and the footer. */
export function SaudiBadge({ className }: { className?: string }) {
  const t = useTranslations('common.brand');

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-pill bg-success-50 px-3 py-1.5 text-helper font-bold text-success-700',
        className,
      )}
    >
      <span aria-hidden="true" className="size-2 rounded-full bg-success-500" />
      {t('saudi')}
    </span>
  );
}
