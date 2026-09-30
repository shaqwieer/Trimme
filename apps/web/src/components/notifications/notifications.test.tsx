import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Notice } from '@/lib/notifications/notices';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { AdminNotificationBell, NotificationBell } from './NotificationBell';
import { NotificationsPanel } from './NotificationsPanel';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));
vi.mock('./useNotificationSignal', () => ({ useNotificationSignal: vi.fn() }));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });
const fail = (status: number) => ({
  error: { status, errorCode: 'server.unexpected' },
  response: new Response(null, { status }),
});

const notice = (overrides: Partial<Notice>): Notice => ({
  id: 'n1',
  kind: 'booking.created',
  parameters: {
    customerName: 'نورة',
    itemNameAr: 'قص شعر',
    itemNameEn: 'Haircut',
    professionalNameAr: 'فيصل',
    professionalNameEn: 'Faisal',
    startsAt: '2026-09-18T14:30:00Z',
  },
  bookingId: 'b1',
  createdAt: '2026-09-17T09:00:00Z',
  readAt: null,
  ...overrides,
});

const unread = (count: number) =>
  vi.stubGlobal(
    'fetch',
    vi.fn(() => Promise.resolve(new Response(JSON.stringify({ unread: count }), { status: 200 }))),
  );

beforeEach(() => {
  api.GET.mockReset();
  api.POST.mockReset();
  vi.unstubAllGlobals();
});

describe('NotificationsPanel', () => {
  it('renders each notice in the reader language without any phone number, and marks one read', async () => {
    api.POST.mockResolvedValue(ok(undefined));
    const { container } = renderWithIntl(
      <NotificationsPanel
        audience="shop"
        unread={1}
        items={[notice({}), notice({ id: 'n2', kind: 'booking.cancelled', readAt: '2026-09-17T10:00:00Z' })]}
      />,
    );
    const items = screen.getAllByTestId('notification');
    expect(items[0]).toHaveTextContent(
      'حجز جديد من نورة: قص شعر مع فيصل يوم الجمعة، ١٨ سبتمبر الساعة ٥:٣٠ م',
    );
    expect(items[0]).toHaveAttribute('data-read', 'false');
    expect(items[1]).toHaveAttribute('data-read', 'true');
    expect(container.textContent).not.toMatch(/\+966|05\d{8}/);

    await userEvent.click(within(items[0]!).getByRole('button', { name: 'تعليم كمقروء' }));
    await waitFor(() => expect(items[0]).toHaveAttribute('data-read', 'true'));
    expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/notifications/{notificationId}/read', {
      params: { path: { notificationId: 'n1' } },
    });
    await expectNoAxeViolations(container);
  });

  it('rolls an optimistic mark-read back when the API refuses', async () => {
    api.POST.mockResolvedValue(fail(503));
    renderWithIntl(
      <NotificationsPanel audience="customer" unread={1} items={[notice({ kind: 'booking.confirmed' })]} />,
      {
        locale: 'en',
      },
    );
    const item = screen.getByTestId('notification');
    await userEvent.click(within(item).getByRole('button', { name: 'Mark as read' }));
    await waitFor(() =>
      expect(screen.getByText('The change could not be saved. Try again.')).toBeInTheDocument(),
    );
    expect(item).toHaveAttribute('data-read', 'false');
    expect(api.POST.mock.calls[0]?.[0]).toBe('/api/v1/me/notifications/{notificationId}/read');
  });

  it('marks everything read and shows an empty state when there is nothing', async () => {
    api.POST.mockResolvedValue(ok(undefined));
    renderWithIntl(
      <NotificationsPanel audience="shop" unread={2} items={[notice({}), notice({ id: 'n2' })]} />,
      { locale: 'en' },
    );
    await userEvent.click(screen.getByRole('button', { name: 'Mark all as read' }));
    await waitFor(() =>
      expect(screen.getAllByTestId('notification').every((n) => n.dataset.read === 'true')).toBe(true),
    );
    expect(api.POST).toHaveBeenCalledWith('/api/v1/shop/notifications/read-all');

    renderWithIntl(<NotificationsPanel audience="shop" unread={0} items={[]} />, { locale: 'en' });
    expect(screen.getByText('No notifications yet')).toBeInTheDocument();
  });
});

describe('NotificationBell', () => {
  it('shows the unread count in its accessible name and links to the page', async () => {
    unread(3);
    renderWithIntl(<NotificationBell scope="shop" href="/shop/notifications" />, { locale: 'en' });
    const bell = screen.getByTestId('notification-bell');
    await waitFor(() => expect(bell).toHaveAccessibleName('Notifications, 3 unread'));
    expect(screen.getByTestId('unread-badge')).toHaveTextContent('3');
    expect(bell).toHaveAttribute('href', expect.stringContaining('/shop/notifications'));
    expect(fetch).toHaveBeenCalledWith('/api/v1/shop/notifications/unread-count', expect.anything());
  });

  it('hides the badge at zero', async () => {
    unread(0);
    renderWithIntl(<NotificationBell scope="me" href="/account/notifications" />, { locale: 'en' });
    await waitFor(() =>
      expect(fetch).toHaveBeenCalledWith('/api/v1/me/notifications/unread-count', expect.anything()),
    );
    expect(screen.queryByTestId('unread-badge')).toBeNull();
    expect(screen.getByTestId('notification-bell')).toHaveAccessibleName('Notifications');
  });

  it('lists the admin alerts in a menu with mark-all', async () => {
    unread(1);
    api.GET.mockImplementation(() =>
      Promise.resolve(
        ok({
          items: [
            notice({
              id: 'a1',
              kind: 'whatsapp.dispatch_failed',
              parameters: { event: 'BookingConfirmed' },
              bookingId: null,
            }),
          ],
          page: 1,
          pageSize: 6,
          total: 1,
          unread: 1,
        }),
      ),
    );
    api.POST.mockResolvedValue(ok(undefined));
    renderWithIntl(<AdminNotificationBell />, { locale: 'en' });
    const bell = screen.getByTestId('notification-bell');
    await waitFor(() => expect(bell).toHaveAccessibleName('Notifications, 1 unread'));
    await userEvent.click(bell);
    expect(
      await screen.findByText('A WhatsApp message could not be sent (BookingConfirmed)'),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Mark all as read' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledWith('/api/v1/me/notifications/read-all'));
  });
});
