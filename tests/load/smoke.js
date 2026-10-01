// k6 smoke test (Phase 17): availability reads and the booking path, against a local stack with the demo data.
//   docker run --rm -i -e BASE_URL=http://host.docker.internal:3300 grafana/k6 run - < tests/load/smoke.js
// Development only: it signs up customers through the dev OTP inbox and books, then cancels, real demo slots.
import http from 'k6/http';
import { check, fail, sleep } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:3300';
const SHOP = 'barber-house';
const SERVICE = '0199a0de-5a10-7000-8000-000000000411'; // Cut & style
const OMAR = '0199a0de-5a10-7000-8000-000000000201';

export const options = {
  scenarios: {
    availability: {
      executor: 'constant-vus',
      vus: 5,
      duration: '30s',
      exec: 'availability',
    },
    booking: {
      executor: 'per-vu-iterations',
      vus: 3,
      iterations: 2,
      maxDuration: '60s',
      exec: 'booking',
    },
  },
  thresholds: {
    'http_req_failed{scenario:availability}': ['rate<0.01'],
    'http_req_duration{scenario:availability}': ['p(95)<500'],
    'http_req_duration{name:create booking}': ['p(95)<1000'],
    checks: ['rate>0.99'],
  },
};

function riyadhDate(days) {
  const now = new Date(Date.now() + 3 * 3600 * 1000 + days * 86400 * 1000);
  return now.toISOString().slice(0, 10);
}

export function availability() {
  const date = riyadhDate(1 + (__ITER % 5));
  const slots = http.get(`${BASE}/api/v1/public/shops/${SHOP}/availability/slots?serviceId=${SERVICE}&date=${date}`, {
    tags: { name: 'slots' },
  });
  check(slots, { 'slots 200': (r) => r.status === 200 });
  const dates = http.get(`${BASE}/api/v1/public/shops/${SHOP}/availability/dates?serviceId=${SERVICE}`, {
    tags: { name: 'dates' },
  });
  check(dates, { 'dates 200': (r) => r.status === 200 });
  // A visitor's pace: each VU about two requests a second (an unpaced loop only measures the rate limiter's 429s).
  sleep(1);
}

/**
 * A minimal cookie jar. The API's cookies are `Secure`, and k6 (rightly) does not send them over plain HTTP to a host
 * other than localhost, so the smoke test keeps them itself.
 */
function session() {
  const cookies = {};
  const keep = (response) => {
    for (const [name, values] of Object.entries(response.cookies)) {
      if (values.length > 0) cookies[name] = values[0].value;
    }
    return response;
  };
  const headers = (extra = {}) => ({
    'Content-Type': 'application/json',
    Cookie: Object.entries(cookies).map(([k, v]) => `${k}=${v}`).join('; '),
    'X-CSRF-Token': decodeURIComponent(cookies['trimme-csrf'] || ''),
    ...extra,
  });
  return {
    get: (url, tags) => keep(http.get(url, { headers: headers(), tags })),
    post: (url, body, tags, extra) => keep(http.post(url, JSON.stringify(body), { headers: headers(extra), tags })),
  };
}

export function booking() {
  const s = session();
  const phone = `+9665${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;
  s.get(`${BASE}/api/v1/auth/csrf`, { name: 'csrf' });
  const requested = s.post(`${BASE}/api/v1/auth/otp/request`, { phone, termsAccepted: true, locale: 'ar' }, { name: 'otp request' });
  if (!check(requested, { 'otp requested': (r) => r.status === 202 })) fail(`otp ${requested.status}`);
  const code = s.get(`${BASE}/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(phone)}`).json('code');
  const verified = s.post(`${BASE}/api/v1/auth/otp/verify`, { challengeId: requested.json('challengeId'), code }, { name: 'otp verify' });
  check(verified, { 'otp verified': (r) => r.status === 200 });
  const completed = s.post(`${BASE}/api/v1/auth/profile/complete`, { displayName: 'اختبار حمل', preferredLocale: 'ar', termsAccepted: true }, { name: 'profile' });
  check(completed, { 'profile completed': (r) => r.status === 200 });

  // Each VU books its own days, so this measures an uncontended booking. Two customers racing for overlapping times of
  // one barber are decided by the exclusion constraint (D-089); see docs/performance.md for that case.
  for (const days of [10 + __VU * 3, 11 + __VU * 3, 12 + __VU * 3]) {
    const slots = s
      .get(`${BASE}/api/v1/public/shops/${SHOP}/availability/slots?serviceId=${SERVICE}&professionalId=${OMAR}&date=${riyadhDate(days)}`, { name: 'slots' })
      .json('slots');
    if (!slots || slots.length === 0) continue;
    const slot = slots[Math.floor(Math.random() * slots.length)];
    const created = s.post(
      `${BASE}/api/v1/bookings`,
      { shopSlug: SHOP, serviceId: SERVICE, professionalId: OMAR, startsAt: slot.startsAt },
      { name: 'create booking' },
      { 'Idempotency-Key': `${phone}-${days}` },
    );
    if (created.status === 409) continue; // another VU took the time: the double-booking guard at work
    check(created, { 'booking created': (r) => r.status === 201 });
    const cancelled = s.post(`${BASE}/api/v1/me/bookings/${created.json('id')}/cancel`, { reason: null, version: created.json('version') }, { name: 'cancel booking' });
    check(cancelled, { 'booking cancelled': (r) => r.status === 200 });
    return;
  }
  fail(`no free time for Omar on VU ${__VU}'s days`);
}
