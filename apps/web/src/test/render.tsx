import { render, type RenderOptions } from '@testing-library/react';
import { NextIntlClientProvider } from 'next-intl';
import type { ReactElement } from 'react';
import { vi } from 'vitest';
import ar from '../../messages/ar.json';
import en from '../../messages/en.json';

/** Current pathname returned by the mocked Next.js router (set per test with setPathname). */
let currentPathname = '/ar';

export function setPathname(pathname: string) {
  currentPathname = pathname;
}

vi.mock(import('next/navigation'), async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
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
      {ui}
    </NextIntlClientProvider>,
    options,
  );
}
