import { describe, expect, it } from 'vitest';
import { isSignedInGated, readSignedInHint, signedInDestination, signedInUserType } from './signedIn';

describe('signed-in gate (D-144)', () => {
  it('gates only the landing page and the sign-in and sign-up steps', () => {
    for (const path of [
      '/',
      '/auth/sign-in',
      '/auth/sign-up',
      '/auth/verify',
      '/auth/staff/sign-in',
      '/auth/sign-in/',
    ]) {
      expect(isSignedInGated(path), path).toBe(true);
    }
    for (const path of [
      '/discover',
      '/auth/session',
      '/auth/complete-profile',
      '/auth/accept-invite',
      '/auth/reset-password',
      '/auth/forgot-password',
      '/terms',
      '/shops/barber-house',
      '/auth/sign-in-other',
    ]) {
      expect(isSignedInGated(path), path).toBe(false);
    }
  });

  it('sends the user through the session page to returnTo or their home', () => {
    expect(signedInDestination('Customer', undefined)).toBe('/auth/session?returnTo=%2Fdiscover');
    expect(signedInDestination('ShopUser', undefined)).toBe('/auth/session?returnTo=%2Fshop');
    expect(signedInDestination('PlatformAdmin', undefined)).toBe('/auth/session?returnTo=%2Fadmin');
    expect(signedInDestination('Customer', '/shops/x/book?step=review')).toBe(
      '/auth/session?returnTo=%2Fshops%2Fx%2Fbook%3Fstep%3Dreview',
    );
  });

  it('reads only the user types the API writes', () => {
    expect(readSignedInHint('trimme-csrf=abc; trimme-signed-in=ShopUser; trimme-theme=dark')).toBe(
      'ShopUser',
    );
    expect(readSignedInHint('trimme-csrf=abc')).toBeUndefined();
    expect(readSignedInHint('trimme-signed-in=%2F%2Fevil')).toBeUndefined();
    expect(signedInUserType('Customer')).toBe('Customer');
    expect(signedInUserType('customer')).toBeUndefined();
    expect(signedInUserType(undefined)).toBeUndefined();
  });
});
