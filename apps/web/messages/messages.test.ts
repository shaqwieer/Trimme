import { describe, expect, it } from 'vitest';
import ar from './ar.json';
import en from './en.json';
import tr from './tr.json';

type Catalog = { [key: string]: string | Catalog };

function flatten(catalog: Catalog, prefix = ''): Map<string, string> {
  const entries = new Map<string, string>();
  for (const [key, value] of Object.entries(catalog)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'string') {
      entries.set(path, value);
    } else {
      for (const [childKey, childValue] of flatten(value, path)) {
        entries.set(childKey, childValue);
      }
    }
  }
  return entries;
}

/**
 * ICU argument names: `{name}` or `{name, plural|select…`. The text inside plural branches (`one {# shop}`,
 * `=0 {No shops}`) is not an argument, so a name must be followed by `}` or `,`.
 */
const placeholders = (message: string) =>
  [...new Set([...message.matchAll(/\{\s*([A-Za-z0-9_]+)\s*[,}]/g)].map((m) => m[1]))].sort();

describe('messages_have_key_parity (R-WEB-05)', () => {
  const arEntries = flatten(ar as Catalog);
  /** Every other catalogue is checked against the Arabic one, the primary locale. */
  const others = { en: flatten(en as Catalog), tr: flatten(tr as Catalog) };

  it.each(Object.entries(others))('has exactly the same keys in Arabic and %s', (_locale, entries) => {
    const missing = [...arEntries.keys()].filter((k) => !entries.has(k));
    const extra = [...entries.keys()].filter((k) => !arEntries.has(k));
    expect({ missing, extra }).toEqual({ missing: [], extra: [] });
  });

  it('has no empty messages', () => {
    const empty = [arEntries, ...Object.values(others)]
      .flatMap((entries) => [...entries])
      .filter(([, v]) => v.trim() === '')
      .map(([k]) => k);
    expect(empty).toEqual([]);
  });

  it.each(Object.entries(others))('uses the same ICU placeholders in Arabic and %s', (_locale, entries) => {
    const mismatched = [...arEntries.keys()].filter(
      (key) =>
        JSON.stringify(placeholders(arEntries.get(key)!)) !==
        JSON.stringify(placeholders(entries.get(key) ?? '')),
    );
    expect(mismatched).toEqual([]);
  });
});
