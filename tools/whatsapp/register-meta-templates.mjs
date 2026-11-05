#!/usr/bin/env node
// Registers the WhatsApp templates in docs/whatsapp/meta-templates.json with Meta (D-140).
// The file is generated from the default wording by the unit tests (MetaTemplateCatalogTests).
//
// Usage (Node 22, no dependencies):
//   WHATSAPP_ACCESS_TOKEN=... WHATSAPP_WABA_ID=... node tools/whatsapp/register-meta-templates.mjs [--status]
//   Optional: GRAPH_API_VERSION (default v27.0).
//
// Each name and language that already exists is reported and skipped; any other is submitted for review.
// --status only lists what Meta has for each entry. The token is never printed. Exit code 1 if a submission failed.

import { readFile } from 'node:fs/promises';

const token = process.env.WHATSAPP_ACCESS_TOKEN;
const wabaId = process.env.WHATSAPP_WABA_ID;
const version = process.env.GRAPH_API_VERSION || 'v27.0';
const statusOnly = process.argv.includes('--status');

if (!token || !wabaId) {
  console.error('Set WHATSAPP_ACCESS_TOKEN and WHATSAPP_WABA_ID.');
  process.exit(2);
}

const base = `https://graph.facebook.com/${version}/${wabaId}/message_templates`;
const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
const templates = JSON.parse(await readFile(new URL('../../docs/whatsapp/meta-templates.json', import.meta.url), 'utf8'));

function describe(error) {
  if (!error) return 'unknown error';
  const parts = [`code ${error.code}${error.error_subcode ? `/${error.error_subcode}` : ''}`];
  if (error.error_user_title) parts.push(error.error_user_title);
  parts.push(error.error_user_msg || error.message);
  return parts.join(': ');
}

async function existing(name) {
  const url = new URL(base);
  url.searchParams.set('name', name);
  url.searchParams.set('fields', 'name,language,status,category');
  url.searchParams.set('limit', '50');
  const response = await fetch(url, { headers });
  const body = await response.json();
  if (!response.ok) throw new Error(`listing ${name} failed: ${describe(body.error)}`);
  return (body.data ?? []).filter((t) => t.name === name);
}

let failed = 0;
const cache = new Map();
for (const template of templates) {
  if (!cache.has(template.name)) cache.set(template.name, await existing(template.name));
  const found = cache.get(template.name).find((t) => t.language === template.language);
  if (found) {
    console.log(`exists  ${template.name} ${template.language} ${found.status} ${found.category}`);
    continue;
  }

  if (statusOnly) {
    console.log(`missing ${template.name} ${template.language}`);
    continue;
  }

  const response = await fetch(base, { method: 'POST', headers, body: JSON.stringify(template) });
  const body = await response.json();
  if (response.ok) {
    console.log(`created ${template.name} ${template.language} ${body.status ?? ''} ${body.category ?? ''} ${body.id ?? ''}`.trimEnd());
  } else {
    failed++;
    console.log(`FAILED  ${template.name} ${template.language} ${describe(body.error)}`);
  }
}

process.exit(failed > 0 ? 1 : 0);
