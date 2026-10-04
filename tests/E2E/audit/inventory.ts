import type { APIRequestContext, BrowserContext } from '@playwright/test';

/**
 * Every page route of the web app (apps/web/src/app/[locale]/**), as the role that can open it, with seeded ids
 * (DemoData, the module seeders). The route audit (Phase 17) visits each in both locales.
 * `indexable` pages must not carry `noindex`; every other page must (R-NEG-10).
 */
export type Role = 'anonymous' | 'customer' | 'owner' | 'admin';

export type AuditRoute = { path: string; indexable?: boolean; map?: boolean };

export const DEMO_PASSWORD = 'trimme local demo';
export const ADMIN = { email: 'admin@trimme.local', password: 'trimme local admin' };
export const OWNER = { email: 'owner@barber-house.trimme.local', password: DEMO_PASSWORD };

const id = (suffix: string) => `0199a0de-5a10-7000-8000-${suffix.padStart(12, '0')}`;

export const SEED = {
  alAsala: { id: id('1'), slug: 'al-asala' },
  barberHouse: { id: id('2'), slug: 'barber-house' },
  faisal: id('101'),
  omar: { id: id('201'), slug: 'omar' },
  haircut: id('401'),
  cutAndStyle: id('411'),
  barberHousePackage: id('511'),
  monthlyPlan: id('601'),
  noura: id('901'),
  nouraBooking: id('a11'),
  alAsalaQr: { id: id('c001'), code: 'aswn7qkd' },
  barberHouseQr: { id: id('c004'), code: 'bhsh5mzc' },
  retiredQr: 'bhxx8dfg',
};

/** Ids that only exist at run time; the audit reads them from the API before visiting. */
export type RuntimeIds = {
  roleId: string;
  templateId: string;
  dispatchId: string;
  bookingId: string;
};

export function routes(role: Role, ids: RuntimeIds): AuditRoute[] {
  switch (role) {
    case 'anonymous':
      return [
        { path: '/', indexable: true },
        { path: '/shops', indexable: true },
        { path: `/shops/${SEED.alAsala.slug}`, indexable: true },
        { path: `/shops/${SEED.barberHouse.slug}`, indexable: true },
        { path: `/shops/${SEED.barberHouse.slug}/professionals/${SEED.omar.slug}`, indexable: true },
        { path: '/privacy', indexable: true },
        { path: '/terms', indexable: true },
        { path: `/shops/${SEED.barberHouse.slug}/book` },
        { path: '/discover' },
        { path: '/search' },
        { path: '/search?view=map', map: true },
        { path: '/onboarding/location', map: true },
        { path: `/q/${SEED.alAsalaQr.code}` },
        { path: `/q/${SEED.retiredQr}` },
        { path: '/auth/sign-in' },
        { path: '/auth/sign-up' },
        { path: '/auth/verify' },
        { path: '/auth/staff/sign-in' },
        { path: '/auth/forgot-password' },
        { path: '/auth/reset-password' },
        { path: '/auth/accept-invite' },
        { path: '/auth/session' },
        { path: '/no-such-page' },
      ];
    case 'customer':
      return [
        { path: '/account' },
        { path: '/account/bookings' },
        { path: `/account/bookings/${ids.bookingId}` },
        { path: `/account/bookings/${ids.bookingId}/reschedule` },
        { path: `/account/bookings/${ids.bookingId}/review` },
        { path: '/account/favorites' },
        { path: '/account/notifications' },
        { path: '/account/profile' },
        { path: '/account/security' },
        { path: '/auth/complete-profile' },
      ];
    case 'owner':
      return [
        { path: '/shop' },
        { path: '/shop/appointments' },
        { path: '/shop/calendar' },
        { path: '/shop/calendar?view=week' },
        { path: '/shop/walk-in' },
        { path: '/shop/schedule' },
        { path: '/shop/services' },
        { path: '/shop/services/new' },
        { path: `/shop/services/${SEED.cutAndStyle}` },
        { path: '/shop/packages/new' },
        { path: `/shop/packages/${SEED.barberHousePackage}` },
        { path: '/shop/settings' },
        { path: '/shop/settings/location', map: true },
        { path: '/shop/subscription' },
        { path: '/shop/notifications' },
        { path: '/shop/qr' },
        { path: `/shop/qr/${SEED.barberHouseQr.id}/poster` },
      ];
    case 'admin':
      return [
        { path: '/admin' },
        { path: '/admin/shops' },
        { path: '/admin/shops/new', map: true },
        { path: `/admin/shops/${SEED.alAsala.id}` },
        { path: '/admin/professionals' },
        { path: '/admin/professionals/new' },
        { path: `/admin/professionals/${SEED.faisal}` },
        { path: '/admin/services' },
        { path: '/admin/services/categories' },
        { path: `/admin/services/${SEED.haircut}` },
        { path: `/admin/services/new?shopId=${SEED.alAsala.id}` },
        { path: '/admin/packages' },
        { path: '/admin/bookings' },
        { path: `/admin/bookings/${SEED.nouraBooking}` },
        { path: '/admin/customers' },
        { path: `/admin/customers/${SEED.noura}` },
        { path: '/admin/subscriptions' },
        { path: '/admin/subscription-plans' },
        { path: '/admin/subscription-plans/new' },
        { path: `/admin/subscription-plans/${SEED.monthlyPlan}` },
        { path: '/admin/reviews' },
        { path: '/admin/qr' },
        { path: `/admin/qr/${SEED.alAsalaQr.id}/poster` },
        { path: '/admin/whatsapp/templates' },
        { path: `/admin/whatsapp/templates/${ids.templateId}` },
        { path: '/admin/whatsapp/dispatches' },
        { path: `/admin/whatsapp/dispatches/${ids.dispatchId}` },
        { path: '/admin/roles' },
        { path: `/admin/roles/${ids.roleId}` },
        { path: '/admin/roles/staff' },
        { path: '/admin/audit' },
        { path: '/admin/settings' },
      ];
  }
}

