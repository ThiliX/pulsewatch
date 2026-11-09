/**
 * Thin fetch wrapper for the PulseWatch API:
 * - same-origin cookies (the auth cookie is HttpOnly; the browser sends it for us),
 * - the anti-forgery token on every write (read from the XSRF-TOKEN cookie, fetched when missing,
 *   refreshed and retried once if the server rejects it),
 * - RFC 9457 Problem Details turned into a typed ApiError with per-field messages.
 */

const XSRF_COOKIE = 'XSRF-TOKEN'
const XSRF_HEADER = 'X-XSRF-TOKEN'
const ANTIFORGERY_PATH = '/api/v1/auth/antiforgery'
const ANTIFORGERY_FAILED_TITLE = 'Missing or invalid anti-forgery token.'

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | undefined
  /** Server validation messages keyed by field name (camelCase, as in the request body). */
  readonly fieldErrors: Record<string, string[]>
  /** Seconds from a Retry-After header, when the server sent one. */
  readonly retryAfterSeconds: number | undefined

  constructor(init: {
    status: number
    title: string
    detail?: string | undefined
    fieldErrors?: Record<string, string[]>
    retryAfterSeconds?: number | undefined
  }) {
    super(init.detail ? `${init.title} ${init.detail}` : init.title)
    this.name = 'ApiError'
    this.status = init.status
    this.title = init.title
    this.detail = init.detail
    this.fieldErrors = init.fieldErrors ?? {}
    this.retryAfterSeconds = init.retryAfterSeconds
  }
}

export function isApiError(error: unknown, status?: number): error is ApiError {
  return error instanceof ApiError && (status === undefined || error.status === status)
}

/** A short message suitable for a toast or an error panel. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.detail ? `${error.title} ${error.detail}` : error.title
  }
  return 'Could not reach PulseWatch. Check your connection and try again.'
}

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PATCH' | 'DELETE'
  body?: unknown
  /** Sent as If-Match for optimistic concurrency (the monitor's ETag). */
  ifMatch?: string | undefined
  signal?: AbortSignal | undefined
}

export type ApiResponse<T> = { data: T; etag: string | null }

function readCookie(name: string): string | undefined {
  const prefix = `${name}=`
  for (const part of document.cookie.split(';')) {
    const trimmed = part.trim()
    if (trimmed.startsWith(prefix)) {
      return decodeURIComponent(trimmed.slice(prefix.length))
    }
  }
  return undefined
}

function toUrl(path: string): string {
  // Absolute URL so it also works where fetch has no document base (tests).
  return new URL(path, window.location.origin).toString()
}

async function fetchXsrfToken(): Promise<string | undefined> {
  const response = await fetch(toUrl(ANTIFORGERY_PATH), {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) {
    return undefined
  }
  const body = (await response.json()) as { token?: string }
  return body.token ?? readCookie(XSRF_COOKIE)
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: { title?: string; detail?: string; errors?: Record<string, string[]> } = {}
  const contentType = response.headers.get('Content-Type') ?? ''
  if (contentType.includes('json')) {
    try {
      problem = (await response.json()) as typeof problem
    } catch {
      // Not valid JSON: fall back to the status text below.
    }
  }

  const retryAfter = Number(response.headers.get('Retry-After'))
  return new ApiError({
    status: response.status,
    title: problem.title ?? (response.statusText || `Request failed (HTTP ${response.status}).`),
    detail: problem.detail,
    fieldErrors: problem.errors ?? {},
    retryAfterSeconds: Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
  })
}

async function send(path: string, options: RequestOptions, xsrfToken: string | undefined) {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (xsrfToken) {
    headers[XSRF_HEADER] = xsrfToken
  }
  if (options.ifMatch) {
    headers['If-Match'] = options.ifMatch
  }

  const init: RequestInit = {
    method: options.method ?? 'GET',
    credentials: 'same-origin',
    headers,
  }
  if (options.body !== undefined) {
    init.body = JSON.stringify(options.body)
  }
  if (options.signal) {
    init.signal = options.signal
  }
  return fetch(toUrl(path), init)
}

export async function apiRequest<T>(
  path: string,
  options: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const isWrite = (options.method ?? 'GET') !== 'GET'
  let token = isWrite ? (readCookie(XSRF_COOKIE) ?? (await fetchXsrfToken())) : undefined

  let response = await send(path, options, token)
  if (isWrite && response.status === 400) {
    const error = await toApiError(response.clone())
    if (error.title === ANTIFORGERY_FAILED_TITLE) {
      // The token belongs to an older session (signed in or out elsewhere): refresh once and retry.
      token = await fetchXsrfToken()
      response = await send(path, options, token)
    }
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  const etag = response.headers.get('ETag')
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return { data: undefined as T, etag }
  }
  return { data: (await response.json()) as T, etag }
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  return (await apiRequest<T>(path, { signal })).data
}
