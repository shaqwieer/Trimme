/**
 * The corner «أعد الحجز» notice on the customer's home (D-156). Its ✕ remembers the visit it was about in a first-party
 * cookie, so the notice stays closed until a newer completed visit, or for 30 days. Not HttpOnly: the page writes it
 * on the ✕, and it holds only a booking id, never a token or a phone number. Pure, so the server page and the client
 * button share it.
 */
export const REBOOK_DISMISSED_COOKIE = 'trimme_rebook_dismissed';

/** 30 days: after a month with no new visit, the reminder may come back. */
export const REBOOK_DISMISSED_MAX_AGE = 60 * 60 * 24 * 30;

const BOOKING_ID = /^[0-9a-f-]{36}$/i;

/** The `document.cookie` assignment for a closed notice. */
export function rebookDismissedCookie(bookingId: string, secure: boolean): string {
  return [
    `${REBOOK_DISMISSED_COOKIE}=${bookingId}`,
    'Path=/',
    `Max-Age=${REBOOK_DISMISSED_MAX_AGE}`,
    'SameSite=Lax',
    ...(secure ? ['Secure'] : []),
  ].join('; ');
}

/** The closed visit's id from the cookie's value; anything else counts as none. */
export function parseRebookDismissed(value: string | undefined | null): string | null {
  return value && BOOKING_ID.test(value) ? value : null;
}
