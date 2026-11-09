import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { getMe, logout } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'

/** The signed-in user (null when signed out). */
export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: ({ signal }) => getMe(signal),
    staleTime: 5 * 60_000,
  })
}

export function useSignOut() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  return useMutation({
    mutationFn: logout,
    onSettled: () => {
      // Forget everything that belonged to this user, even if the request failed.
      queryClient.clear()
      queryClient.setQueryData(queryKeys.me, null)
      void navigate('/login', { replace: true })
    },
  })
}

/**
 * Only same-site paths are allowed as a return address after signing in, so a crafted link
 * can't send the user to another site.
 */
export function safeReturnTo(value: string | null): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/'
  }
  return value
}
