import type { ReactNode } from 'react';
import { Logo } from '@/components/brand/Logo';
import { cn } from '@/lib/cn';

type AuthCardProps = {
  title: ReactNode;
  subtitle?: ReactNode;
  /** Design c-auth: the sign-up frame shows the logo above a centred heading. */
  showLogo?: boolean;
  align?: 'center' | 'start';
  /** Placed above the heading, e.g. a back button (design OTP frame). */
  leading?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
};

/**
 * Card used by every auth screen (design c-auth 918–970): a single mobile-first column that becomes a centred,
 * elevated card from 768px. Server-safe.
 */
export function AuthCard({
  title,
  subtitle,
  showLogo = false,
  align = 'center',
  leading,
  children,
  footer,
}: AuthCardProps) {
  return (
    <div className="mx-auto w-full max-w-[460px] px-4 py-6 md:py-12">
      <div className="flex flex-col gap-6 md:rounded-card md:border md:border-border md:bg-surface md:p-8 md:shadow-e1">
        {leading}
        <header
          className={cn(
            'flex flex-col gap-2',
            align === 'center' ? 'items-center text-center' : 'items-start',
          )}
        >
          {showLogo && (
            <div className="pb-3">
              <Logo height={58} priority />
            </div>
          )}
          <h1 className="text-h2 font-bold text-navy-900">{title}</h1>
          {subtitle && <div className="text-caption leading-[1.8] text-text-secondary">{subtitle}</div>}
        </header>
        {children}
        {footer && <div className="text-center text-caption text-text-secondary">{footer}</div>}
      </div>
    </div>
  );
}
