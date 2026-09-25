import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@messages': fileURLToPath(new URL('./messages', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'messages/**/*.test.ts'],
    css: false,
    // next-intl's ESM build imports 'next/navigation' without an extension; let Vite resolve it.
    server: { deps: { inline: ['next-intl'] } },
    env: { TZ: 'UTC' },
  },
});
