import { describe, expect, it } from 'vitest';
import { ApiError, fallbackCode, toApiError } from './problem';

describe('API problem details', () => {
  it('maps a problem response to a typed error with field errors', async () => {
    const response = new Response(
      JSON.stringify({
        status: 400,
        title: 'One or more validation errors occurred.',
        errorCode: 'validation.failed',
        correlationId: 'abc12345',
        errors: { name: ['name.required'] },
      }),
      { status: 400, headers: { 'content-type': 'application/problem+json' } },
    );

    const error = await toApiError(response);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.isValidation).toBe(true);
    expect(error.fieldErrors).toEqual({ name: ['name.required'] });
    expect(error.correlationId).toBe('abc12345');
  });

  it('falls back to the status mapping when the body is not JSON', async () => {
    const error = await toApiError(new Response('<html>bad gateway</html>', { status: 502 }));
    expect(error.errorCode).toBe('server.unexpected');
    expect(error.fieldErrors).toEqual({});
  });

  it('mirrors the API status → code fallbacks', () => {
    expect(fallbackCode(401)).toBe('auth.unauthenticated');
    expect(fallbackCode(403)).toBe('auth.forbidden');
    expect(fallbackCode(404)).toBe('resource.not_found');
    expect(fallbackCode(409)).toBe('resource.conflict');
    expect(fallbackCode(429)).toBe('rate_limit.exceeded');
    expect(fallbackCode(400)).toBe('request.invalid');
  });

  it('exposes convenience flags', () => {
    expect(new ApiError(401, undefined).isUnauthenticated).toBe(true);
    expect(new ApiError(403, undefined).isForbidden).toBe(true);
    expect(new ApiError(409, { errorCode: 'booking.slot_unavailable' }).isConflict).toBe(true);
  });
});
