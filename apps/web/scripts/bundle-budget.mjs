#!/usr/bin/env node
/**
 * First-load JavaScript per route, gzipped, against budgets (Phase 17). Next 16 no longer prints route sizes, so this
 * reads the production build's manifests: each page's root chunks (build-manifest.json) and the chunks of its layouts
 * and page (page_client-reference-manifest.js). Run after `next build`; exits 1 when a route is over its budget.
 *
 *   node scripts/bundle-budget.mjs           check
 *   node scripts/bundle-budget.mjs --report  print every route, largest first
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import vm from 'node:vm';
import { gzipSync } from 'node:zlib';

const root = join(import.meta.dirname, '..');
const next = join(root, '.next');
const appDir = join(next, 'server', 'app');

/**
 * Budgets in KB (gzip), the first match applies. Set in Phase 17 a little above the measured sizes, so a regression
 * fails the build check: public pages are what visitors and search engines load first; dashboards carry the forms.
 */
const BUDGETS = [
  {
    name: 'map pages',
    match: /\/(search|onboarding\/location|shop\/settings\/location|admin\/shops\/(new|\[shopId\]))$/,
    kb: 330,
  },
  { name: 'sign-in, booking and discover', match: /\/(auth\/.*|shops\/\[slug\]\/book|discover)$/, kb: 260 },
  {
    name: 'public pages',
    match: /^\/\[locale\](\/(shops(\/.*)?|privacy|terms|q\/.*|\[\.\.\.rest\]))?$/,
    kb: 220,
  },
  { name: 'dashboards and account', match: /.*/, kb: 330 },
];

if (!existsSync(appDir)) {
  console.error('No production build found: run `pnpm build` first.');
  process.exit(2);
}

const gzipped = new Map();
function size(file) {
  if (!gzipped.has(file)) {
    const path = join(next, file.replace(/^\/_next\//, ''));
    gzipped.set(file, existsSync(path) ? gzipSync(readFileSync(path)).length : 0);
  }
  return gzipped.get(file);
}

function manifests(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return manifests(path);
    return entry.name === 'page_client-reference-manifest.js' ? [path] : [];
  });
}

const routes = [];
for (const file of manifests(appDir)) {
  const route = '/' + relative(appDir, file).split(sep).slice(0, -1).join('/');
  if (route.includes('/dev/')) continue; // development-only previews
  const context = { globalThis: {} };
  vm.runInNewContext(readFileSync(file, 'utf8'), context);
  const manifest = context.globalThis.__RSC_MANIFEST[`${route}/page`];
  const buildManifest = JSON.parse(readFileSync(join(file, '..', 'page', 'build-manifest.json'), 'utf8'));
  const chunks = new Set([...buildManifest.rootMainFiles, ...buildManifest.polyfillFiles]);
  for (const [entry, files] of Object.entries(manifest.entryJSFiles)) {
    if (entry.includes('/apps/web/src/app/')) files.forEach((f) => chunks.add(f));
  }
  const bytes = [...chunks].reduce((sum, chunk) => sum + size(chunk), 0);
  const budget = BUDGETS.find((b) => b.match.test(route));
  routes.push({ route, kb: Math.round(bytes / 102.4) / 10, budget });
}

routes.sort((a, b) => b.kb - a.kb);
const over = routes.filter((r) => r.kb > r.budget.kb);
if (process.argv.includes('--report')) {
  for (const r of routes)
    console.log(`${r.kb.toFixed(1).padStart(7)} KB  ${r.route}  (${r.budget.name}: ${r.budget.kb} KB)`);
}
console.log(`${routes.length} routes; largest ${routes[0]?.route} at ${routes[0]?.kb} KB gzip.`);
for (const r of over)
  console.error(`Over budget: ${r.route} ${r.kb} KB > ${r.budget.kb} KB (${r.budget.name})`);
process.exit(over.length > 0 ? 1 : 0);
