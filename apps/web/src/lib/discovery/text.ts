/**
 * The browser's copy of the API's search-text normalization (D-091), used only to filter short client-side lists (the
 * district picker). Discovery search itself is normalized by the API.
 */
export const SearchText = {
  normalize(value: string | null | undefined): string {
    if (!value) return '';
    return value
      .normalize('NFKC')
      .toLowerCase()
      .replace(/[ً-ٰٟـ]/g, '')
      .replace(/[أإآٱ]/g, 'ا')
      .replace(/ى/g, 'ي')
      .replace(/ة/g, 'ه')
      .replace(/ؤ/g, 'و')
      .replace(/ئ/g, 'ي')
      .replace(/[\p{P}\p{S}\s]+/gu, ' ')
      .trim();
  },
};
