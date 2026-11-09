import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { isApiError } from './client.ts'
import { queryKeys } from './queryKeys.ts'

/**
 * Shared settings:
 * - client errors (4xx) are not retried; network and server errors are retried twice;
 * - a 401 anywhere means the session ended, so the user is marked signed out and RequireAuth
 *   sends them to the sign-in page.
 */
export function createQueryClient(): QueryClient {
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: handleUnauthorized }),
    mutationCache: new MutationCache({ onError: handleUnauthorized }),
    defaultOptions: {
      queries: {
        staleTime: 10_000,
        retry: (failureCount, error) =>
          !(isApiError(error) && error.status < 500) && failureCount < 2,
      },
      mutations: { retry: false },
    },
  })

  function handleUnauthorized(error: unknown) {
    if (isApiError(error, 401) && client.getQueryData(queryKeys.me)) {
      client.setQueryData(queryKeys.me, null)
    }
  }

  return client
}
