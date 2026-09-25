import { defineConfig, globalIgnores } from 'eslint/config';
import nextVitals from 'eslint-config-next/core-web-vitals';
import nextTs from 'eslint-config-next/typescript';

/**
 * Project guards (spec §5, §6, §9):
 * - UI text must come from the message catalogs (no literal strings in JSX).
 * - Colours must come from design tokens (no raw hex values in components).
 * - Auth/session data must never touch Web Storage.
 */
const HEX_COLOR = '/#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\\b/';

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Explicit React version: eslint-plugin-react's auto-detection uses an API removed in ESLint 10.
  { settings: { react: { version: '19.3' } } },
  globalIgnores([
    '.next/**',
    'out/**',
    'build/**',
    'coverage/**',
    'next-env.d.ts',
    'src/lib/api/schema.d.ts',
  ]),
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-globals': [
        'error',
        {
          name: 'localStorage',
          message: 'Do not use Web Storage; sessions live in HttpOnly cookies (spec §9).',
        },
        {
          name: 'sessionStorage',
          message: 'Do not use Web Storage; sessions live in HttpOnly cookies (spec §9).',
        },
      ],
      'no-restricted-properties': [
        'error',
        { object: 'window', property: 'localStorage', message: 'Do not use Web Storage (spec §9).' },
        { object: 'window', property: 'sessionStorage', message: 'Do not use Web Storage (spec §9).' },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: `Literal[value=${HEX_COLOR}]`,
          message: 'Use design tokens (CSS variables / Tailwind theme) instead of raw hex colours.',
        },
        {
          selector: `TemplateElement[value.raw=${HEX_COLOR}]`,
          message: 'Use design tokens (CSS variables / Tailwind theme) instead of raw hex colours.',
        },
      ],
      '@typescript-eslint/consistent-type-imports': ['error', { fixStyle: 'inline-type-imports' }],
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
    },
  },
  {
    files: ['src/**/*.tsx'],
    ignores: ['src/**/*.test.tsx', 'src/app/**/dev/**'],
    rules: {
      'react/jsx-no-literals': [
        'error',
        {
          noStrings: true,
          ignoreProps: true,
          noAttributeStrings: false,
          allowedStrings: ['·', '—', '–', '/', '+', '•', '|', ':', '(', ')', '%', '*', '…'],
        },
      ],
    },
  },
  {
    // The single place where brand colours may appear as literals (mirrors src/styles/tokens.css).
    files: ['src/styles/**/*.ts'],
    rules: { 'no-restricted-syntax': 'off' },
  },
  {
    files: ['src/**/*.test.{ts,tsx}', 'vitest.setup.ts'],
    rules: {
      'no-restricted-syntax': 'off',
    },
  },
]);

export default eslintConfig;
