/**
 * Makes `Intl.DateTimeFormat` behave as Safari does for Arabic (D-156): a locale `ar-SA…` without a `ca-` key, or a
 * `calendar` option, formats in the Umm al-Qura (Hijri) calendar. Node's ICU defaults `ar-SA` to Gregorian, so without
 * this a test cannot see the iPhone's bug (the day strip read «سبت ١٨ نوفمبر» for Saturday 28 November).
 * Returns the function that puts the real formatter back.
 */
export function installSafariArabicCalendar(): () => void {
  const Real = Intl.DateTimeFormat;
  function SafariDateTimeFormat(locales?: string | string[], options?: Intl.DateTimeFormatOptions) {
    const first = Array.isArray(locales) ? locales[0] : locales;
    const hijri = first?.startsWith('ar-SA') && !first.includes('-ca-') && !options?.calendar;
    const locale = hijri ? `${first}${first!.includes('-u-') ? '' : '-u'}-ca-islamic-umalqura` : locales;
    return new Real(locale, options);
  }
  Intl.DateTimeFormat = SafariDateTimeFormat as unknown as typeof Intl.DateTimeFormat;
  return () => {
    Intl.DateTimeFormat = Real;
  };
}
