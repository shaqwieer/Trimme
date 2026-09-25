'use client';

import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { Icon } from './icons';

export type ToastTone = 'success' | 'info' | 'error';

export type ToastOptions = {
  title: ReactNode;
  tone?: ToastTone;
  /** Optional action; toasts with an action never auto-dismiss (WCAG 2.2.1). */
  action?: { label: ReactNode; onClick: () => void };
  /** Milliseconds before auto-dismiss (default 5000). Paused while hovered or focused. */
  duration?: number;
};

type ToastEntry = ToastOptions & { id: number };

type ToastContextValue = { show: (options: ToastOptions) => number; dismiss: (id: number) => void };

const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used inside <ToastProvider>.');
  return context;
}

/**
 * Toast notifications (design 694–700). Live regions are mounted up front so the first message is
 * announced; errors use an assertive region. Centred with logical properties (fixes DV-T04).
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const t = useTranslations('ui.toast');
  const [toasts, setToasts] = useState<ToastEntry[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback(
    (id: number) => setToasts((current) => current.filter((toast) => toast.id !== id)),
    [],
  );
  const show = useCallback((options: ToastOptions) => {
    const id = nextId.current++;
    setToasts((current) => [...current.slice(-2), { ...options, id }]);
    return id;
  }, []);
  const value = useMemo(() => ({ show, dismiss }), [show, dismiss]);

  const polite = toasts.filter((toast) => toast.tone !== 'error');
  const assertive = toasts.filter((toast) => toast.tone === 'error');

  return (
    <ToastContext.Provider value={value}>
      {children}
      <section
        aria-label={t('region')}
        className="pointer-events-none fixed inset-x-4 bottom-[calc(var(--layout-bottom-nav-height)+1rem)] z-60 mx-auto flex max-w-[440px] flex-col gap-2 lg:bottom-6"
      >
        <div role="status" aria-live="polite" className="flex flex-col gap-2">
          {polite.map((toast) => (
            <ToastItem key={toast.id} toast={toast} onDismiss={dismiss} dismissLabel={t('dismiss')} />
          ))}
        </div>
        <div role="alert" aria-live="assertive" className="flex flex-col gap-2">
          {assertive.map((toast) => (
            <ToastItem key={toast.id} toast={toast} onDismiss={dismiss} dismissLabel={t('dismiss')} />
          ))}
        </div>
      </section>
    </ToastContext.Provider>
  );
}

function ToastItem({
  toast,
  onDismiss,
  dismissLabel,
}: {
  toast: ToastEntry;
  onDismiss: (id: number) => void;
  dismissLabel: string;
}) {
  const [paused, setPaused] = useState(false);
  const tone = toast.tone ?? 'success';

  useEffect(() => {
    if (toast.action || paused) return;
    const timer = window.setTimeout(() => onDismiss(toast.id), toast.duration ?? 5000);
    return () => window.clearTimeout(timer);
  }, [toast, paused, onDismiss]);

  return (
    <div
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
      className="pointer-events-auto flex items-center gap-3 rounded-button bg-navy-900 px-4 py-3 text-[0.875rem] text-on-navy shadow-toast"
    >
      <span
        aria-hidden="true"
        className={cn(
          'flex size-6 shrink-0 items-center justify-center rounded-full',
          tone === 'success' && 'bg-success-500',
          tone === 'info' && 'bg-brand-600',
          tone === 'error' && 'bg-danger-500',
        )}
      >
        <Icon
          name={tone === 'success' ? 'check' : tone === 'info' ? 'info' : 'alert'}
          className="size-3.5"
          strokeWidth={2.25}
        />
      </span>
      <p className="flex-1">{toast.title}</p>
      {toast.action && (
        <button
          type="button"
          onClick={() => {
            toast.action?.onClick();
            onDismiss(toast.id);
          }}
          className="min-h-11 shrink-0 rounded-sm px-2 font-bold text-on-navy underline underline-offset-4"
        >
          {toast.action.label}
        </button>
      )}
      <button
        type="button"
        onClick={() => onDismiss(toast.id)}
        aria-label={dismissLabel}
        className="inline-flex size-9 shrink-0 items-center justify-center rounded-sm text-on-navy-muted hover:text-on-navy"
      >
        <Icon name="x" className="size-4" />
      </button>
    </div>
  );
}
