import type { Formats } from 'next-intl';

/**
 * Named number formats for messages (D-148, «٤-٤»). A bare `{count}` or a plural's `#` prints Latin digits under the
 * `ar` locale, so ar.json writes numeric arguments as `{count, number, arab}` (`arabPlain` for years and pixel sizes).
 * The other locales keep plain arguments, so they never use these.
 */
export const MESSAGE_FORMATS = {
  number: {
    arab: { numberingSystem: 'arab' },
    arabPlain: { numberingSystem: 'arab', useGrouping: false },
  },
} satisfies Formats;
