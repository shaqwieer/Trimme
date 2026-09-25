import axe from 'axe-core';
import { expect } from 'vitest';

/**
 * Runs axe-core against rendered markup and fails on any violation. jsdom has no layout engine,
 * so colour-contrast is disabled here (it is covered by tokens.test.ts and Playwright axe runs).
 */
export async function expectNoAxeViolations(container: Element = document.body) {
  const results = await axe.run(container, {
    rules: { 'color-contrast': { enabled: false } },
  });
  const summary = results.violations.map(
    (v) => `${v.id}: ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`,
  );
  expect(summary).toEqual([]);
}
