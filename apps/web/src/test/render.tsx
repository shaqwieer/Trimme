import { render, type RenderOptions } from '@testing-library/react';
import { NextIntlClientProvider } from 'next-intl';
import type { ReactElement } from 'react';
import { vi } from 'vitest';
import { DirectionProvider } from '@/components/providers/DirectionProvider';
import { ThemeProvider } from '@/components/theme/ThemeProvider';
import ar from '../../messages/ar.json';
import en from '../../messages/en.json';

/** Current pathname returned by the mocked Next.js router (set per test with setPathname). */
let currentPathname = '/ar';

export function setPathname(pathname: string) {
  currentPathname = pathname;
}

/** Like Next.js, history.pushState / replaceState update `useSearchParams` (the booking wizard keeps its steps there). */
const LOCATION_EVENT = 'test:location';
if (typeof window !== 'undefined' && !('__trimmePatched' in window.history)) {
  for (const method of ['pushState', 'replaceState'] as const) {
    const original = window.history[method].bind(window.history);
    window.history[method] = (...args: Parameters<History['pushState']>) => {
      original(...args);
      window.dispatchEvent(new Event(LOCATION_EVENT));
    };
  }
  Object.defineProperty(window.history, '__trimmePatched', { value: true });
}

vi.mock(import('next/navigation'), async (importOriginal) => {
  const actual = await importOriginal();
  const { useMemo, useSyncExternalStore } = await import('react');
  const subscribe = (onChange: () => void) => {
    window.addEventListener(LOCATION_EVENT, onChange);
    window.addEventListener('popstate', onChange);
    return () => {
      window.removeEventListener(LOCATION_EVENT, onChange);
      window.removeEventListener('popstate', onChange);
    };
  };
  return {
    ...actual,
    useSearchParams: (() => {
      const search = useSyncExternalStore(
        subscribe,
        () => window.location.search,
        () => '',
      );
      return useMemo(() => new URLSearchParams(search), [search]);
    }) as unknown as typeof actual.useSearchParams,
    usePathname: () => currentPathname,
    useRouter: () =>
      ({
        push: vi.fn(),
        replace: vi.fn(),
        prefetch: vi.fn(),
        back: vi.fn(),
        forward: vi.fn(),
        refresh: vi.fn(),
      }) as unknown as ReturnType<typeof actual.useRouter>,
    useParams: (() => ({ locale: currentPathname.split('/')[1] ?? 'ar' })) as typeof actual.useParams,
  };
});

/** Renders UI inside next-intl with the real message catalogs, in a document of the right direction. */
export function renderWithIntl(
  ui: ReactElement,
  { locale = 'ar', ...options }: { locale?: 'ar' | 'en' } & RenderOptions = {},
) {
  document.documentElement.lang = locale;
  document.documentElement.dir = locale === 'ar' ? 'rtl' : 'ltr';
  return render(
    <NextIntlClientProvider locale={locale} messages={locale === 'ar' ? ar : en} timeZone="Asia/Riyadh">
      <ThemeProvider initial="system">
        <DirectionProvider dir={locale === 'ar' ? 'rtl' : 'ltr'}>{ui}</DirectionProvider>
      </ThemeProvider>
    </NextIntlClientProvider>,
    options,
  );
}
