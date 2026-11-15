import { Navigate, Outlet, useLocation } from 'react-router'
import { ErrorState, LoadingBlock } from '../../components/States.tsx'
import { useMe } from './auth.ts'

/** Shows its child routes only to signed-in users; everyone else is sent to the sign-in page. */
export function RequireAuth() {
  const me = useMe()
  const location = useLocation()

  if (me.isPending) {
    return (
      <div className="mx-auto max-w-md p-8">
        <LoadingBlock label="Checking your session" rows={2} />
      </div>
    )
  }
  if (me.isError) {
    return (
      <div className="mx-auto max-w-lg p-8">
        <ErrorState
          title="Couldn't check your session"
          error={me.error}
          onRetry={() => void me.refetch()}
        />
      </div>
    )
  }
  if (!me.data) {
    const returnTo = `${location.pathname}${location.search}`
    const search = returnTo === '/' ? '' : `?returnTo=${encodeURIComponent(returnTo)}`
    return <Navigate to={`/login${search}`} replace />
  }
  return <Outlet />
}
