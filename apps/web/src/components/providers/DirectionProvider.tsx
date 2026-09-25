'use client';

import { Direction } from 'radix-ui';
import type { ReactNode } from 'react';

/**
 * Tells Radix primitives the document direction so keyboard navigation (tabs, menus) and sliders
 * are mirrored in Arabic. `html[dir]` alone is not enough for their key handling (D-048).
 */
export function DirectionProvider({ dir, children }: { dir: 'rtl' | 'ltr'; children: ReactNode }) {
  return <Direction.Provider dir={dir}>{children}</Direction.Provider>;
}
