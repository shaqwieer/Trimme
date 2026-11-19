import { createTranslator } from 'next-intl';
import { describe, expect, it } from 'vitest';
import { MESSAGE_FORMATS } from '@/i18n/formats';
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

  it('words what a barber did for the owner, and a barber inbox in its own words (D-145)', () => {
    expect(noticeKey('shop', 'booking.cancelled', 'ShopUser')).toBe('shop.booking_cancelled_by_barber');
    expect(noticeKey('shop', 'booking.cancelled', 'Customer')).toBe('shop.booking_cancelled');
    expect(noticeKey('shop', 'booking.created', 'ShopUser')).toBe('shop.booking_created');
    expect(noticeKey('barber', 'booking.cancelled', 'ShopUser')).toBe('barber.booking_cancelled');
    expect(noticeKey('barber', 'subscription.expiring')).toBe('generic');
    expect(noticeKey('customer', 'booking.professional_changed')).toBe(
      'customer.booking_professional_changed',
    );

    const moved = {
      ...booking,
      kind: 'booking.professional_changed',
      parameters: {
        ...booking.parameters,
        previousProfessionalNameAr: 'عمر',
        previousProfessionalNameEn: 'Omar',
      },
    };
    expect(noticeValues(moved, 'en').previousProfessional).toBe('Omar');
    expect(noticeValues(moved, 'ar').previousProfessional).toBe('عمر');
    expect(noticeValues(booking, 'en').previousProfessional).toBe('');
    expect(noticeHref('barber', moved)).toBe('/shop/appointments?booking=b1');
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
      ['shop', 'booking.professional_changed'],
      ['shop', 'whatsapp.allowance_reached'],
      ['customer', 'loyalty.visit_completed'],
      ['customer', 'booking.completed'],
      ['customer', 'booking.confirmed'],
      ['customer', 'booking.rescheduled'],
      ['customer', 'booking.cancelled'],
      ['customer', 'booking.professional_changed'],
      ['barber', 'booking.created'],
      ['barber', 'booking.pending'],
      ['barber', 'booking.confirmed'],
      ['barber', 'booking.rescheduled'],
      ['barber', 'booking.cancelled'],
      ['barber', 'booking.professional_changed'],
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
    for (const kind of ['booking.confirmed', 'booking.rescheduled', 'booking.cancelled']) {
      const path = `notifications.kinds.${noticeKey('shop', kind, 'ShopUser')}`;
      expect(typeof lookup(ar, path), path).toBe('string');
      expect(typeof lookup(en, path), path).toBe('string');
    }
  });

  it('words «خلصت زيارتك» with the points the visit added and what is left, and leads to the cards (D-149)', () => {
    const t = createTranslator({
      locale: 'ar',
      messages: ar,
      formats: MESSAGE_FORMATS,
      namespace: 'notifications.kinds',
    });
    const visit = (pointsEarned: string, pointsToNext: string, rewardsReady: string): Notice => ({
      id: 'n9',
      kind: 'loyalty.visit_completed',
      parameters: {
        shopNameAr: 'صالون الأصالة',
        shopNameEn: 'Al Asala',
        pointsEarned,
        pointsToNext,
        rewardsReady,
      },
      bookingId: 'b1',
      createdAt: '2026-11-19T09:00:00Z',
      readAt: null,
    });
    const say = (notice: Notice) =>
      t(noticeKey('customer', notice.kind) as 'generic', noticeValues(notice, 'ar'));
    expect(say(visit('2', '2', '0'))).toBe(
      'خلصت زيارتك في صالون الأصالة، وأضفنا نقطتين إلى بطاقتك. باقي نقطتان للمكافأة.',
    );
    expect(say(visit('3', '5', '1'))).toBe(
      'خلصت زيارتك في صالون الأصالة، وأضفنا ٣ نقاط إلى بطاقتك. مكافأتك جاهزة، استخدمها في حجزك القادم.',
    );
    // A visit that used a reward adds no points.
    expect(say(visit('0', '6', '0'))).toBe('خلصت زيارتك في صالون الأصالة. باقي ٦ نقاط للمكافأة.');
    expect(noticeHref('customer', visit('2', '2', '0'))).toBe('/account/loyalty');
  });

  it('thanks the customer once per visit, asks for a rating and adds the points when the shop has loyalty (D-152, 3-4)', () => {
    const t = createTranslator({
      locale: 'ar',
      messages: ar,
      formats: MESSAGE_FORMATS,
      namespace: 'notifications.kinds',
    });
    const done = (loyalty?: { earned: string; toNext: string; ready: string }): Notice => ({
      id: 'n11',
      kind: 'booking.completed',
      parameters: {
        shopNameAr: 'صالون الأصالة',
        shopNameEn: 'Al Asala',
        shopSlug: 'al-asala',
        ...(loyalty && {
          pointsEarned: loyalty.earned,
          pointsToNext: loyalty.toNext,
          rewardsReady: loyalty.ready,
          pointsForReward: '6',
        }),
      },
      bookingId: 'b7',
      createdAt: '2026-11-19T09:00:00Z',
      readAt: null,
    });
    const say = (notice: Notice) =>
      t(noticeKey('customer', notice.kind) as 'generic', noticeValues(notice, 'ar'));
    expect(say(done())).toBe('شكراً لزيارتك في صالون الأصالة. قيّم زيارتك ليستفيد غيرك من تجربتك.');
    expect(say(done({ earned: '1', toNext: '1', ready: '0' }))).toBe(
      'شكراً لزيارتك في صالون الأصالة، وأضفنا نقطة واحدة إلى بطاقتك. باقي نقطة واحدة للمكافأة. قيّم زيارتك ليستفيد غيرك من تجربتك.',
    );
    expect(say(done({ earned: '0', toNext: '2', ready: '0' }))).toBe(
      'شكراً لزيارتك في صالون الأصالة. باقي نقطتان للمكافأة. قيّم زيارتك ليستفيد غيرك من تجربتك.',
    );
    expect(noticeHref('customer', done())).toBe('/account/bookings/b7/review');
  });

  it('tells the owner when the month’s WhatsApp messages are used up, or that the plan has none (D-149)', () => {
    const t = createTranslator({
      locale: 'ar',
      messages: ar,
      formats: MESSAGE_FORMATS,
      namespace: 'notifications.kinds',
    });
    const reached = (allowance: string): Notice => ({
      id: 'n10',
      kind: 'whatsapp.allowance_reached',
      parameters: { allowance, month: '2026-11' },
      bookingId: null,
      createdAt: '2026-11-19T09:00:00Z',
      readAt: null,
    });
    const say = (notice: Notice) =>
      t(noticeKey('shop', notice.kind) as 'generic', noticeValues(notice, 'ar'));
    expect(say(reached('500'))).toBe(
      'وصلتم إلى حد رسائل واتساب لهذا الشهر (٥٠٠). تستمر الإشعارات داخل التطبيق، وتعود رسائل واتساب أول الشهر القادم.',
    );
    expect(say(reached('0'))).toBe('باقتكم لا تشمل رسائل واتساب للعملاء. تصل إشعاراتهم داخل التطبيق.');
    expect(noticeHref('shop', reached('500'))).toBe('/shop/subscription');
  });

  it('copies every customer WhatsApp to the bell: the shop’s change, its cancellation with the reason, win-back (D-155)', () => {
    const t = createTranslator({
      locale: 'ar',
      messages: ar,
      formats: MESSAGE_FORMATS,
      namespace: 'notifications.kinds',
    });
    const notice = (kind: string, extra: Record<string, string>): Notice => ({
      id: 'n20',
      kind,
      parameters: {
        ...booking.parameters,
        shopNameAr: 'صالون الأصالة',
        shopNameEn: 'Al Asala',
        shopSlug: 'al-asala',
        ...extra,
      },
      bookingId: 'b9',
      createdAt: '2026-09-17T09:00:00Z',
      readAt: null,
    });
    const say = (n: Notice) => t(noticeKey('customer', n.kind) as 'generic', noticeValues(n, 'ar'));
    const moved = notice('customer.booking_rescheduled', {
      previousStartsAt: '2026-09-18T13:00:00Z',
      actor: 'ShopUser',
    });
    expect(say(moved)).toMatch(/^تغيير موعدك في صالون الأصالة: من .+ إلى .+. تقدر تلغي أو تغيّر مجاناً$/);
    expect(
      say(
        notice('customer.booking_rescheduled', {
          previousStartsAt: '2026-09-18T13:00:00Z',
          actor: 'Customer',
        }),
      ),
    ).toMatch(/^غيّرت موعدك في صالون الأصالة/);
    expect(say(notice('customer.booking_cancelled_by_shop', { reasonCode: 'StaffUnwell' }))).toMatch(
      /^نعتذر منك، ألغى صالون الأصالة حجزك .+. السبب: الحلاق تعبان. احجز من جديد/,
    );
    expect(say(notice('customer.booking_cancelled_by_shop', { reason: 'انقطاع الماء' }))).toContain(
      'السبب: انقطاع الماء',
    );
    // The WhatsApp's words (`MessageFormat.CancellationReason`): nothing typed is «ظرف عند المحل».
    expect(say(notice('customer.booking_cancelled_by_shop', {}))).toContain('السبب: ظرف عند المحل.');
    const winBack = notice('customer.win_back', {});
    expect(say(winBack)).toBe('اشتقنا لك في صالون الأصالة! احجز موعدك القادم بسهولة');
    expect(noticeHref('customer', winBack)).toBe('/shops/al-asala');
    expect(noticeHref('customer', moved)).toBe('/account/bookings/b9');

    const limit: Notice = {
      id: 'n21',
      kind: 'marketing.limit_reached',
      parameters: { allowance: '500', windowEnd: '2026-12-04' },
      bookingId: null,
      createdAt: '2026-11-19T09:00:00Z',
      readAt: null,
    };
    expect(t(noticeKey('shop', limit.kind) as 'generic', noticeValues(limit, 'ar'))).toBe(
      'وصل الحد: استُخدمت رسائل الدعوة لهالشهر (٥٠٠). ما تُرسل رسائل دعوة جديدة حتى ٤ ديسمبر ٢٠٢٦',
    );
    expect(noticeHref('shop', limit)).toBe('/shop/subscription');
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
