import { describe, expect, it } from 'vitest';
import ar from '../../../messages/ar.json';
import en from '../../../messages/en.json';
import { type Notice, noticeHref, noticeKey, noticeValues } from './notices';

const booking: Notice = {
  id: 'n1',
  kind: 'booking.created',
  parameters: {
    customerName: 'نورة',
    itemNameAr: 'قص شعر',
    itemNameEn: 'Haircut',
    professionalNameAr: 'فيصل',
    professionalNameEn: 'Faisal',
    startsAt: '2026-09-18T14:30:00Z',
    reference: 'TRM4K7QZ',
  },
  bookingId: 'b1',
  createdAt: '2026-09-17T09:00:00Z',
  readAt: null,
};

function lookup(messages: unknown, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>((node, key) => (node as Record<string, unknown> | undefined)?.[key], messages);
}

describe('notices', () => {
  it('maps each inbox to its own message and unknown kinds to a generic line', () => {
    expect(noticeKey('shop', 'booking.created')).toBe('shop.booking_created');
    expect(noticeKey('customer', 'booking.cancelled')).toBe('customer.booking_cancelled');
    expect(noticeKey('admin', 'whatsapp.dispatch_failed')).toBe('admin.whatsapp_dispatch_failed');
    expect(noticeKey('customer', 'booking.created')).toBe('generic');
    expect(noticeKey('shop', 'something.new')).toBe('generic');
  });

  it('has an Arabic and an English message for every known kind', () => {
    const kinds: Array<[Parameters<typeof noticeKey>[0], string]> = [
      ['shop', 'booking.created'],
      ['shop', 'booking.pending'],
      ['shop', 'booking.confirmed'],
      ['shop', 'booking.rescheduled'],
      ['shop', 'booking.cancelled'],
      ['shop', 'subscription.expiring'],
      ['shop', 'subscription.expired'],
      ['shop', 'admin.message'],
      ['customer', 'booking.confirmed'],
      ['customer', 'booking.rescheduled'],
      ['customer', 'booking.cancelled'],
      ['admin', 'whatsapp.dispatch_failed'],
      ['admin', 'subscription.expiring'],
      ['admin', 'subscription.expired'],
      ['admin', 'outbox.dead_lettered'],
    ];
    for (const [audience, kind] of kinds) {
      const path = `notifications.kinds.${noticeKey(audience, kind)}`;
      expect(typeof lookup(ar, path), path).toBe('string');
      expect(typeof lookup(en, path), path).toBe('string');
    }
  });

  it('formats names in the reader language and times with the clock digit rule', () => {
    const arabic = noticeValues(booking, 'ar');
    expect(arabic.item).toBe('قص شعر');
    expect(arabic.time).toBe('٥:٣٠ م');
    expect(arabic.date).toBe('الجمعة، ١٨ سبتمبر');
    const english = noticeValues(booking, 'en');
    expect(english.item).toBe('Haircut');
    expect(english.professional).toBe('Faisal');
    expect(english.time).toBe('5:30 pm');
    expect(
      noticeValues({ ...booking, parameters: { daysLeft: '7', endDate: '2026-10-31' } }, 'en'),
    ).toMatchObject({
      days: 7,
      endDate: '31 October 2026',
    });
  });

  it('links each inbox to its own area', () => {
    expect(noticeHref('shop', booking)).toBe('/shop/appointments?booking=b1');
    expect(noticeHref('customer', booking)).toBe('/account/bookings/b1');
    expect(noticeHref('shop', { ...booking, kind: 'subscription.expiring', bookingId: null })).toBe(
      '/shop/subscription',
    );
    expect(noticeHref('admin', { ...booking, kind: 'whatsapp.dispatch_failed' })).toBe(
      '/admin/whatsapp/dispatches?status=Failed',
    );
    expect(
      noticeHref('admin', {
        ...booking,
        kind: 'subscription.expired',
        bookingId: null,
        parameters: { shopId: 's1' },
      }),
    ).toBe('/admin/shops/s1?tab=subscription');
  });
});
