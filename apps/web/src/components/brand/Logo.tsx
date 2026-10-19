import Image from 'next/image';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';

/**
 * Intrinsic size of public/brand/groomhere-logo.png: the Groom Here logo (new-logo.jpeg) with its background made transparent,
 * cropped to the artwork and set in the brand blue so it reads on light and dark surfaces (D-131).
 */
export const LOGO_WIDTH = 866;
export const LOGO_HEIGHT = 296;

type LogoProps = {
  /** Rendered height in CSS pixels; width follows the intrinsic aspect ratio (never stretched). */
  height?: number;
  /** `onDark` applies the design's documented lightening for navy surfaces (design lines 216/797). */
  tone?: 'default' | 'onDark';
  priority?: boolean;
  className?: string;
};

/** The Groom Here logo, rendered from the derived PNG without redrawing or distortion. */
export function Logo({ height = 32, tone = 'default', priority = false, className }: LogoProps) {
  const t = useTranslations('common.brand');
  const width = Math.round((height * LOGO_WIDTH) / LOGO_HEIGHT);

  return (
    <Image
      src="/brand/groomhere-logo.png"
      alt={t('logoAlt')}
      width={width}
      height={height}
      loading={priority ? 'eager' : undefined}
      fetchPriority={priority ? 'high' : undefined}
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
