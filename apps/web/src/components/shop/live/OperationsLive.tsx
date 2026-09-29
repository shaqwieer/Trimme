'use client';

import { type HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useTranslations } from 'next-intl';
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
import { useRouter } from '@/i18n/navigation';
import { refreshSession } from '@/lib/api/client';
import { cn } from '@/lib/cn';

/** A change on the shop's board (D-099): ids, times and status only; pages refetch what they show. */
export type OperationsEvent = {
  kind: string;
  shopId: string;
  bookingId: string;
  professionalId: string;
  status: string;
  startsAt: string;
  endsAt: string;
};

export type LiveState = 'connecting' | 'live' | 'offline';

type Listener = (event: OperationsEvent) => void;

type LiveContextValue = { state: LiveState; subscribe: (listener: Listener) => () => void };

const LiveContext = createContext<LiveContextValue | null>(null);

export const HUB_PATH = '/hubs/operations';
export const HUB_EVENT = 'bookingChanged';

/** Waits 1 s, 2 s, 4 s… up to 30 s between reconnection attempts. */
export function reconnectDelay(attempt: number): number {
  return Math.min(30_000, 1_000 * 2 ** Math.min(Math.max(attempt, 0), 5));
}

/**
 * One live connection for the shop dashboard (spec §13, D-099). The server puts it in the shop's group from the
 * session. The connection closes when the short-lived access cookie expires; the provider then refreshes the session
 * (the refresh cookie is HttpOnly and scoped to the auth API) and connects again, backing off while it fails.
 */
export function OperationsLiveProvider({ children }: { children: ReactNode }) {
  const listeners = useRef(new Set<Listener>());
  const [state, setState] = useState<LiveState>('connecting');

  useEffect(() => {
    let stopped = false;
    let connection: HubConnection | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const retry = (attempt: number) => {
      if (stopped) return;
      timer = setTimeout(async () => {
        await refreshSession();
        void connect(attempt);
      }, reconnectDelay(attempt));
    };

    const connect = async (attempt: number) => {
      if (stopped) return;
      const current = new HubConnectionBuilder()
        .withUrl(HUB_PATH, { withCredentials: true })
        .configureLogging(LogLevel.None)
        .build();
      connection = current;
      current.on(HUB_EVENT, (event: OperationsEvent) => {
        listeners.current.forEach((listener) => listener(event));
      });
      current.onclose(() => {
        if (stopped || connection !== current) return;
        setState('offline');
        retry(0);
      });
      try {
        await current.start();
        if (stopped) {
          await current.stop();
          return;
        }
        setState('live');
      } catch {
        if (stopped) return;
        setState('offline');
        retry(attempt + 1);
      }
    };

    void connect(0);
    return () => {
      stopped = true;
      clearTimeout(timer);
      void connection?.stop();
    };
  }, []);

  const subscribe = useCallback((listener: Listener) => {
    listeners.current.add(listener);
    return () => {
      listeners.current.delete(listener);
    };
  }, []);
  const value = useMemo<LiveContextValue>(() => ({ state, subscribe }), [state, subscribe]);

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>;
}

/** Calls `listener` for every live change (nothing outside the provider). */
export function useOperationsEvents(listener: Listener) {
  const live = useContext(LiveContext);
  const latest = useRef(listener);
  useEffect(() => {
    latest.current = listener;
  });
  useEffect(() => live?.subscribe((event) => latest.current(event)), [live]);
}

export function useLiveState(): LiveState | null {
  return useContext(LiveContext)?.state ?? null;
}

/** Re-renders a server-rendered page (router.refresh) shortly after live changes, batching bursts. */
export function LiveRefresh({ delayMs = 400 }: { delayMs?: number }) {
  const router = useRouter();
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useOperationsEvents(() => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => router.refresh(), delayMs);
  });
  useEffect(() => () => clearTimeout(timer.current), []);
  return null;
}

/** "Live" / "Reconnecting…" next to the page title, announced politely. */
export function LiveIndicator({ className }: { className?: string }) {
  const t = useTranslations('shopBoard.live');
  const state = useLiveState();
  if (!state) return null;
  return (
    <span
      role="status"
      className={cn('inline-flex items-center gap-1.5 text-helper font-bold', className)}
      data-testid="live-indicator"
      data-state={state}
    >
      <span
        aria-hidden="true"
        className={cn(
          'size-2 rounded-full',
          state === 'live' ? 'bg-success-500' : state === 'connecting' ? 'bg-warning-500' : 'bg-danger-500',
        )}
      />
      <span className={state === 'live' ? 'text-success-700' : 'text-text-secondary'}>{t(state)}</span>
    </span>
  );
}
