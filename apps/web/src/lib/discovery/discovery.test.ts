import { describe, expect, it } from 'vitest';
import { clientIp } from '@/lib/api/forwarding';
import { durationParts, minuteOfDay, relativeTime } from './format';
import { distanceKm, parseLocation, roundForPrivacy, serializeLocation } from './location';
import { isTomorrow, openingLabel } from './opening';
import { activeFilterCount, filtersToQuery, parseSearchFilters, toApiQuery } from './search';
import { SearchText } from './text';

const opening = (key: string, values?: Record<string, string>) => `${key}:${JSON.stringify(values ?? {})}`;

describe('search filters in the URL (mapRules #3)', () => {
  it('reads every filter and ignores malformed values', () => {
    const filters = parseSearchFilters(
      {
        q: '  تهذيب ',
        category: 'not-a-uuid',
        sort: 'earliest',
        open: '1',
        verified: '0',
        today: '1',
        min: '25',
        max: '-3',
        view: 'map',
        page: '2',
      },
      true,
    );
    expect(filters).toMatchObject({
      q: 'تهذيب',
      category: null,
      sort: 'earliest',
      openNow: true,
      verified: false,
      today: true,
      minPrice: 25,
      maxPrice: null,
      view: 'map',
      page: 2,
    });
  });

  it('defaults to nearest with a location and to rating without one', () => {
    expect(parseSearchFilters({}, true).sort).toBe('nearest');
    expect(parseSearchFilters({}, false).sort).toBe('rating');
  });

  it('writes only non-default filters back to the URL and round-trips', () => {
    const filters = parseSearchFilters(
      { q: 'قص', open: '1', category: '0199a0de-5a10-7000-8000-000000000301' },
      true,
    );
    const query = filtersToQuery(filters);
    expect(query).toEqual({ q: 'قص', category: '0199a0de-5a10-7000-8000-000000000301', open: '1' });
    expect(parseSearchFilters(query, true)).toEqual(filters);
    expect(activeFilterCount(filters)).toBe(2);
  });

  it('maps to the API query: location with a radius, API sort names, distance falls back to rating without a location', () => {
    const filters = parseSearchFilters({ sort: 'rating', max: '50' }, true);
    expect(toApiQuery(filters, { lat: 24.77, lng: 46.64, label: '', source: 'device' }, 20)).toMatchObject({
      lat: 24.77,
      lng: 46.64,
      radiusKm: 10,
      sort: 'Rating',
      maxPrice: 50,
      pageSize: 20,
    });
    const noLocation = toApiQuery(parseSearchFilters({ sort: 'nearest' }, false), null, 20);
    expect(noLocation.lat).toBeUndefined();
    expect(noLocation.radiusKm).toBeUndefined();
    expect(noLocation.sort).toBe('Rating');
  });
});

describe('the location cookie (D-095)', () => {
  it('rounds coordinates to about 100 m and round-trips', () => {
    expect(roundForPrivacy(24.774319)).toBe(24.774);
    const raw = serializeLocation({ lat: 24.774319, lng: 46.638012, label: 'الملقا', source: 'area' });
    expect(parseLocation(raw)).toEqual({ lat: 24.774, lng: 46.638, label: 'الملقا', source: 'area' });
  });

  it('ignores malformed or out-of-range values', () => {
    expect(parseLocation('not json')).toBeNull();
    expect(parseLocation(encodeURIComponent(JSON.stringify({ lat: 95, lng: 46 })))).toBeNull();
    expect(parseLocation(encodeURIComponent(JSON.stringify({ lat: '24', lng: 46 })))).toBeNull();
    expect(parseLocation(undefined)).toBeNull();
  });

  it('measures great-circle distance', () => {
    expect(distanceKm({ lat: 24.77, lng: 46.639 }, { lat: 24.8, lng: 46.7 })).toBeCloseTo(6.99, 1);
  });
});

describe('opening labels', () => {
  const now = new Date('2026-10-04T07:00:00Z'); // 10:00 in Riyadh

  it('says open until the closing time', () => {
    expect(
      openingLabel(opening, 'en', { isOpenNow: true, closesAt: '2026-10-04T18:00:00Z' }, 'Asia/Riyadh', now),
    ).toBe('openUntil:{"time":"9:00 pm"}');
  });

  it('says opens at a time today, and adds the weekday on another day', () => {
    expect(
      openingLabel(
        opening,
        'en',
        { isOpenNow: false, nextOpensAt: '2026-10-04T11:00:00Z' },
        'Asia/Riyadh',
        now,
      ),
    ).toBe('opensAt:{"time":"2:00 pm"}');
    expect(
      openingLabel(
        opening,
        'en',
        { isOpenNow: false, nextOpensAt: '2026-10-06T06:00:00Z' },
        'Asia/Riyadh',
        now,
      ),
    ).toBe('opensOn:{"day":"Tuesday","time":"9:00 am"}');
  });

  it('says closed when nothing opens soon', () => {
    expect(openingLabel(opening, 'ar', { isOpenNow: false }, 'Asia/Riyadh', now)).toBe('closed:{}');
  });

  it('knows tomorrow in the shop time zone', () => {
    expect(isTomorrow('2026-10-05T07:00:00Z', 'Asia/Riyadh', now)).toBe(true);
    expect(isTomorrow('2026-10-04T20:00:00Z', 'Asia/Riyadh', now)).toBe(false);
  });
});

describe('page formatting', () => {
  it('formats minutes of the day, wrapping past midnight', () => {
    expect(minuteOfDay(9 * 60, 'en')).toBe('9:00 am');
    expect(minuteOfDay(26 * 60, 'en')).toBe('2:00 am');
    expect(minuteOfDay(21 * 60, 'ar')).toBe('٩:٠٠ م');
  });

  it('writes relative review times', () => {
    const now = new Date('2026-10-04T12:00:00Z');
    expect(relativeTime('2026-10-01T12:00:00Z', 'en', now)).toBe('3 days ago');
    expect(relativeTime('2026-10-01T12:00:00Z', 'ar', now)).toContain('٣');
  });

  it('turns the cancellation cutoff into hours when it is whole hours', () => {
    expect(durationParts(120)).toEqual({ unit: 'hours', count: 2 });
    expect(durationParts(90)).toEqual({ unit: 'minutes', count: 90 });
  });

  it('normalizes Arabic spelling variants for the district filter', () => {
    expect(SearchText.normalize('الأصالة')).toBe(SearchText.normalize('الاصاله'));
  });
});

describe('forwarded client address (D-094)', () => {
  const headers = (values: Record<string, string>) => ({ get: (name: string) => values[name] ?? null });

  it('trusts only the last hop, which the nearest proxy appended', () => {
    expect(clientIp(headers({ 'x-forwarded-for': '6.6.6.6, 10.0.0.9' }))).toBe('10.0.0.9');
    expect(clientIp(headers({ 'x-real-ip': '10.0.0.5' }))).toBe('10.0.0.5');
  });

  it('ignores anything that is not an address', () => {
    expect(clientIp(headers({ 'x-forwarded-for': '10.0.0.1, evil\r\nx: y' }))).toBeUndefined();
    expect(clientIp(headers({}))).toBeUndefined();
  });
});
