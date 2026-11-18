import * as rootParams from 'next/root-params';
import { notFound } from 'next/navigation';
import { hasLocale } from 'next-intl';
import { getRequestConfig } from 'next-intl/server';
import { MESSAGE_FORMATS } from './formats';
import { routing } from './routing';
import { OPERATING_TIME_ZONE } from '@/lib/i18n/config';

export default getRequestConfig(async ({ locale }) => {
  let resolved = locale;
  if (!resolved) {
    const paramValue = await rootParams.locale();
    if (!hasLocale(routing.locales, paramValue)) {
      notFound();
    }
    resolved = paramValue;
  }

  return {
    locale: resolved,
    timeZone: OPERATING_TIME_ZONE,
    formats: MESSAGE_FORMATS,
    messages: (await import(`../../messages/${resolved}.json`)).default,
  };
});
