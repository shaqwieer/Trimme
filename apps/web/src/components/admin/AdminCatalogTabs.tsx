import { useTranslations } from 'next-intl';
import { LinkTabs } from '@/components/ui/LinkTabs';

/**
 * Tabs of the admin services area (a-services, corrected per DV-S02). Server-safe. The platform categories tab is gone
 * (D-151): each shop has its own sections (D-149), and nothing customers see uses the platform categories.
 */
export function AdminCatalogTabs({ active }: { active: 'services' | 'packages' }) {
  const t = useTranslations('adminServices.tabs');
  return (
    <LinkTabs
      label={t('label')}
      tabs={[
        { href: '/admin/services', label: t('services'), active: active === 'services' },
        { href: '/admin/packages', label: t('packages'), active: active === 'packages' },
      ]}
    />
  );
}
