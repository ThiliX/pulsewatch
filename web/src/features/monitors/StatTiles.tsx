import type { UptimeWindow } from '../../api/types.ts'
import { formatMs, formatUptime, plural } from '../../lib/format.ts'

const WINDOW_LABELS: Record<UptimeWindow['window'], string> = {
  '24h': 'Last 24 hours',
  '7d': 'Last 7 days',
  '30d': 'Last 30 days',
}

/** Sampled uptime and p95 per window, always with the sample count; "No data" when there were no checks. */
export function StatTiles({ windows }: { windows: UptimeWindow[] }) {
  return (
    <section aria-labelledby="stats-heading" className="space-y-2">
      <h2 id="stats-heading" className="text-lg font-semibold">
        Uptime and latency
      </h2>
      <ul className="grid gap-3 sm:grid-cols-3">
        {windows.map((w) => (
          <li
            key={w.window}
            className="rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900"
          >
            <p className="text-sm text-slate-600 dark:text-slate-400">{WINDOW_LABELS[w.window]}</p>
            <p className="mt-1 text-2xl font-semibold" data-testid={`uptime-${w.window}`}>
              {formatUptime(w.uptimePercent)}
            </p>
            <p className="text-xs text-slate-600 dark:text-slate-400">
              {w.totalChecks === 0
                ? 'No checks in this window'
                : `${w.successfulChecks} of ${plural(w.totalChecks, 'check')} succeeded`}
            </p>
            <p className="mt-2 text-sm">
              p95 latency: <span className="font-medium">{formatMs(w.p95DurationMs)}</span>
            </p>
          </li>
        ))}
      </ul>
      <details className="text-sm text-slate-600 dark:text-slate-400">
        <summary className="cursor-pointer font-medium">How are these calculated?</summary>
        <p className="mt-1">
          <strong>Sampled uptime</strong> is the share of PulseWatch's checks that succeeded in the
          window. It is based on samples taken from one location, so it is not the same as true
          availability. With no checks it shows "No data", never 100 %. <strong>p95 latency</strong>{' '}
          is the 95th percentile of response times of successful checks: 95 % of them were at least
          this fast.
        </p>
      </details>
    </section>
  )
}
