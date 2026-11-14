'use client';

import dynamic from 'next/dynamic';
import { useTranslations } from 'next-intl';
import type { ComponentProps } from 'react';
import { SkeletonList } from '@/components/ui/states';
import type { LegalEditor as Editor } from './LegalEditor';

/**
 * The legal-page editor, loaded on demand (D-144): TipTap is the admin page's own weight, kept out of its first load
 * (the Phase 17 bundle budget) and out of every other page. A skeleton holds its place while it loads.
 */
function Loading() {
  const t = useTranslations('legalAdmin');
  return <SkeletonList rows={6} label={t('loading')} />;
}

const LegalEditor = dynamic(() => import('./LegalEditor').then((module) => module.LegalEditor), {
  ssr: false,
  loading: Loading,
});

export function LegalEditorLoader(props: ComponentProps<typeof Editor>) {
  return <LegalEditor {...props} />;
}
