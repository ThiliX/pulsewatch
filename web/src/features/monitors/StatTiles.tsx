import { Info } from 'lucide-react'
import type { UptimeWindow } from '../../api/types.ts'
import { mutedText, surface } from '../../components/styles.ts'
import { formatMs, formatUptime, plural } from '../../lib/format.ts'

const WINDOW_LABELS: Record<UptimeWindow['window'], string> = {
  '24h': 'Last 24 hours',
  '7d': 'Last 7 days',
  '30d': 'Last 30 days',
}

function meterColour(percent: number): string {
  if (percent >= 99.9) return 'bg-emerald-500'
  if (percent >= 99) return 'bg-amber-400'
  return 'bg-rose-500'
}

/** Sampled uptime and p95 per window, always with the sample count; "No data" when there were no checks. */
export function StatTiles({ windows }: { windows: UptimeWindow[] }) {
  return (
    <section aria-labelledby="stats-heading" className="space-y-3">
      <h2 id="stats-heading" className="sr-only">
        Uptime and latency
      </h2>
      <ul className="grid gap-4 sm:grid-cols-3">
        {windows.map((w) => (
          <li key={w.window} className={`${surface} p-5`}>
            <p className={`text-sm font-medium ${mutedText}`}>{WINDOW_LABELS[w.window]}</p>
            <p
              className="mt-2 text-3xl font-semibold tracking-tight text-zinc-900 tabular-nums dark:text-white"
              data-testid={`uptime-${w.window}`}
            >
              {formatUptime(w.uptimePercent)}
            </p>
            <div
              aria-hidden="true"
              className="mt-3 h-1.5 overflow-hidden rounded-full bg-zinc-100 dark:bg-zinc-800"
            >
              {w.uptimePercent !== null && (
                <div
                  className={`h-full rounded-full ${meterColour(w.uptimePercent)}`}
                  style={{ width: `${Math.max(2, w.uptimePercent)}%` }}
                />
              )}
            </div>
            <p className={`mt-2 text-xs ${mutedText}`}>
              {w.totalChecks === 0
                ? 'No checks in this window'
                : `${w.successfulChecks} of ${plural(w.totalChecks, 'check')} succeeded`}
            </p>
            <p className="mt-3 flex items-center justify-between border-t border-zinc-100 pt-3 text-sm dark:border-zinc-800">
              <span className={mutedText}>p95 latency</span>
              <span className="font-medium text-zinc-900 tabular-nums dark:text-zinc-100">
                {formatMs(w.p95DurationMs)}
              </span>
            </p>
          </li>
        ))}
      </ul>
      <details className={`group text-sm ${mutedText}`}>
        <summary className="inline-flex cursor-pointer items-center gap-1.5 font-medium hover:text-zinc-900 dark:hover:text-zinc-100">
          <Info aria-hidden="true" className="size-4" />
          How are these calculated?
        </summary>
        <p className="mt-2 max-w-3xl">
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
