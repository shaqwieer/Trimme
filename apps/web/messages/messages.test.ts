import { describe, expect, it } from 'vitest';
import ar from './ar.json';
import en from './en.json';

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

const placeholders = (message: string) =>
  [...message.matchAll(/\{\s*([A-Za-z0-9_]+)/g)].map((m) => m[1]).sort();

describe('messages_have_key_parity (R-WEB-05)', () => {
  const arEntries = flatten(ar as Catalog);
  const enEntries = flatten(en as Catalog);

  it('has exactly the same keys in Arabic and English', () => {
    const missingInEn = [...arEntries.keys()].filter((k) => !enEntries.has(k));
    const missingInAr = [...enEntries.keys()].filter((k) => !arEntries.has(k));
    expect({ missingInEn, missingInAr }).toEqual({ missingInEn: [], missingInAr: [] });
  });

  it('has no empty messages', () => {
    const empty = [...arEntries, ...enEntries].filter(([, v]) => v.trim() === '').map(([k]) => k);
    expect(empty).toEqual([]);
  });

  it('uses the same ICU placeholders in both locales', () => {
    const mismatched = [...arEntries.keys()].filter(
      (key) =>
        JSON.stringify(placeholders(arEntries.get(key)!)) !==
        JSON.stringify(placeholders(enEntries.get(key) ?? '')),
    );
    expect(mismatched).toEqual([]);
  });
});
