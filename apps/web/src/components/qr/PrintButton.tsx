'use client';

import { Button } from '@/components/ui/Button';

/** Opens the browser's print dialog (A5 page set by the poster's print styles; "Save as PDF" gives the PDF). */
export function PrintButton({ label }: { label: string }) {
  return (
    <Button size="sm" icon="download" onClick={() => window.print()}>
      {label}
    </Button>
  );
}
