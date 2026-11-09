import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type {
  CheckSeries,
  DashboardSummary,
  Incident,
  Me,
  Monitor,
  MonitorStats,
  Paged,
} from '../api/types.ts'

export const server = setupServer()

export const ME: Me = { id: 'user-1', email: 'owner@example.com' }

export function paged<T>(items: T[], page = 1, pageSize = 25, totalCount = items.length): Paged<T> {
  return { items, page, pageSize, totalCount }
}

export function problem(status: number, title: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json(
    { type: 'about:blank', title, status, ...extra },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

const iso = (minutesAgo: number) => new Date(Date.now() - minutesAgo * 60_000).toISOString()

export function monitor(overrides: Partial<Monitor> = {}): Monitor {
  return {
    id: 'mon-1',
    name: 'Orders API',
    url: 'https://api.example.com/health',
    isEnabled: true,
    state: 'Healthy',
    isStale: false,
    intervalSeconds: 60,
    timeoutMs: 10_000,
    failureThreshold: 3,
    recoveryThreshold: 1,
    expectedStatusMin: 200,
    expectedStatusMax: 299,
    consecutiveFailures: 0,
    consecutiveSuccesses: 4,
    lastCheckedAtUtc: iso(1),
    lastOutcome: 'Success',
    lastStatusCode: 200,
    lastDurationMs: 123,
    nextCheckAtUtc: iso(-1),
    createdAtUtc: iso(600),
    updatedAtUtc: iso(600),
    version: '42',
    ...overrides,
  }
}

export function summary(overrides: Partial<DashboardSummary> = {}): DashboardSummary {
  return {
    totalMonitors: 3,
    healthy: 2,
    degraded: 0,
    down: 1,
    unknown: 0,
    paused: 0,
    stale: 0,
    openIncidents: 1,
    workerLastSeenAtUtc: iso(0),
    workerOnline: true,
    ...overrides,
  }
}

export function incident(overrides: Partial<Incident> = {}): Incident {
  return {
    id: 'inc-1',
    monitorId: 'mon-1',
    monitorName: 'Orders API',
    status: 'Open',
    firstFailureAtUtc: iso(10),
    openedAtUtc: iso(8),
    resolvedAtUtc: null,
    durationSeconds: 600,
    failureCount: 3,
    lastFailureAtUtc: iso(8),
    openingOutcome: 'HttpStatusFailure',
    openingStatusCode: 503,
    ...overrides,
  }
}

export function emptyStats(): MonitorStats {
  return {
    windows: (['24h', '7d', '30d'] as const).map((window) => ({
      window,
      fromUtc: iso(60),
      toUtc: iso(0),
      totalChecks: 0,
      successfulChecks: 0,
      uptimePercent: null,
      p95DurationMs: null,
    })),
  }
}

export function emptySeries(): CheckSeries {
  return { window: '24h', bucketSeconds: 300, fromUtc: iso(1440), toUtc: iso(0), points: [] }
}

/** Handlers for a signed-in user, with the anti-forgery endpoint setting the XSRF cookie. */
export function signedIn(me: Me = ME) {
  return [http.get('*/api/v1/auth/me', () => HttpResponse.json(me)), antiforgery()]
}

export function signedOut() {
  return [http.get('*/api/v1/auth/me', () => problem(401, 'Not signed in.')), antiforgery()]
}

export const XSRF_TOKEN = 'test-xsrf-token'

export function antiforgery() {
  return http.get('*/api/v1/auth/antiforgery', () => {
    document.cookie = `XSRF-TOKEN=${XSRF_TOKEN}; path=/`
    return HttpResponse.json({ token: XSRF_TOKEN, headerName: 'X-XSRF-TOKEN' })
  })
}

/** Detail-page handlers for one monitor. */
export function monitorDetail(m: Monitor, stats: MonitorStats = emptyStats()) {
  return [
    http.get(`*/api/v1/monitors/${m.id}`, () =>
      HttpResponse.json(m, { headers: { ETag: `"${m.version}"` } }),
    ),
    http.get(`*/api/v1/monitors/${m.id}/stats`, () => HttpResponse.json(stats)),
    http.get(`*/api/v1/monitors/${m.id}/checks/series`, () => HttpResponse.json(emptySeries())),
    http.get(`*/api/v1/monitors/${m.id}/checks`, () => HttpResponse.json(paged([]))),
    http.get(`*/api/v1/monitors/${m.id}/incidents`, () => HttpResponse.json(paged([]))),
  ]
}
