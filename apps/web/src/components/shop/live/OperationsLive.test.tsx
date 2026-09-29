import { act, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { LiveIndicator, OperationsLiveProvider, STABLE_CONNECTION_MS } from './OperationsLive';

type FakeConnection = {
  start: ReturnType<typeof vi.fn>;
  stop: ReturnType<typeof vi.fn>;
  on: ReturnType<typeof vi.fn>;
  close: () => void;
};

const hub = vi.hoisted(() => ({ connections: [] as FakeConnection[], failStarts: false }));
const session = vi.hoisted(() => ({ refreshSession: vi.fn<() => Promise<boolean>>() }));

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      let onClose: (() => void) | undefined;
      const connection: FakeConnection = {
        start: vi.fn(() =>
          hub.failStarts ? Promise.reject(new Error('negotiate failed')) : Promise.resolve(),
        ),
        stop: vi.fn(() => Promise.resolve()),
        on: vi.fn(),
        close: () => onClose?.(),
      };
      hub.connections.push(connection);
      return { ...connection, onclose: (handler: () => void) => (onClose = handler) };
    }
  }
  return { HubConnectionBuilder, LogLevel: { None: 6 } };
});
vi.mock('@/lib/api/client', () => session);
vi.mock('@/i18n/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

const latest = () => hub.connections[hub.connections.length - 1]!;
const indicator = () => screen.getByTestId('live-indicator').getAttribute('data-state');

/** Advances to just before, then exactly to, the expected delay and checks that only then a new connection starts. */
async function expectReconnectAfter(ms: number) {
  const before = hub.connections.length;
  await act(() => vi.advanceTimersByTimeAsync(ms - 1));
  expect(hub.connections.length, `no reconnect before ${ms} ms`).toBe(before);
  await act(() => vi.advanceTimersByTimeAsync(1));
  expect(hub.connections.length, `reconnect at ${ms} ms`).toBe(before + 1);
}

describe('OperationsLiveProvider reconnection', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    hub.connections.length = 0;
    hub.failStarts = false;
    session.refreshSession.mockReset().mockResolvedValue(true);
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  async function mount() {
    const view = renderWithIntl(
      <OperationsLiveProvider>
        <LiveIndicator />
      </OperationsLiveProvider>,
    );
    await act(() => vi.advanceTimersByTimeAsync(0));
    return view;
  }

  it('backs off when the server closes every connection right after it opens, instead of spinning', async () => {
    await mount();
    expect(indicator()).toBe('live');

    for (const delay of [1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000]) {
      await act(async () => latest().close());
      expect(indicator()).toBe('offline');
      await expectReconnectAfter(delay);
      expect(indicator()).toBe('live');
    }
    // One refresh per reconnection, never more.
    expect(session.refreshSession).toHaveBeenCalledTimes(7);
  });

  it('starts the backoff again once a connection has stayed up', async () => {
    await mount();
    await act(async () => latest().close());
    await expectReconnectAfter(1_000);
    await act(async () => latest().close());
    await expectReconnectAfter(2_000);

    await act(() => vi.advanceTimersByTimeAsync(STABLE_CONNECTION_MS));
    await act(async () => latest().close());
    await expectReconnectAfter(1_000);
  });

  it('backs off on failed starts too', async () => {
    await mount();
    hub.failStarts = true;
    await act(async () => latest().close());
    for (const delay of [1_000, 2_000, 4_000, 8_000]) {
      await expectReconnectAfter(delay);
      expect(indicator()).toBe('offline');
    }
    expect(session.refreshSession).toHaveBeenCalledTimes(4);
  });

  it('stops when the session cannot be refreshed', async () => {
    await mount();
    session.refreshSession.mockResolvedValue(false);
    await act(async () => latest().close());
    await act(() => vi.advanceTimersByTimeAsync(60_000));

    expect(session.refreshSession).toHaveBeenCalledTimes(1);
    expect(hub.connections).toHaveLength(1);
    expect(indicator()).toBe('offline');
  });

  it('stops everything when unmounted', async () => {
    const view = await mount();
    const current = latest();
    await act(async () => current.close());
    view.unmount();
    await act(() => vi.advanceTimersByTimeAsync(60_000));

    expect(session.refreshSession).not.toHaveBeenCalled();
    expect(hub.connections).toHaveLength(1);
  });
});
