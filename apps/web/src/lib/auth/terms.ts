/**
 * This browser remembers that its customer accepted the terms (D-158, the client's request: once accepted, sign-up says
 * so instead of asking again). A flag only: no person, no number and no token, so it grants nothing. The page sends no
 * acceptance on its behalf: the server keeps the account's earlier one, and a new number is asked on profile completion.
 */
export const TERMS_COOKIE = 'trimme-terms';

/** Two years, the time a browser keeps it. */
const MAX_AGE_SECONDS = 2 * 365 * 24 * 60 * 60;

export function termsCookie(secure: boolean): string {
  return `${TERMS_COOKIE}=1; Path=/; Max-Age=${MAX_AGE_SECONDS}; SameSite=Lax${secure ? '; Secure' : ''}`;
}

/** Whether the cookie's value says the terms were accepted in this browser. */
export function termsRemembered(value: string | undefined): boolean {
  return value === '1';
}

/** Called once a signed-in customer's account holds an acceptance. */
export function rememberTermsAccepted(): void {
  document.cookie = termsCookie(window.location.protocol === 'https:');
}
