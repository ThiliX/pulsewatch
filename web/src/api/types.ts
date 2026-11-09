// Mirrors the API's JSON (camelCase, enums as strings, times as ISO-8601 UTC strings).

export type MonitorState = 'Unknown' | 'Healthy' | 'Degraded' | 'Down' | 'Paused'

export const MONITOR_STATES: readonly MonitorState[] = [
  'Healthy',
  'Degraded',
  'Down',
  'Unknown',
  'Paused',
]

export type CheckOutcome =
  | 'Success'
  | 'HttpStatusFailure'
  | 'Timeout'
  | 'DnsFailure'
  | 'TlsFailure'
  | 'ConnectionFailure'
  | 'RedirectRejected'
  | 'DestinationBlocked'
  | 'ResponseTooLarge'
  | 'UnexpectedError'

export type IncidentStatus = 'Open' | 'Resolved'

export type NotificationEventType = 'IncidentOpened' | 'IncidentResolved'

export type NotificationStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed' | 'Exhausted'

export type Me = { id: string; email: string }

export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number }

export type Monitor = {
  id: string
  name: string
  url: string
  isEnabled: boolean
  state: MonitorState
  isStale: boolean
  intervalSeconds: number
  timeoutMs: number
  failureThreshold: number
  recoveryThreshold: number
  expectedStatusMin: number
  expectedStatusMax: number
  consecutiveFailures: number
  consecutiveSuccesses: number
  lastCheckedAtUtc: string | null
  lastOutcome: CheckOutcome | null
  lastStatusCode: number | null
  lastDurationMs: number | null
  nextCheckAtUtc: string
  createdAtUtc: string
  updatedAtUtc: string
  version: string
}

/** Fields the user edits. Create sends all of them; edit sends only the changed ones. */
export type MonitorInput = {
  name: string
  url: string
  intervalSeconds: number
  timeoutMs: number
  failureThreshold: number
  recoveryThreshold: number
  expectedStatusMin: number
  expectedStatusMax: number
}

export type Check = {
  id: string
  startedAtUtc: string
  completedAtUtc: string
  durationMs: number
  outcome: CheckOutcome
  httpStatusCode: number | null
  errorSummary: string | null
}

export type StatsWindowName = '24h' | '7d' | '30d'

export type UptimeWindow = {
  window: StatsWindowName
  fromUtc: string
  toUtc: string
  totalChecks: number
  successfulChecks: number
  uptimePercent: number | null
  p95DurationMs: number | null
}

export type MonitorStats = { windows: UptimeWindow[] }

export type SeriesPoint = {
  bucketStartUtc: string
  checks: number
  failures: number
  avgDurationMs: number | null
  maxDurationMs: number | null
}

export type CheckSeries = {
  window: StatsWindowName
  bucketSeconds: number
  fromUtc: string
  toUtc: string
  points: SeriesPoint[]
}

export type Incident = {
  id: string
  monitorId: string
  monitorName: string
  status: IncidentStatus
  firstFailureAtUtc: string
  openedAtUtc: string
  resolvedAtUtc: string | null
  durationSeconds: number
  failureCount: number
  lastFailureAtUtc: string | null
  openingOutcome: CheckOutcome
  openingStatusCode: number | null
}

export type IncidentNotification = {
  eventType: NotificationEventType
  channel: 'Email'
  recipient: string
  status: NotificationStatus
  attemptCount: number
  lastAttemptAtUtc: string | null
  sentAtUtc: string | null
  lastErrorCategory: string | null
}

export type IncidentDetail = { incident: Incident; notifications: IncidentNotification[] }

export type DashboardSummary = {
  totalMonitors: number
  healthy: number
  degraded: number
  down: number
  unknown: number
  paused: number
  stale: number
  openIncidents: number
  workerLastSeenAtUtc: string | null
  workerOnline: boolean
}
