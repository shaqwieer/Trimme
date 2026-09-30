import { describe, expect, it } from 'vitest';
import {
  activeOf,
  dispatchTone,
  draftOf,
  insertPlaceholder,
  isDirty,
  localeKey,
  placeholderToken,
  type TemplateVersion,
  usedPlaceholders,
} from './whatsapp';

const version = (overrides: Partial<TemplateVersion>): TemplateVersion => ({
  id: 'v1',
  number: 1,
  body: 'Hi {{customer_name}}',
  buttons: [],
  providerTemplateName: 'trimme_customer_booking_confirmed',
  status: 'Active',
  createdAt: '2026-09-17T09:00:00Z',
  createdByName: null,
  activatedAt: '2026-09-17T09:00:00Z',
  activatedByName: null,
  ...overrides,
});

describe('whatsapp template helpers', () => {
  it('inserts a placeholder at the caret, replacing a selection', () => {
    expect(placeholderToken('shop_name')).toBe('{{shop_name}}');
    expect(insertPlaceholder('Hello  there', 6, 6, 'customer_name')).toEqual({
      text: 'Hello {{customer_name}} there',
      caret: 23,
    });
    expect(insertPlaceholder('Hello NAME', 6, 10, 'customer_name')).toEqual({
      text: 'Hello {{customer_name}}',
      caret: 23,
    });
    expect(insertPlaceholder('abc', 99, 99, 'amount').text).toBe('abc{{amount}}');
  });

  it('lists the placeholders a body uses in order, once each', () => {
    expect(usedPlaceholders('{{shop_name}} at {{booking_time}}, {{shop_name}} {{ booking_date }}')).toEqual([
      'shop_name',
      'booking_time',
      'booking_date',
    ]);
  });

  it('finds the draft and the active version, and detects unsaved edits', () => {
    const detail = {
      activeVersionId: 'v1',
      versions: [version({ id: 'v2', number: 2, status: 'Draft', body: 'Draft {{shop_name}}' }), version({})],
    };
    expect(draftOf(detail)?.id).toBe('v2');
    expect(activeOf(detail)?.id).toBe('v1');
    const stored = draftOf(detail);
    expect(
      isDirty(
        {
          body: 'Draft {{shop_name}}',
          buttons: [],
          providerTemplateName: 'trimme_customer_booking_confirmed',
        },
        stored,
      ),
    ).toBe(false);
    expect(
      isDirty(
        {
          body: 'Draft {{shop_name}}!',
          buttons: [],
          providerTemplateName: 'trimme_customer_booking_confirmed',
        },
        stored,
      ),
    ).toBe(true);
    expect(
      isDirty(
        {
          body: 'Draft {{shop_name}}',
          buttons: [{ label: 'Go', target: 'ShopPage' }],
          providerTemplateName: 'trimme_customer_booking_confirmed',
        },
        stored,
      ),
    ).toBe(true);
  });

  it('maps dispatch statuses to badge tones and locales to message keys', () => {
    expect(dispatchTone('Delivered')).toBe('success');
    expect(dispatchTone('Read')).toBe('success');
    expect(dispatchTone('Failed')).toBe('danger');
    expect(dispatchTone('Queued')).toBe('warning');
    expect(localeKey('en')).toBe('en');
    expect(localeKey('ar')).toBe('ar');
    expect(localeKey('fr')).toBe('ar');
  });
});
