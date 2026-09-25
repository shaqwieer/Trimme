/**
 * Input normalisation for numeric fields (phone, OTP): Arabic-Indic (٠-٩) and Eastern Arabic-Indic (۰-۹)
 * digits typed on Arabic keyboards are converted to Latin digits, and everything else is dropped.
 */
/** Arabic-Indic (U+0660–0669) and Eastern Arabic-Indic (U+06F0–06F9) digit ranges. */
const ARABIC_INDIC = new RegExp(`[${String.fromCharCode(0x0660)}-${String.fromCharCode(0x0669)}]`, 'g');
const EASTERN_ARABIC_INDIC = new RegExp(
  `[${String.fromCharCode(0x06f0)}-${String.fromCharCode(0x06f9)}]`,
  'g',
);

export function toLatinDigits(value: string): string {
  return value
    .replace(ARABIC_INDIC, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(EASTERN_ARABIC_INDIC, (d) => String(d.charCodeAt(0) - 0x06f0));
}

export function digitsOnly(value: string, maxLength?: number): string {
  const digits = toLatinDigits(value).replace(/\D/g, '');
  return maxLength ? digits.slice(0, maxLength) : digits;
}

/** International dialling code for the operating country (spec §6: Saudi Arabia). */
export const SAUDI_DIAL_CODE = '+966';

/** Saudi mobile: 9 national digits starting with 5 (e.g. 512345678). Accepts a leading 0 or +966/00966. */
export function normalizeSaudiMobile(value: string): string {
  let digits = digitsOnly(value);
  if (digits.startsWith('00966')) digits = digits.slice(5);
  else if (digits.startsWith('966')) digits = digits.slice(3);
  if (digits.startsWith('0')) digits = digits.slice(1);
  return digits.slice(0, 9);
}

export function isValidSaudiMobile(national: string): boolean {
  return /^5\d{8}$/.test(national);
}

/** E.164 form sent to the API, or null when incomplete/invalid. */
export function toE164(national: string): string | null {
  const normalized = normalizeSaudiMobile(national);
  return isValidSaudiMobile(normalized) ? `+966${normalized}` : null;
}

/** "512345678" → "51 234 5678" for display while typing. */
export function groupSaudiMobile(national: string): string {
  const d = normalizeSaudiMobile(national);
  return [d.slice(0, 2), d.slice(2, 5), d.slice(5, 9)].filter(Boolean).join(' ');
}
