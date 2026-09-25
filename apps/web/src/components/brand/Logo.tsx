import Image from 'next/image';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';

/** Intrinsic size of public/brand/trimme-logo.png (source logo with only its transparent margin removed, D-042). */
export const LOGO_WIDTH = 371;
export const LOGO_HEIGHT = 177;

type LogoProps = {
  /** Rendered height in CSS pixels; width follows the intrinsic aspect ratio (never stretched). */
  height?: number;
  /** `onDark` applies the design's documented lightening for navy surfaces (design lines 216/797). */
  tone?: 'default' | 'onDark';
  priority?: boolean;
  className?: string;
};

/** The TRIMME logo, rendered from the official PNG without redrawing, recolouring or distortion. */
export function Logo({ height = 32, tone = 'default', priority = false, className }: LogoProps) {
  const t = useTranslations('common.brand');
  const width = Math.round((height * LOGO_WIDTH) / LOGO_HEIGHT);

  return (
    <Image
      src="/brand/trimme-logo.png"
      alt={t('logoAlt')}
      width={width}
      height={height}
      priority={priority}
      data-tone={tone}
      className={cn(
        'block max-w-none select-none',
        tone === 'onDark' && 'brightness-[1.35] saturate-[.85]',
        className,
      )}
      style={{ width, height }}
    />
  );
}