export async function csrf(context: BrowserContext, request: APIRequestContext): Promise<string> {
  await request.get('/api/v1/auth/csrf');
  return decodeURIComponent((await context.cookies()).find((c) => c.name === 'trimme-csrf')!.value);
}

export async function staffSignIn(
  context: BrowserContext,
  request: APIRequestContext,
  email: string,
  password: string,
) {
  const response = await request.post('/api/v1/auth/staff/sign-in', {
    headers: { 'X-CSRF-Token': await csrf(context, request) },
    data: { email, password },
  });
  if (!response.ok()) throw new Error(`Staff sign-in failed for ${email}: ${response.status()}`);
}

/** A new customer through the OTP flow (development inbox), so no seeded number's hourly code budget is used. */
export async function customerSignUp(context: BrowserContext, request: APIRequestContext): Promise<void> {
  const phone = `+9665${Math.floor(10_000_000 + Math.random() * 89_999_999)}`;
  let token = await csrf(context, request);
  const requested = await request.post('/api/v1/auth/otp/request', {
    headers: { 'X-CSRF-Token': token },
    data: { phone, termsAccepted: true, locale: 'ar' },
  });
  const { challengeId } = (await requested.json()) as { challengeId: string };
  const { code } = (await (
    await request.get(`/api/v1/dev/otp-inbox/latest?phone=${encodeURIComponent(phone)}`)
  ).json()) as { code: string };
  const verified = await request.post('/api/v1/auth/otp/verify', {
    headers: { 'X-CSRF-Token': token },
    data: { challengeId, code },
  });
  if (!verified.ok()) throw new Error(`OTP verification failed: ${verified.status()}`);
  token = await csrf(context, request);
  const completed = await request.post('/api/v1/auth/profile/complete', {
    headers: { 'X-CSRF-Token': token },
    data: { displayName: 'مراجعة الصفحات', preferredLocale: 'ar', termsAccepted: true },
  });
  if (!completed.ok()) throw new Error(`Profile completion failed: ${completed.status()}`);
}

/** YYYY-MM-DD in Riyadh, `days` from today. */
function riyadhDate(days: number): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Riyadh' }).format(new Date());
  const [y, m, d] = today.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** Books Faisal's haircut at Al Asala on a late free time 9–11 days ahead, for the customer's booking pages. */
export async function bookFaisal(context: BrowserContext, request: APIRequestContext): Promise<string> {
  const token = await csrf(context, request);
  for (const days of [9, 10, 11]) {
    const { slots } = (await (
      await request.get(
        `/api/v1/public/shops/${SEED.alAsala.slug}/availability/slots?serviceId=${SEED.haircut}&professionalId=${SEED.faisal}&date=${riyadhDate(days)}`,
      )
    ).json()) as { slots: Array<{ startsAt: string }> };
    for (const slot of slots.slice(-3)) {
      const created = await request.post('/api/v1/bookings', {
        headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() },
        data: {
          shopSlug: SEED.alAsala.slug,
          serviceId: SEED.haircut,
          professionalId: SEED.faisal,
          startsAt: slot.startsAt,
        },
      });
      if (created.status() === 201) return ((await created.json()) as { id: string }).id;
    }
  }
  throw new Error('Faisal has no free time 9–11 days ahead.');
}

export async function cancelBooking(context: BrowserContext, request: APIRequestContext, bookingId: string) {
  const booking = (await (await request.get(`/api/v1/me/bookings/${bookingId}`)).json()) as {
    version: number;
  };
  await request.post(`/api/v1/me/bookings/${bookingId}/cancel`, {
    headers: { 'X-CSRF-Token': await csrf(context, request) },
    data: { reason: null, version: booking.version },
  });
}

/** The first id of a list response (`[…]` or `{ items: […] }`). */
export async function firstId(request: APIRequestContext, path: string): Promise<string> {
  const json = (await (await request.get(path)).json()) as unknown;
  const list = Array.isArray(json) ? json : (json as { items?: unknown[] }).items;
  const first = list?.[0] as { id?: string } | undefined;
  if (!first?.id) throw new Error(`No id in ${path}`);
  return first.id;
}
