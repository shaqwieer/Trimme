'use client';

import { type HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useEffect, useRef } from 'react';
import { reconnectDelay, STABLE_CONNECTION_MS } from '@/components/shop/live/OperationsLive';
import { refreshSessionOutcome } from '@/lib/api/client';

export const NOTIFICATIONS_HUB_PATH = '/hubs/notifications';
export const NOTIFICATIONS_EVENT = 'notificationsChanged';

/**
 * Calls `onSignal` whenever the server says the signed-in user (or their shop) has new notifications (D-112). The
 * signal carries nothing; callers refetch through the API. Reconnects with the same backoff as the operations
 * connection (1 s … 30 s), refreshing the session first, and stops when the refresh is refused.
 */
export function useNotificationSignal(onSignal: () => void, enabled = true) {
  const latest = useRef(onSignal);
  useEffect(() => {
    latest.current = onSignal;
  });

  useEffect(() => {
    if (!enabled) return;
    let stopped = false;
    let connection: HubConnection | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let failures = 0;
    let connectedAt = 0;

    const retry = () => {
      if (stopped) return;
      timer = setTimeout(
        async () => {
          if (stopped) return;
          const outcome = await refreshSessionOutcome();
          if (stopped || outcome === 'expired') return;
          if (outcome === 'unavailable') {
            failures += 1;
            retry();
            return;
          }
          void connect();
        },
        reconnectDelay(failures - 1),
      );
    };

    const connect = async () => {
      if (stopped) return;
      const current = new HubConnectionBuilder()
        .withUrl(NOTIFICATIONS_HUB_PATH, { withCredentials: true })
        .configureLogging(LogLevel.None)
        .build();
      connection = current;
      current.on(NOTIFICATIONS_EVENT, () => latest.current());
      current.onclose(() => {
        if (stopped || connection !== current) return;
        failures = Date.now() - connectedAt >= STABLE_CONNECTION_MS ? 1 : failures + 1;
        retry();
      });
      try {
        await current.start();
        if (stopped) {
          await current.stop();
          return;
        }
        connectedAt = Date.now();
        // Anything that arrived while disconnected is picked up by one refetch.
        latest.current();
      } catch {
        if (stopped) return;
        failures += 1;
        retry();
      }
    };

    void connect();
    return () => {
      stopped = true;
      clearTimeout(timer);
      void connection?.stop();
    };
  }, [enabled]);
}
