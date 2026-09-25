import { notFound } from 'next/navigation';

/** Renders the localized not-found page for any unknown path under a locale. */
export default function CatchAll() {
  notFound();
}
