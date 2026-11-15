import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The dev server proxies API and health calls to the ASP.NET Core API, so the browser
// sees a single origin. This keeps the auth cookie first-party and avoids CORS.
const apiTarget = process.env.PULSEWATCH_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      '/health': { target: apiTarget, changeOrigin: false },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    // Must stay above the 10 s findBy* timeout in src/test/setup.ts: the first test that loads the lazy
    // chart module can need several seconds on a slow runner, and the default 5 s test timeout then
    // fired before the element query could finish.
    testTimeout: 20_000,
  },
})
