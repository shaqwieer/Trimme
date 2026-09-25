import Image from 'next/image';
import { cn } from '@/lib/cn';

const sizes = {
  sm: 'size-8 text-badge',
  md: 'size-9 text-label',
  lg: 'size-14 text-h3',
  xl: 'size-[68px] text-h2',
} as const;
const pixels = { sm: 32, md: 36, lg: 56, xl: 68 } as const;

const ARABIC_ARTICLE = 'ال';

/** First letter of a word, skipping the Arabic definite article ("العنزي" → "ع"). */
function firstLetter(word: string | undefined): string {
  if (!word) return '';
  return word.startsWith(ARABIC_ARTICLE) && word.length > 2 ? (word[2] ?? '') : (word[0] ?? '');
}

/** First letters of the first and last words ("محمد العنزي" → "م ع", "Faisal Q" → "FQ"). */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const first = firstLetter(words[0]);
  const last = words.length > 1 ? firstLetter(words[words.length - 1]) : '';
  const isArabic = /[؀-ۿ]/.test(name);
  return isArabic ? [first, last].filter(Boolean).join(' ') : `${first}${last}`.toUpperCase();
}

type AvatarProps = {
  name: string;
  src?: string | null;
  size?: keyof typeof sizes;
  className?: string;
};

/** Photo avatar, or brand-tinted initials when no photo exists (design 575, 668). */
export function Avatar({ name, src, size = 'md', className }: AvatarProps) {
  if (src) {
    return (
      <Image
        src={src}
        alt={name}
        width={pixels[size]}
        height={pixels[size]}
        className={cn('shrink-0 rounded-full object-cover', sizes[size], className)}
      />
    );
  }
  return (
    <span
      role="img"
      aria-label={name}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-full bg-brand-100 font-bold text-brand-700',
        sizes[size],
        className,
      )}
    >
      <span aria-hidden="true">{initials(name)}</span>
    </span>
  );
}

/** Striped placeholder used until real imagery exists (design 135deg stripes #E7EDF4/#EFF3F8). */
export function ImagePlaceholder({ className }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'block bg-[repeating-linear-gradient(135deg,var(--color-bg-subtle)_0_8px,var(--color-bg-page)_8px_16px)]',
        className,
      )}
    />
  );
}
