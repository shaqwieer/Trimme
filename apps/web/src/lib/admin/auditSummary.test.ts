import { createTranslator } from 'next-intl';
import { describe, expect, it } from 'vitest';
import { MESSAGE_FORMATS } from '@/i18n/formats';
import ar from '../../../messages/ar.json';
import en from '../../../messages/en.json';
import tr from '../../../messages/tr.json';
import { summaryKey, summaryValues } from './auditSummary';

type Messages = typeof ar;

function render(messages: Messages, locale: 'ar' | 'en' | 'tr') {
  const t = createTranslator({ locale, messages, formats: MESSAGE_FORMATS, namespace: 'adminAudit' });
  const settings = createTranslator({ locale, messages, namespace: 'platformSettings.fields' });
  const word = (value: string) =>
    t.has(`values.${value}` as 'values.nothing')
      ? t(`values.${value}` as 'values.nothing')
      : settings.has(value as 'currency')
        ? settings(value as 'currency')
        : value;
  return (code: string, args: Record<string, string>) =>
    t(
      `summaries.${summaryKey(code)}` as 'summaries.shop_created',
      summaryValues(args, locale, word) as Record<string, string>,
    );
}

describe('audit summaries (D-155, the client’s 8-3-6: the activity log in the reader’s language)', () => {
  const say = render(ar, 'ar');

  it('words statuses, documents, lengths and dates in Arabic', () => {
    expect(say('legal_document.saved', { kind: 'Terms', locale: 'ar' })).toBe('حُفظت وثيقة الشروط (العربية)');
    expect(
      say('booking.status_changed_by_admin', { reference: 'TRM4K7QZ', from: 'Confirmed', to: 'NoShow' }),
    ).toBe('الحجز TRM4K7QZ: من مؤكد إلى ما حضر');
    // The booking settings' sentence moved from the reason to the summary.
    const settings = say('shop.booking_settings_changed', {
      by: 'shop',
      slotStep: '5',
      minLeadTime: '360',
      horizonDays: '30',
      buffer: '10',
    });
    expect(settings).toContain('من المحل');
    expect(settings).toContain('كل ٥ دقائق');
    expect(settings).toContain('أقل مهلة ٦ ساعات');
    expect(settings).toContain('٣٠ يوماً');
    expect(
      say('plan.price_added', { version: '2', amount: '450', currency: 'SAR', from: '2026-12-01' }),
    ).toMatch(/^الإصدار ٢: .*٤٥٠.* من .*ديسمبر/);
    expect(say('schedule.unavailable_today', { by: 'barber', until: '2026-11-19T21:00:00Z' })).toMatch(
      /^الحلاق بنفسه — حتى /,
    );
  });

  it('words lists the reader’s way, a setting by its form label, and an empty list as nothing', () => {
    expect(say('shop.profile_updated', { by: 'admin', fields: 'Description,Logo' })).toBe(
      'من المنصة: الوصف والشعار',
    );
    expect(say('platform_settings.updated', { fields: 'reviewWindowDays' })).not.toContain(
      'reviewWindowDays',
    );
    expect(say('role.permissions_changed', { role: 'Support', added: 'Admin.Audit.View', removed: '' })).toBe(
      'الدعم: أُضيف Admin.Audit.View، وأُزيل لا شيء',
    );
    expect(
      say('whatsapp_shop_messages.saved', {
        changes: 'Customer.BookingConfirmed off,Professional.BookingCancelled on',
      }),
    ).toBe('العميل: تأكيد الحجز — موقوفة والحلاق: إلغاء الحجز — مفعّلة');
  });

  it('leaves out what the record does not carry', () => {
    expect(say('shop.online_booking_paused', { by: 'shop' })).toBe('من المحل — بلا وقت رجوع');
    expect(say('subscription.overridden', { amountFrom: '300', amountTo: '250', currency: 'SAR' })).toMatch(
      /^المبلغ: من .* إلى .*\.\s*$/,
    );
  });

  it('has every summary in Arabic, English and Turkish, each formatting with every argument', () => {
    const sample: Record<string, string> = {
      by: 'admin',
      kind: 'shop',
      locale: 'ar',
      fields: 'Name',
      reference: 'TRM4K7QZ',
      from: 'Confirmed',
      to: 'NoShow',
      name: 'x',
      role: 'Support',
      added: 'a',
      removed: 'b',
      firstVisit: '1',
      visit: '1',
      pointsForReward: '6',
      rewardType: 'AmountOff',
      rewardAmount: '20',
      inactivityRule: 'Wipe',
      inactivityDays: '30',
      event: 'BookingConfirmed',
      audience: 'Customer',
      attempt: '2',
      changes: 'Customer.BookingConfirmed on',
      version: '3',
      whatsApp: 'true',
      notifications: 'true',
      status: 'Active',
      number: 'changed',
      change: 'removed',
      count: '3',
      code: 'ABC123',
      automatic: 'true',
      rating: '5',
      length: '40',
      icon: 'scissors',
      priceFrom: '10',
      priceTo: '20',
      currency: 'SAR',
      durationFrom: '30',
      durationTo: '45',
      online: 'true',
      barbers: '2',
      price: '50',
      duration: '30',
      services: '2',
      source: 'Manual',
      demo: 'true',
      image: 'Logo',
      enabled: 'true',
      numberChanged: 'true',
      slotStep: '5',
      minLeadTime: '60',
      horizonDays: '30',
      buffer: '0',
      resumesAt: '2026-11-20T08:00:00Z',
      until: '2026-11-19T21:00:00Z',
      amount: '300',
      planAr: 'الذهبية',
      planEn: 'Gold',
      start: '2026-11-01',
      end: '2026-11-30',
      custom: 'true',
      standardAmount: '350',
      amountFrom: '300',
      amountTo: '250',
      endFrom: '2026-11-30',
      endTo: '2026-12-31',
    };
    const keys = Object.keys(ar.adminAudit.summaries);
    for (const [messages, locale] of [
      [ar, 'ar'],
      [en, 'en'],
      [tr, 'tr'],
    ] as const) {
      expect(Object.keys((messages as Messages).adminAudit.summaries).sort()).toEqual([...keys].sort());
      const words = render(messages as Messages, locale);
      for (const key of keys) {
        const text = words(key, sample);
        expect(text, `${locale} ${key}`).not.toMatch(/[{}]|adminAudit\./);
      }
    }
  });
});
