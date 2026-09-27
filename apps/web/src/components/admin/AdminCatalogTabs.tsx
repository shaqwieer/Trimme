import { useTranslations } from 'next-intl';
import { LinkTabs } from '@/components/ui/LinkTabs';

/** Tabs of the admin services area (a-services, corrected per DV-S02). Server-safe. */
export function AdminCatalogTabs({ active }: { active: 'services' | 'categories' | 'packages' }) {
  const t = useTranslations('adminServices.tabs');
  return (
    <LinkTabs
      label={t('label')}
      tabs={[
        { href: '/admin/services', label: t('services'), active: active === 'services' },
        { href: '/admin/services/categories', label: t('categories'), active: active === 'categories' },
        { href: '/admin/packages', label: t('packages'), active: active === 'packages' },
      ]}
    />
  );
}
