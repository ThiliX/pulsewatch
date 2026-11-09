import { useCallback, useEffect, useState } from 'react'

export type ApiStatusState =
  | { kind: 'loading' }
  | { kind: 'ready'; checkedAt: Date }
  | { kind: 'not-ready'; checkedAt: Date; httpStatus: number }
  | { kind: 'unreachable'; checkedAt: Date }

const READY_URL = '/health/ready'
const TIMEOUT_MS = 5000

/** Asks the API's readiness endpoint whether PulseWatch itself is up. */
export function useApiStatus() {
  const [state, setState] = useState<ApiStatusState>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let unmounted = false
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS)

    fetch(READY_URL, { signal: controller.signal, headers: { Accept: 'text/plain' } })
      .then((response) => {
        if (unmounted) return
        const checkedAt = new Date()
        setState(
          response.ok
            ? { kind: 'ready', checkedAt }
            : { kind: 'not-ready', checkedAt, httpStatus: response.status },
        )
      })
      .catch(() => {
        // Network error or our own timeout. Aborts caused by unmounting are ignored.
        if (unmounted) return
        setState({ kind: 'unreachable', checkedAt: new Date() })
      })
      .finally(() => clearTimeout(timer))

    return () => {
      unmounted = true
      clearTimeout(timer)
      controller.abort()
    }
  }, [attempt])

  const retry = useCallback(() => {
    setState({ kind: 'loading' })
    setAttempt((n) => n + 1)
  }, [])

  return { state, retry }
}
