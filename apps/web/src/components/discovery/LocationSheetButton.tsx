'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Icon } from '@/components/ui/icons';
import { Sheet } from '@/components/ui/overlays';
import type { DiscoveryArea } from '@/lib/api/public-types';
import { LocationChooser } from './LocationChooser';

/**
 * The home header's location switcher (c-home 1000–1004: "موقعك الحالي · حي الملقا، الرياض ▾"). It opens the location
 * sheet (DV-A21) and refreshes the page with the new origin.
 */
export function LocationSheetButton({ label, areas }: { label: string | null; areas: DiscoveryArea[] }) {
  const t = useTranslations('discover');
  const tLocation = useTranslations('location');
  const [open, setOpen] = useState(false);
  const shown = label ?? t('setLocation');

  return (
    <Sheet
      open={open}
      onOpenChange={setOpen}
      side="bottom"
      title={tLocation('sheetTitle')}
      trigger={
        <button
          type="button"
          aria-label={label ? t('changeLocation', { label }) : t('setLocation')}
          className="flex min-h-11 flex-col items-start rounded-field text-start"
        >
          <span className="text-helper text-text-secondary">{t('yourLocation')}</span>
          <span className="flex items-center gap-1 text-label font-bold text-navy-900">
            <Icon name="pin" className="size-4 text-brand-600" />
            {shown}
            <Icon name="chevD" className="size-4 text-text-tertiary" />
          </span>
        </button>
      }
    >
      <div className="flex flex-col gap-4 p-5 pt-3">
        <p className="text-helper text-text-secondary">{tLocation('body')}</p>
        <LocationChooser areas={areas} onDone={() => setOpen(false)} />
      </div>
    </Sheet>
  );
}
