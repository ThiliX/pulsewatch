import type { CheckSeries } from '../../api/types.ts'
import { mutedText } from '../../components/styles.ts'
import { plural } from '../../lib/format.ts'

const SEGMENTS = 48

type Segment = { from: number; to: number; checks: number; failures: number }

/** Groups the series' buckets into a fixed number of segments, oldest first. */
function toSegments(series: CheckSeries): Segment[] {
  const points = series.points
  if (points.length === 0) return []
  const perSegment = Math.max(1, Math.ceil(points.length / SEGMENTS))
  const segments: Segment[] = []
  for (let i = 0; i < points.length; i += perSegment) {
    const group = points.slice(i, i + perSegment)
    const from = Date.parse(group[0]!.bucketStartUtc)
    segments.push({
      from,
      to: Date.parse(group[group.length - 1]!.bucketStartUtc) + series.bucketSeconds * 1000,
      checks: group.reduce((sum, p) => sum + p.checks, 0),
      failures: group.reduce((sum, p) => sum + p.failures, 0),
    })
  }
  return segments
}

function segmentClass(s: Segment): string {
  if (s.checks === 0) return 'bg-zinc-200 dark:bg-zinc-800'
  if (s.failures === 0) return 'bg-emerald-500 hover:bg-emerald-400'
  if (s.failures < s.checks) return 'bg-amber-400 hover:bg-amber-300'
  return 'bg-rose-500 hover:bg-rose-400'
}

function segmentTitle(s: Segment): string {
  const time = (ms: number) =>
    new Date(ms).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const range = `${time(s.from)}–${time(s.to)}`
  if (s.checks === 0) return `${range}: no checks`
  return `${range}: ${plural(s.checks, 'check')}, ${s.failures} failed`
}

/**
 * A status-page style bar for the last 24 hours: green = every check passed, amber = some
 * failed, red = all failed, grey = no data. The whole bar has a text summary for screen readers.
 */
export function UptimeBar({ series }: { series: CheckSeries }) {
  const segments = toSegments(series)
  const checks = segments.reduce((sum, s) => sum + s.checks, 0)
  const failures = segments.reduce((sum, s) => sum + s.failures, 0)
  const summary =
    checks === 0
      ? 'No checks in the last 24 hours.'
      : `${checks - failures} of ${plural(checks, 'check')} succeeded in the last 24 hours.`

  return (
    <div>
      <div role="img" aria-label={summary} className="flex h-9 items-stretch gap-[3px]">
        {segments.map((s) => (
          <span
            key={s.from}
            title={segmentTitle(s)}
            className={`flex-1 rounded-[3px] transition-colors ${segmentClass(s)}`}
          />
        ))}
      </div>
      <div className={`mt-2 flex items-center justify-between text-xs ${mutedText}`}>
        <span>24 hours ago</span>
        <span className="hidden items-center gap-3 sm:flex">
          <Legend className="bg-emerald-500" label="All passed" />
          <Legend className="bg-amber-400" label="Some failed" />
          <Legend className="bg-rose-500" label="All failed" />
          <Legend className="bg-zinc-200 dark:bg-zinc-800" label="No data" />
        </span>
        <span>Now</span>
      </div>
    </div>
  )
}

function Legend({ className, label }: { className: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span aria-hidden="true" className={`size-2 rounded-sm ${className}`} />
      {label}
    </span>
  )
}
