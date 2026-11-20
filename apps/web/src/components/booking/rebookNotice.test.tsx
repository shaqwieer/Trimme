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

describe('the corner «أعد الحجز» notice (D-156)', () => {
  it('is one short line with the shop that opens the same booking, pinned under the header', async () => {
    notice();
    const aside = screen.getByRole('region', { name: 'أعد الحجز' });
    expect(aside).toHaveTextContent('أعجبك حجزك في صالون الأصالة للحلاقة؟');
    expect(screen.getByRole('link', { name: /أعد الحجز من هنا/ })).toHaveAttribute(
      'href',
      expect.stringContaining('/shops/al-asala/book?pro=p1'),
    );
    // Fixed in the logo's corner: it takes no room from the page, and it is not announced or focused.
    expect(aside.parentElement?.parentElement).toHaveClass('fixed', 'top-18');
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
