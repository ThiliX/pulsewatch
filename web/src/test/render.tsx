import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import App from '../App.tsx'
import { ToastProvider } from '../components/Toast.tsx'
import { LocationProbe } from './LocationProbe.tsx'

/** Renders the whole app at a route, with a fresh cache and no retries. */
export function renderApp(path: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, refetchInterval: false },
      mutations: { retry: false },
    },
  })
  const user = userEvent.setup()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <ToastProvider>
          <App />
          <LocationProbe />
        </ToastProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { ...view, user, queryClient }
}
