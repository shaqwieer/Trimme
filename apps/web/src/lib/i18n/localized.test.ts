import { describe, expect, it } from 'vitest';
import { asLocale } from '@/i18n/routing';
import { contentLanguage, langIfOther, localizedName, localizedText } from './localized';

describe('locales', () => {
  it('narrows route parameters to a supported locale, falling back to Arabic', () => {
    expect(asLocale('tr')).toBe('tr');
    expect(asLocale('en')).toBe('en');
    expect(asLocale('ar')).toBe('ar');
    expect(asLocale('fr')).toBe('ar');
  });

  it('shows record data in Arabic on Arabic pages and in English everywhere else', () => {
    expect(contentLanguage('ar')).toBe('ar');
    expect(contentLanguage('en')).toBe('en');
    expect(contentLanguage('tr')).toBe('en');
    expect(contentLanguage('fr')).toBe('ar');
  });
});

describe('localized record data', () => {
  it('names a record in English on Turkish pages, or in Arabic when it has no English name', () => {
    expect(localizedName('tr', 'صالون الأصالة', 'Al Asala Salon')).toBe('Al Asala Salon');
    expect(localizedName('tr', 'صالون الأصالة', '')).toBe('صالون الأصالة');
    expect(localizedName('tr', 'صالون الأصالة', null)).toBe('صالون الأصالة');
    expect(localizedName('ar', 'صالون الأصالة', 'Al Asala Salon')).toBe('صالون الأصالة');
  });

  it('marks the language of a description that is not in the page language', () => {
    const english = localizedText('tr', 'وصف', 'Description')!;
    expect(english).toEqual({ text: 'Description', lang: 'en' });
    expect(langIfOther(english, 'tr')).toBe('en');

    const arabic = localizedText('tr', 'وصف', null)!;
    expect(arabic).toEqual({ text: 'وصف', lang: 'ar' });
    expect(langIfOther(arabic, 'tr')).toBe('ar');

    expect(langIfOther(localizedText('en', 'وصف', 'Description')!, 'en')).toBeUndefined();
  });
});
