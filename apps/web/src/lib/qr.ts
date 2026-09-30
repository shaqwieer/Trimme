/** QR helpers shared by the landing, admin and shop pages (Phase 16, D-114). Pure, so server and client use them. */

export const QR_FORMATS = ['png', 'svg', 'pdf'] as const;
export type QrFormat = (typeof QR_FORMATS)[number];

/** Who reads the file: an admin (any shop's code) or the shop itself (its own codes only). */
export type QrScope = 'admin' | 'shop';

/** The API file of one code; same-origin, so the session cookie authorizes the download or the `<img>`. */
export function qrImageUrl(scope: QrScope, codeId: string, format: QrFormat, size?: number): string {
  const query = new URLSearchParams({ format });
  if (size) query.set('size', String(size));
  return `/api/v1/${scope}/qr/codes/${codeId}/image?${query.toString()}`;
}

/** The printed URL without its scheme, as the cards show it (`trimme.sa/q/aswn7qkd`). */
export function displayUrl(url: string): string {
  return url.replace(/^https?:\/\//, '');
}

/** A period of the last `days` days up to `today` (YYYY-MM-DD), both ends included. */
export function lastDays(today: string, days: number): { from: string; to: string } {
  const [y, m, d] = today.split('-').map(Number) as [number, number, number];
  const from = new Date(Date.UTC(y, m - 1, d - (days - 1))).toISOString().slice(0, 10);
  return { from, to: today };
}

export const QR_PERIODS = [7, 30, 90] as const;
export type QrPeriod = (typeof QR_PERIODS)[number];

export function qrPeriod(value: string | undefined): QrPeriod {
  const days = Number(value);
  return (QR_PERIODS as readonly number[]).includes(days) ? (days as QrPeriod) : 30;
}

type NextSlot = { startsAt: string; localTime: string };
type ProfessionalNext = {
  professionalId: string;
  date: string | null;
  slots: NextSlot[];
  offer: { id: string; isPackage: boolean } | null;
};

export type EarliestSlot = NextSlot & { professionalId: string; offerId: string; isPackage: boolean };

/**
 * The shop's earliest free times (c-qr «أقرب الأوقات اليوم»): the first day any professional is free, then that day's
 * first `count` distinct times across professionals, each with the professional and offer to open the wizard at.
 */
export function earliestSlots(
  professionals: ProfessionalNext[],
  count = 3,
): { date: string | null; slots: EarliestSlot[] } {
  const dates = professionals.map((p) => p.date).filter((d): d is string => Boolean(d));
  if (dates.length === 0) return { date: null, slots: [] };
  const date = dates.sort()[0]!;
  const byTime = new Map<string, EarliestSlot>();
  for (const professional of professionals) {
    if (professional.date !== date || !professional.offer) continue;
    for (const slot of professional.slots) {
      if (!byTime.has(slot.startsAt)) {
        byTime.set(slot.startsAt, {
          ...slot,
          professionalId: professional.professionalId,
          offerId: professional.offer.id,
          isPackage: professional.offer.isPackage,
        });
      }
    }
  }
  const slots = [...byTime.values()]
    .sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime())
    .slice(0, count);
  return { date, slots };
}
