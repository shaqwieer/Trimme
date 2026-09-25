import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { assertDevRoutesEnabled } from '@/lib/dev/devRoutes';

export const metadata: Metadata = { robots: { index: false, follow: false } };

export default async function DevLayout({ children }: { children: ReactNode }) {
  await assertDevRoutesEnabled();
  return children;
}
