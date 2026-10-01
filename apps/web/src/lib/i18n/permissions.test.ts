import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import ar from '@messages/ar.json';
import en from '@messages/en.json';

/**
 * Phase 17 route audit: the roles pages showed raw keys for `Admin.Jobs.View`, a permission added without its label.
 * Every platform permission in the API's catalogue needs a label and an area label in both languages.
 */
const catalogue = readFileSync(
  join(__dirname, '../../../../../src/Modules/Identity/Trimme.Modules.Identity/Domain/Permissions.cs'),
  'utf8',
);
const platformCodes = [...catalogue.matchAll(/= "((?:Admin|SuperAdmin)\.[A-Za-z.]+)"/g)].map((m) => m[1]!);

/** The roles page groups codes by area: the second segment, or `SuperAdmin` for SuperAdmin-only codes. */
const area = (code: string) => (code.startsWith('SuperAdmin.') ? 'SuperAdmin' : code.split('.')[1]!);

describe('permission labels', () => {
  it('reads the catalogue', () => {
    expect(platformCodes.length).toBeGreaterThan(30);
    expect(platformCodes).toContain('Admin.Jobs.View');
  });

  it.each([
    ['ar', ar.permissions],
    ['en', en.permissions],
  ])('every platform permission has a label and an area label in %s', (_locale, messages) => {
    const codes = messages.codes as Record<string, string>;
    const areas = messages.areas as Record<string, string>;
    expect(platformCodes.filter((code) => !codes[code.replace(/\./g, '_')])).toEqual([]);
    expect([...new Set(platformCodes.map(area))].filter((name) => !areas[name])).toEqual([]);
  });
});
