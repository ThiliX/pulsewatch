import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { CheckSeries } from '../../api/types.ts'
import { formatMs, plural } from '../../lib/format.ts'
import { formatAxisTime, formatLocal } from '../../lib/time.ts'

/**
 * Lines are drawn between neighbouring points only, so a point with no data on either side would be
 * invisible. Draw a dot for those.
 */
function isolatedDot(rows: Row[], key: 'avg' | 'max', color: string) {
  function IsolatedDot(props: { cx?: number; cy?: number; index?: number }) {
    const { cx, cy, index = 0 } = props
    const isolated = rows[index - 1]?.[key] == null && rows[index + 1]?.[key] == null
    if (!isolated || cx === undefined || cy === undefined || rows[index]?.[key] == null) {
      return <g key={`${key}-${index}`} />
    }
    return <circle key={`${key}-${index}`} cx={cx} cy={cy} r={3} fill={color} />
  }
  return IsolatedDot
}

type Row = { t: number; avg: number | null; max: number | null; failures: number; checks: number }

/** Latency of successful checks (average and maximum per bucket) with failures as bars. */
export function LatencyChart({ series }: { series: CheckSeries }) {
  const rows: Row[] = series.points.map((p) => ({
    t: Date.parse(p.bucketStartUtc),
    avg: p.avgDurationMs,
    max: p.maxDurationMs,
    failures: p.failures,
    checks: p.checks,
  }))
  const totalChecks = rows.reduce((sum, r) => sum + r.checks, 0)
  const totalFailures = rows.reduce((sum, r) => sum + r.failures, 0)

  if (totalChecks === 0) {
    return (
      <p className="rounded-lg border border-dashed border-slate-300 p-6 text-center text-sm text-slate-600 dark:border-slate-700 dark:text-slate-400">
        No checks in this window yet.
      </p>
    )
  }

  const successfulAvgs = series.points.filter((p) => p.avgDurationMs !== null)
  const weighted = successfulAvgs.reduce(
    (acc, p) => ({
      sum: acc.sum + (p.avgDurationMs ?? 0) * (p.checks - p.failures),
      n: acc.n + p.checks - p.failures,
    }),
    { sum: 0, n: 0 },
  )
  const overallAvg = weighted.n > 0 ? Math.round(weighted.sum / weighted.n) : null
  const summary = `${plural(totalChecks, 'check')}, ${totalFailures} failed. Average latency of successful checks: ${formatMs(overallAvg)}.`

  return (
    <figure>
      <div className="h-64 w-full" role="img" aria-label={`Latency chart. ${summary}`}>
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="currentColor" strokeOpacity={0.15} />
            <XAxis
              dataKey="t"
              type="number"
              scale="time"
              domain={['dataMin', 'dataMax']}
              tickFormatter={(v: number) => formatAxisTime(v, series.bucketSeconds)}
              tick={{ fontSize: 12, fill: 'currentColor' }}
              minTickGap={40}
            />
            <YAxis
              yAxisId="ms"
              tickFormatter={(v: number) => formatMs(v)}
              tick={{ fontSize: 12, fill: 'currentColor' }}
              width={56}
            />
            <YAxis yAxisId="failures" orientation="right" allowDecimals={false} hide />
            <Tooltip
              labelFormatter={(v) =>
                formatLocal(new Date(Number(v)).toISOString(), { seconds: false })
              }
              formatter={(value, name) =>
                name === 'Failures'
                  ? [String(value), name]
                  : [formatMs(typeof value === 'number' ? value : null), name]
              }
              contentStyle={{ fontSize: 12 }}
            />
            <Legend wrapperStyle={{ fontSize: 12 }} />
            <Bar yAxisId="failures" dataKey="failures" name="Failures" fill="#be123c" barSize={4} />
            <Line
              yAxisId="ms"
              type="monotone"
              dataKey="avg"
              name="Average"
              stroke="#0369a1"
              strokeWidth={2}
              dot={isolatedDot(rows, 'avg', '#0369a1')}
              connectNulls={false}
            />
            <Line
              yAxisId="ms"
              type="monotone"
              dataKey="max"
              name="Maximum"
              stroke="#64748b"
              strokeDasharray="4 3"
              dot={isolatedDot(rows, 'max', '#64748b')}
              connectNulls={false}
            />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
      <figcaption className="mt-2 text-xs text-slate-600 dark:text-slate-400">
        {summary} Latency counts successful checks only; gaps mean no successful check in that
        period.
      </figcaption>
    </figure>
  )
}
