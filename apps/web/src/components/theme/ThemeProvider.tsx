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
import {
  colorScheme,
  parseTheme,
  THEME_CHANNEL,
  themeAttribute,
  themeCookie,
  themeFromCookieString,
  type ThemePreference,
} from '@/lib/theme/theme';

type ThemeContextValue = {
  preference: ThemePreference;
  setPreference: (preference: ThemePreference) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

/** Puts the preference on <html>; the CSS does the rest (System follows `prefers-color-scheme` with no script). */
export function applyTheme(preference: ThemePreference) {
  const root = document.documentElement;
  const attribute = themeAttribute(preference);
  if (attribute) {
    root.setAttribute('data-theme', attribute);
  } else {
    root.removeAttribute('data-theme');
  }
  document.querySelector('meta[name="color-scheme"]')?.setAttribute('content', colorScheme(preference));
}

/** Same-origin channel to the other open tabs; absent in very old browsers, where tabs catch up on their next load. */
function openChannel(): BroadcastChannel | null {
  return typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(THEME_CHANNEL);
}

/**
 * Holds the theme preference (D-124). `initial` comes from the cookie the server already rendered with, so the first
 * client render matches the HTML. A change is written to the cookie (persists across navigation and restarts) and
 * broadcast to every other open tab, which applies it at once. Web Storage is not used (spec §9 lint guard).
 */
export function ThemeProvider({ initial, children }: { initial: ThemePreference; children: ReactNode }) {
  const [preference, setState] = useState<ThemePreference>(initial);
  const channelRef = useRef<BroadcastChannel | null>(null);

  const setPreference = useCallback((next: ThemePreference) => {
    setState(next);
    applyTheme(next);
    document.cookie = themeCookie(next, window.location.protocol === 'https:');
    channelRef.current?.postMessage(next);
  }, []);

  useEffect(() => {
    const adopt = (next: ThemePreference) => {
      setState(next);
      applyTheme(next);
    };
    const channel = openChannel();
    channelRef.current = channel;
    if (channel) {
      channel.onmessage = (event: MessageEvent<unknown>) =>
        adopt(parseTheme(typeof event.data === 'string' ? event.data : null));
    }
    // A page restored from the back/forward cache missed any change made meanwhile; the cookie has the latest.
    const onPageShow = (event: PageTransitionEvent) => {
      if (event.persisted) adopt(themeFromCookieString(document.cookie));
    };
    window.addEventListener('pageshow', onPageShow);
    return () => {
      channel?.close();
      channelRef.current = null;
      window.removeEventListener('pageshow', onPageShow);
    };
  }, []);

  const value = useMemo(() => ({ preference, setPreference }), [preference, setPreference]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used inside <ThemeProvider>');
  }
  return context;
}
