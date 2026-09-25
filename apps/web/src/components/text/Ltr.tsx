import type { ReactNode } from 'react';

/**
 * Keeps left-to-right content (phone numbers, times, prices, Latin names, codes) readable inside
 * right-to-left text by isolating its direction (spec §5 bidi handling).
 */
export function Ltr({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <bdi dir="ltr" className={className}>
      {children}
    </bdi>
  );
}
