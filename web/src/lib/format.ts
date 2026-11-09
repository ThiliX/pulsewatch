import type { CheckOutcome, MonitorState } from '../api/types.ts'

/**
 * Sampled uptime with two decimals, truncated rather than rounded so a single failure never
 * shows as "100 %". No checks → "No data", never 100 %.
 */
export function formatUptime(percent: number | null): string {
  if (percent === null) {
    return 'No data'
  }
  const truncated = Math.floor(percent * 100) / 100
  return `${truncated.toFixed(2)} %`
}

/** "1 check", "3 checks". */
export function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`
}

export function formatMs(ms: number | null): string {
  if (ms === null) return '—'
  return ms >= 1000 ? `${(ms / 1000).toFixed(ms >= 10_000 ? 0 : 1)} s` : `${ms} ms`
}

export function formatInterval(seconds: number): string {
  if (seconds % 3600 === 0) return `every ${seconds / 3600} h`
  if (seconds % 60 === 0) return `every ${seconds / 60} min`
  return `every ${seconds} s`
}

const OUTCOME_LABELS: Record<CheckOutcome, string> = {
  Success: 'Success',
  HttpStatusFailure: 'Unexpected status',
  Timeout: 'Timed out',
  DnsFailure: 'DNS lookup failed',
  TlsFailure: 'TLS / certificate error',
  ConnectionFailure: 'Connection failed',
  RedirectRejected: 'Redirect rejected',
  DestinationBlocked: 'Destination blocked',
  ResponseTooLarge: 'Response too large',
  UnexpectedError: 'Unexpected error',
}

/** "Unexpected status 503", "Timed out", … */
export function describeOutcome(outcome: CheckOutcome, statusCode: number | null): string {
  if (outcome === 'HttpStatusFailure' && statusCode !== null) {
    return `${OUTCOME_LABELS[outcome]} ${statusCode}`
  }
  if (outcome === 'Success' && statusCode !== null) {
    return `${statusCode} OK`
  }
  return OUTCOME_LABELS[outcome]
}

export const STATE_DESCRIPTIONS: Record<MonitorState, string> = {
  Healthy: 'The last check succeeded.',
  Degraded: 'Recent checks failed, but not enough to open an incident.',
  Down: 'An incident is open.',
  Unknown: 'No result yet since the monitor was created, resumed or changed.',
  Paused: 'Scheduled checks are paused.',
}

/** The monitored URL without its query string, which may hold API keys. */
export function displayUrl(url: string): string {
  try {
    const parsed = new URL(url)
    return `${parsed.origin}${parsed.pathname}${parsed.search ? '?…' : ''}`
  } catch {
    return url
  }
}
