import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import {
  parseRebookDismissed,
  REBOOK_DISMISSED_COOKIE,
  rebookDismissedCookie,
  rebookDismissedFromCookies,
} from '@/lib/booking/rebookNotice';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { RebookNotice } from './RebookNotice';

const VISIT = '0f8fad5b-d9cb-469f-a165-70867728950e';
const notice = () =>
  renderWithIntl(
    <RebookNotice bookingId={VISIT} shop="صالون الأصالة للحلاقة" href="/shops/al-asala/book?pro=p1" />,
    { locale: 'ar' },
  );

afterEach(() => {
  document.cookie = `${REBOOK_DISMISSED_COOKIE}=; Path=/; Max-Age=0`;
});

describe('the corner «أعد الحجز» notice (D-156, D-160)', () => {
  it('reads the client’s short text, opens the same booking, and sits over the logo in the header', async () => {
    notice();
    // The shop is no longer printed (D-160); it stays in the notice's name.
    const aside = screen.getByRole('region', { name: 'أعد الحجز في صالون الأصالة للحلاقة' });
    expect(aside).toHaveTextContent('أعجبتك حلاقتك الأخيرة؟أعد الحجز هنا');
    expect(aside).not.toHaveTextContent('صالون الأصالة');
    expect(screen.getByRole('link', { name: /أعد الحجز هنا/ })).toHaveAttribute(
      'href',
      expect.stringContaining('/shops/al-asala/book?pro=p1'),
    );
    // Fixed in the logo's corner inside the 64 px header on phones (48 px tall from 8 px), under the header from
    // 1024 px where the menu sits next to the logo; narrower than the screen less the theme switch and the bell.
    expect(aside.parentElement?.parentElement).toHaveClass('fixed', 'top-2', 'lg:top-18');
    expect(aside).toHaveClass('h-12', 'max-w-[min(15rem,calc(100vw-8.5rem))]');
    // The ✕ keeps the 44 px touch width.
    expect(screen.getByRole('button', { name: 'إغلاق' })).toHaveClass('w-11');
    // It takes no room from the page, and it is not announced or focused.
    expect(aside).not.toHaveAttribute('aria-live');
    expect(document.activeElement).toBe(document.body);
    await expectNoAxeViolations();
  });

  it('its ✕ closes it and remembers this visit', async () => {
    const user = userEvent.setup();
    notice();
    await user.click(screen.getByRole('button', { name: 'إغلاق' }));
    expect(screen.queryByTestId('rebook-notice')).not.toBeInTheDocument();
    expect(document.cookie).toContain(`${REBOOK_DISMISSED_COOKIE}=${VISIT}`);
  });

  // Going back to the home page replays the router's cached page, which still has the notice (seen in the browser).
  it('stays closed when it mounts again after its ✕, and a newer visit shows it again', () => {
    document.cookie = rebookDismissedCookie(VISIT, false);
    const { unmount } = notice();
    expect(screen.queryByTestId('rebook-notice')).not.toBeInTheDocument();
    unmount();

    document.cookie = rebookDismissedCookie('7c9e6679-7425-40de-944b-e07fc1f90ae7', false);
    notice();
    expect(screen.getByTestId('rebook-notice')).toBeInTheDocument();
  });

  it('the cookie holds a booking id only, for 30 days', () => {
    expect(rebookDismissedCookie(VISIT, true)).toBe(
      `${REBOOK_DISMISSED_COOKIE}=${VISIT}; Path=/; Max-Age=2592000; SameSite=Lax; Secure`,
    );
    expect(parseRebookDismissed(VISIT)).toBe(VISIT);
    expect(parseRebookDismissed('not-an-id')).toBeNull();
    expect(parseRebookDismissed(undefined)).toBeNull();
    expect(rebookDismissedFromCookies(`a=1; ${REBOOK_DISMISSED_COOKIE}=${VISIT}; b=2`)).toBe(VISIT);
    expect(rebookDismissedFromCookies('a=1')).toBeNull();
  });
});
