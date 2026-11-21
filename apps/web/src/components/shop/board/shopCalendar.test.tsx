import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import {
  CALENDAR_ROWS_COOKIE,
  calendarRowsCookie,
  calendarRowsFromCookies,
  parseCalendarRows,
} from '@/lib/shop/calendarRows';
import { ShopCalendar } from './ShopCalendar';

const api = vi.hoisted(() => ({ GET: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api, refreshSession: vi.fn() }));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

const TZ = 'Asia/Riyadh';
// 2 pm to 6 pm in Riyadh (UTC+3), one barber, one booking at 3:30.
const day = (date: string) => ({
  date,
  closed: false,
  open: [{ start: `${date}T11:00:00Z`, end: `${date}T15:00:00Z` }],
  lanes: [
    {
      professionalId: 'p1',
      working: [{ start: `${date}T11:00:00Z`, end: `${date}T15:00:00Z` }],
      breaks: [],
      timeOff: [],
    },
  ],
  bookings: [
    {
      id: 'b1',
      reference: 'GH-1',
      professionalId: 'p1',
      startsAt: `${date}T12:30:00Z`,
      endsAt: `${date}T13:00:00Z`,
      customerName: 'Sulaiman A',
      itemNameAr: 'حلاقة شعر',
      itemNameEn: 'Haircut',
      status: 'Confirmed',
      channel: 'Online',
    },
  ],
});

function renderCalendar(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const calendar = (props: { initialRowMinutes?: 30 | 60 } = {}) => (
  <ShopCalendar today="2026-11-20" timeZone={TZ} canWalkIn={false} canUpdate={false} {...props} />
);

const labels = () => within(screen.getByTestId('calendar-hours')).getAllByText(/.+/);

beforeEach(() => {
  api.GET.mockReset();
  api.GET.mockImplementation(
    async (_path: string, init: { params: { query: { from: string; to: string } } }) => {
      const { from } = init.params.query;
      return ok({
        from,
        to: init.params.query.to,
        timeZone: TZ,
        professionals: [{ id: 'p1', nameAr: 'محمد', nameEn: 'Mohammed', isActive: true }],
        days: [day(from)],
      });
    },
  );
  window.history.replaceState(null, '', '/ar/shop/calendar');
});

afterEach(() => {
  document.cookie = `${CALENDAR_ROWS_COOKIE}=; Path=/; Max-Age=0`;
});

describe('the calendar’s half-hour rows (D-160)', () => {
  it('labels every hour by default, and every half hour with the switch on, each row keeping its height', async () => {
    const user = userEvent.setup();
    renderCalendar(calendar());
    const column = await screen.findByTestId('calendar-column');

    // 2 pm to 6 pm: four hour rows of 64 px.
    expect(labels().map((l) => l.textContent)).toEqual(['٢:٠٠ م', '٣:٠٠ م', '٤:٠٠ م', '٥:٠٠ م']);
    expect(screen.getByTestId('calendar-hours')).toHaveStyle({ height: '256px' });
    // The sticky axis stays over the bookings (z-10) and under the shell's header (z-20) on a long day.
    expect(screen.getByTestId('calendar-hours').parentElement).toHaveClass('sticky', 'z-15');
    const grid = column.lastElementChild as HTMLElement;
    expect(grid).toHaveStyle({ height: '256px' });

    const toggle = screen.getByRole('switch', { name: 'كل نصف ساعة' });
    expect(toggle).toHaveAttribute('aria-checked', 'false');
    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-checked', 'true');
    expect(labels().map((l) => l.textContent)).toEqual([
      '٢:٠٠ م',
      '٢:٣٠ م',
      '٣:٠٠ م',
      '٣:٣٠ م',
      '٤:٠٠ م',
      '٤:٣٠ م',
      '٥:٠٠ م',
      '٥:٣٠ م',
    ]);
    // Eight half-hour rows of 64 px: the day is twice as tall, and the 3:30 booking starts on its own row's line.
    expect(screen.getByTestId('calendar-hours')).toHaveStyle({ height: '512px' });
    expect(grid).toHaveStyle({ height: '512px' });
    expect(screen.getByTestId('calendar-booking')).toHaveStyle({ top: '37.5%' });
    // Remembered on this device.
    expect(calendarRowsFromCookies(document.cookie)).toBe(30);

    await user.click(toggle);
    expect(labels()).toHaveLength(4);
    expect(calendarRowsFromCookies(document.cookie)).toBe(60);
  });

  it('opens on the device’s remembered half hours, on the week too', async () => {
    window.history.replaceState(null, '', '/ar/shop/calendar?view=week');
    document.cookie = calendarRowsCookie(30, false);
    renderCalendar(calendar({ initialRowMinutes: 30 }));
    await screen.findByTestId('calendar-week');
    expect(screen.getByRole('switch', { name: 'كل نصف ساعة' })).toHaveAttribute('aria-checked', 'true');
    expect(labels()).toHaveLength(8);
  });

  it('the cookie holds 30 or 60 only, for a year', () => {
    expect(calendarRowsCookie(30, true)).toBe(
      `${CALENDAR_ROWS_COOKIE}=30; Path=/; Max-Age=31536000; SameSite=Lax; Secure`,
    );
    expect(parseCalendarRows('30')).toBe(30);
    expect(parseCalendarRows('15')).toBe(60);
    expect(parseCalendarRows(undefined)).toBe(60);
    expect(calendarRowsFromCookies(`a=1; ${CALENDAR_ROWS_COOKIE}=30; b=2`)).toBe(30);
    expect(calendarRowsFromCookies('a=1')).toBe(60);
  });
});
