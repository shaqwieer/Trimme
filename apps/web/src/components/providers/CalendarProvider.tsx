'use client';

import { createContext, type ReactNode, useContext } from 'react';
import type { DisplayCalendar } from '@/lib/i18n/calendar';

const CalendarContext = createContext<DisplayCalendar>('gregory');

/** The shop user's display calendar (D-151), read from its cookie on the server so the first render already uses it. */
export function CalendarProvider({ calendar, children }: { calendar: DisplayCalendar; children: ReactNode }) {
  return <CalendarContext.Provider value={calendar}>{children}</CalendarContext.Provider>;
}

/** Gregorian outside the shop's pages. */
export function useDisplayCalendar(): DisplayCalendar {
  return useContext(CalendarContext);
}
