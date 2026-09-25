import type { Metadata } from 'next';
import { PublicShell } from '@/components/shell/PublicShell';
import { ComponentGallery } from './ComponentGallery';

export const metadata: Metadata = { title: 'Component gallery', robots: { index: false, follow: false } };

/**
 * Development-only component gallery (Phase 03, D-045): every design-system component in RTL/LTR,
 * compared against design/reference/1440/ds-components.jpg. Not a product route.
 */
export default async function ComponentsPage({ params }: PageProps<'/[locale]/dev/components'>) {
  const { locale } = await params;
  return (
    <PublicShell>
      <ComponentGallery locale={locale === 'en' ? 'en' : 'ar'} />
    </PublicShell>
  );
}
