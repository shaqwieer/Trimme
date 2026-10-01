import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';

/**
 * Phase 17 (ZAP baseline): a form submitted before hydration falls back to the browser's default, a GET that puts every
 * field (passwords, phone numbers, codes) in the URL, the history and the server logs. Every form therefore states its
 * method: JavaScript-handled forms use POST, so the fallback carries nothing in the URL, and only search and filter forms
 * use GET.
 */
const root = join(__dirname, '..', '..');

function sources(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return sources(path);
    return entry.name.endsWith('.tsx') && !entry.name.endsWith('.test.tsx') ? [path] : [];
  });
}

/** The opening `<form …>` tags of a file, with JSX expressions balanced. */
function formTags(source: string): string[] {
  const tags: string[] = [];
  for (const match of source.matchAll(/<form\b/g)) {
    let depth = 0;
    let end = match.index;
    for (; end < source.length; end++) {
      const char = source[end];
      if (char === '{') depth++;
      else if (char === '}') depth--;
      else if (char === '>' && depth === 0) break;
    }
    tags.push(source.slice(match.index, end + 1));
  }
  return tags;
}

describe('forms', () => {
  const forms = sources(root).flatMap((file) =>
    formTags(readFileSync(file, 'utf8')).map((tag) => ({ file: relative(root, file), tag })),
  );

  it('finds the forms', () => {
    expect(forms.length).toBeGreaterThan(25);
  });

  it('post when handled in JavaScript, so a submit before hydration never puts fields in the URL', () => {
    const offenders = forms.filter(({ tag }) => tag.includes('onSubmit') && !tag.includes('method="post"'));
    expect(offenders.map((f) => f.file)).toEqual([]);
  });

  it('state their method explicitly, and only filters use GET', () => {
    const implicit = forms.filter(({ tag }) => !/method="(get|post)"/.test(tag));
    expect(implicit.map((f) => f.file)).toEqual([]);
    const gets = forms.filter(({ tag }) => tag.includes('method="get"'));
    expect(gets.every(({ tag }) => !tag.includes('onSubmit'))).toBe(true);
  });
});
