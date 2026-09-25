// Fails when src/lib/api/schema.d.ts is out of date with the committed API contract (R-FND-05).
// Regenerate with: pnpm openapi:generate
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const committed = 'src/lib/api/schema.d.ts';
const dir = mkdtempSync(join(tmpdir(), 'trimme-openapi-'));
const fresh = join(dir, 'schema.d.ts');

try {
  execFileSync(
    process.execPath,
    ['node_modules/openapi-typescript/bin/cli.js', '../api/openapi/v1.json', '-o', fresh],
    {
      stdio: 'pipe',
    },
  );
  const normalize = (text) => text.replace(/\r\n/g, '\n').trim();
  if (normalize(readFileSync(committed, 'utf8')) !== normalize(readFileSync(fresh, 'utf8'))) {
    console.error(`${committed} is out of date with apps/api/openapi/v1.json. Run: pnpm openapi:generate`);
    process.exit(1);
  }
  console.log('OpenAPI client types are up to date.');
} finally {
  rmSync(dir, { recursive: true, force: true });
}
