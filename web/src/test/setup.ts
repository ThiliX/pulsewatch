import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './server.ts'

// The first render of a page can include loading a large lazy module (the chart library). The
// default 1 s wait for findBy* queries is too tight on a busy machine or CI runner.
configure({ asyncUtilTimeout: 5000 })

// Any request without a handler fails the test, so no test silently depends on a real network.
beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))

afterEach(() => {
  cleanup()
  server.resetHandlers()
  // Forget cookies (the XSRF token) between tests.
  for (const cookie of document.cookie.split(';')) {
    const name = cookie.split('=')[0]?.trim()
    if (name) document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/`
  }
})

afterAll(() => server.close())

// jsdom has no layout engine; the chart's ResponsiveContainer needs ResizeObserver to exist.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver
