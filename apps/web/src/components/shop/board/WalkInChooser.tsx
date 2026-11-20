import { useTranslations } from 'next-intl';
import { ButtonLink } from '@/components/ui/Button';
import type { DesignIconName } from '@/components/ui/icons';

/** What the barber records on «حجز حضوري» (D-158): a walk-in, a phone booking, a break, or «غير متوفر اليوم». */
export const WALK_IN_MODES = ['walkIn', 'phone', 'break', 'unavailable'] as const;
export type WalkInMode = (typeof WALK_IN_MODES)[number];

const ICONS: Record<WalkInMode, DesignIconName> = {
  walkIn: 'user',
  phone: 'phone',
  break: 'coffee',
  unavailable: 'ban',
};

/**
 * The barber's four choices at the top of «حجز حضوري» (D-158, the client's request: «أول شي يختار واحد من الأربعة،
 * بعدين تتعدل الشاشة»): they used to sit on the day screen and the appointments (D-151) and now live here only. Nothing
 * below shows until one is chosen; the chosen one is filled and stays in view, so another can be picked at any time.
 * Plain links (`?mode=`), so the browser's back returns to the previous choice.
 */
export function WalkInChooser({ current }: { current?: WalkInMode }) {
  const t = useTranslations('shopBoard.walkIn.choose');
  return (
    <div className="flex flex-col gap-3">
      <nav
        aria-label={t('label')}
        className="grid grid-cols-2 gap-2 md:grid-cols-4"
        data-testid="quick-actions"
      >
        {WALK_IN_MODES.map((mode) => {
          const active = current === mode;
          return (
            <ButtonLink
              key={mode}
              href={`/shop/walk-in?mode=${mode}`}
              // Nothing chosen yet: four equal buttons. Then the chosen one is filled and the others outlined.
              variant={active ? 'primary' : current ? 'outline' : 'secondary'}
              size="md"
              icon={ICONS[mode]}
              aria-current={active ? 'page' : undefined}
              scroll={false}
            >
              {t(mode)}
            </ButtonLink>
          );
        })}
      </nav>
      {!current && (
        <p
          className="rounded-card border border-border bg-surface p-4 text-label text-text-secondary"
          role="status"
        >
          {t('prompt')}
        </p>
      )}
    </div>
  );
}
