import { toLatinDigits } from '@/components/ui/digits';

export const MAX_PRICE = 100_000;

/** Service durations: 5-minute steps up to 8 hours (D-071). */
export const DURATION_OPTIONS: readonly number[] = Array.from({ length: 96 }, (_, i) => (i + 1) * 5);

/**
 * Reads a typed price: Latin or Arabic-Indic digits, "." or the Arabic decimal separator "٫" (or ",") as the decimal
 * point. Returns null unless it is 0–100,000 with at most two decimals (never rounded).
 */
export function parsePrice(text: string): number | null {
  const normalized = toLatinDigits(text.trim()).replace(/[٫,]/g, '.').replace(/\s/g, '');
  if (!/^\d{1,6}(\.\d{1,2})?$/.test(normalized)) return null;
  const value = Number(normalized);
  return value <= MAX_PRICE ? value : null;
}
