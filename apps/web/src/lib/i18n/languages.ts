/**
 * The languages a barber can list (D-149, the owner's item 2): a fixed list the API checks, so customers can find a
 * barber who speaks theirs. Names come from the `spokenLanguages` messages.
 */
export const BARBER_LANGUAGES = ['ar', 'en', 'ur', 'hi', 'bn', 'tl', 'tr'] as const;
export type BarberLanguage = (typeof BARBER_LANGUAGES)[number];

/** The codes in the list's order, without unknown ones. */
export function knownLanguages(codes: readonly string[] | null | undefined): BarberLanguage[] {
  return BARBER_LANGUAGES.filter((code) => codes?.includes(code));
}
