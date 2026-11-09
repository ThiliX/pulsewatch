import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { lazy, Suspense, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { errorMessage, isApiError } from '../../api/client.ts'
import {
  deleteMonitor,
  getCheckSeries,
  getMonitor,
  getMonitorStats,
  listChecks,
  listMonitorIncidents,
  monitorAction,
  type MonitorAction,
} from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import type { Monitor, StatsWindowName } from '../../api/types.ts'
import { Button, ButtonLink } from '../../components/Button.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import {
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
  Pagination,
} from '../../components/States.tsx'
import { useToast } from '../../components/toastContext.ts'
import { describeOutcome, displayUrl, formatInterval, formatMs } from '../../lib/format.ts'
import { formatDuration } from '../../lib/time.ts'
import { StatTiles } from './StatTiles.tsx'
import { IncidentStatusBadge, StaleBadge, StatusBadge } from './StatusBadge.tsx'

// The chart library is large; load it only when a monitor page is opened.
const LatencyChart = lazy(() =>
  import('./LatencyChart.tsx').then((m) => ({ default: m.LatencyChart })),
)

const REFRESH_MS = 30_000
/** Until a new or resumed monitor has its first result, look more often. */
const AWAITING_FIRST_CHECK_MS = 3_000
const CHECKS_PAGE_SIZE = 20
const INCIDENTS_PAGE_SIZE = 5

export function MonitorDetailPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const monitor = useQuery({
    queryKey: queryKeys.monitor(id),
    queryFn: ({ signal }) => getMonitor(id, signal),
    refetchInterval: (query) => {
      const m = query.state.data?.monitor
      return m && m.isEnabled && !m.lastCheckedAtUtc ? AWAITING_FIRST_CHECK_MS : REFRESH_MS
    },
  })

  // When a new check result arrives, refresh the stats, chart, checks and incidents with it, so
  // the page never shows a mix of older and newer data.
  const lastChecked = monitor.data?.monitor.lastCheckedAtUtc ?? null
  const seenLastChecked = useRef(lastChecked)
  useEffect(() => {
    if (lastChecked === seenLastChecked.current) return
    seenLastChecked.current = lastChecked
    void queryClient.invalidateQueries({
      queryKey: queryKeys.monitor(id),
      predicate: (query) => query.queryKey.length > 2,
    })
  }, [id, lastChecked, queryClient])

  if (monitor.isPending) return <LoadingBlock label="Loading the monitor" rows={4} />
  if (monitor.isError && !monitor.data) {
    return isApiError(monitor.error, 404) ? (
      <div className="space-y-4">
        <PageHeader title="Monitor not found">It may have been deleted.</PageHeader>
        <ButtonLink to="/monitors">Back to monitors</ButtonLink>
      </div>
    ) : (
      <ErrorState
        title="Couldn't load the monitor"
        error={monitor.error}
        onRetry={() => void monitor.refetch()}
      />
    )
  }

  const m = monitor.data.monitor
  return (
    <div className="space-y-8">
      <PageHeader title={m.name} actions={<MonitorActions monitor={m} />}>
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge state={m.state} />
          {m.isStale && <StaleBadge />}
          <span className="break-all">{displayUrl(m.url)}</span>
        </div>
        <div className="mt-1">
          <LastUpdated updatedAt={monitor.dataUpdatedAt} refreshFailed={monitor.isError} />
        </div>
      </PageHeader>

      <MonitorFacts monitor={m} />
      <Stats id={id} />
      <Chart id={id} />
      <ChecksSection id={id} />
      <IncidentsSection id={id} />
    </div>
  )
}

function MonitorActions({ monitor }: { monitor: Monitor }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const toast = useToast()
  const [confirmDelete, setConfirmDelete] = useState(false)

  const refreshAll = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.monitors })
    await queryClient.invalidateQueries({ queryKey: queryKeys.summary })
  }, [queryClient])

  const action = useMutation({
    mutationFn: (kind: MonitorAction) => monitorAction(monitor.id, kind),
    onSuccess: async (_, kind) => {
      await refreshAll()
      if (kind === 'check-now') {
        toast.show('Check requested. The result appears within a few seconds.', 'success')
        // Pick up the result without waiting for the next 30-second refresh.
        setTimeout(
          () => void queryClient.invalidateQueries({ queryKey: queryKeys.monitor(monitor.id) }),
          5000,
        )
      } else {
        toast.show(
          kind === 'pause' ? 'Monitor paused.' : 'Monitor resumed. The next check runs shortly.',
          'success',
        )
      }
    },
    onError: (error) => {
      const message =
        isApiError(error, 429) && error.retryAfterSeconds
          ? `A check was requested recently. Try again in ${error.retryAfterSeconds} s.`
          : errorMessage(error)
      toast.show(message, 'error')
    },
  })

  const remove = useMutation({
    mutationFn: () => deleteMonitor(monitor.id),
    onSuccess: async () => {
      setConfirmDelete(false)
      queryClient.removeQueries({ queryKey: queryKeys.monitor(monitor.id) })
      await refreshAll()
      await queryClient.invalidateQueries({ queryKey: queryKeys.incidents })
      toast.show(`"${monitor.name}" was deleted.`, 'success')
      void navigate('/monitors', { replace: true })
    },
    onError: (error) => toast.show(errorMessage(error), 'error'),
  })

  const paused = !monitor.isEnabled
  const onCancelDelete = useCallback(() => setConfirmDelete(false), [])

  return (
    <>
      <Button
        onClick={() => action.mutate('check-now')}
        disabled={paused || action.isPending}
        title={paused ? 'Resume the monitor to run a check.' : undefined}
      >
        Check now
      </Button>
      <Button
        onClick={() => action.mutate(paused ? 'resume' : 'pause')}
        disabled={action.isPending}
      >
        {paused ? 'Resume' : 'Pause'}
      </Button>
      <ButtonLink to={`/monitors/${monitor.id}/edit`}>Edit</ButtonLink>
      <Button variant="danger" onClick={() => setConfirmDelete(true)}>
        Delete
      </Button>
      <ConfirmDialog
        open={confirmDelete}
        title={`Delete "${monitor.name}"?`}
        confirmLabel="Delete monitor"
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onCancel={onCancelDelete}
      >
        This permanently deletes the monitor with its check history, incidents and email history. It
        can't be undone.
      </ConfirmDialog>
    </>
  )
}

function MonitorFacts({ monitor }: { monitor: Monitor }) {
  const facts: { label: string; value: ReactNode }[] = [
    {
      label: 'Last check',
      value:
        monitor.lastCheckedAtUtc && monitor.lastOutcome ? (
          <>
            {describeOutcome(monitor.lastOutcome, monitor.lastStatusCode)}
            {monitor.lastOutcome === 'Success' && ` in ${formatMs(monitor.lastDurationMs)}`},{' '}
            <LocalTime iso={monitor.lastCheckedAtUtc} relative />
          </>
        ) : (
          'Not checked yet'
        ),
    },
    {
      label: 'Next check',
      value: monitor.isEnabled ? <LocalTime iso={monitor.nextCheckAtUtc} relative /> : 'Paused',
    },
    {
      label: 'Schedule',
      value: `${formatInterval(monitor.intervalSeconds)}, timeout ${formatMs(monitor.timeoutMs)}`,
    },
    {
      label: 'Expected status',
      value: `${monitor.expectedStatusMin}–${monitor.expectedStatusMax}`,
    },
    {
      label: 'Alerting',
      value: `Incident after ${monitor.failureThreshold} failed ${monitor.failureThreshold === 1 ? 'check' : 'checks'} in a row; resolves after ${monitor.recoveryThreshold} ${monitor.recoveryThreshold === 1 ? 'success' : 'successes'}`,
    },
  ]
  return (
    <dl className="grid gap-x-6 gap-y-3 rounded-lg border border-slate-200 bg-white p-4 text-sm sm:grid-cols-2 dark:border-slate-800 dark:bg-slate-900">
      {facts.map((f) => (
        <div key={f.label}>
          <dt className="text-slate-600 dark:text-slate-400">{f.label}</dt>
          <dd className="font-medium">{f.value}</dd>
        </div>
      ))}
    </dl>
  )
}

function Stats({ id }: { id: string }) {
  const stats = useQuery({
    queryKey: queryKeys.monitorStats(id),
    queryFn: ({ signal }) => getMonitorStats(id, signal),
    refetchInterval: REFRESH_MS,
  })
  if (stats.isPending) return <LoadingBlock label="Loading uptime" rows={1} />
  if (stats.isError && !stats.data) {
    return (
      <ErrorState
        title="Couldn't load uptime"
        error={stats.error}
        onRetry={() => void stats.refetch()}
      />
    )
  }
  return <StatTiles windows={stats.data.windows} />
}

const WINDOWS: StatsWindowName[] = ['24h', '7d', '30d']

function Chart({ id }: { id: string }) {
  const [window, setWindow] = useState<StatsWindowName>('24h')
  const series = useQuery({
    queryKey: queryKeys.monitorSeries(id, window),
    queryFn: ({ signal }) => getCheckSeries(id, window, signal),
    refetchInterval: REFRESH_MS,
    placeholderData: keepPreviousData,
  })

  return (
    <section aria-labelledby="chart-heading" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="chart-heading" className="text-lg font-semibold">
          Response time
        </h2>
        <div role="group" aria-label="Chart window" className="flex gap-1">
          {WINDOWS.map((w) => (
            <Button
              key={w}
              variant={w === window ? 'primary' : 'secondary'}
              aria-pressed={w === window}
              onClick={() => setWindow(w)}
            >
              {w}
            </Button>
          ))}
        </div>
      </div>
      {series.isPending ? (
        <LoadingBlock label="Loading the chart" rows={1} />
      ) : series.isError && !series.data ? (
        <ErrorState
          title="Couldn't load the chart"
          error={series.error}
          onRetry={() => void series.refetch()}
        />
      ) : (
        <Suspense fallback={<LoadingBlock label="Loading the chart" rows={1} />}>
          <LatencyChart series={series.data} />
        </Suspense>
      )}
    </section>
  )
}

function ChecksSection({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const checks = useQuery({
    queryKey: queryKeys.monitorChecks(id, page),
    queryFn: ({ signal }) => listChecks(id, page, CHECKS_PAGE_SIZE, signal),
    refetchInterval: page === 1 ? REFRESH_MS : false,
    placeholderData: keepPreviousData,
  })

  return (
    <section aria-labelledby="checks-heading" className="space-y-3">
      <h2 id="checks-heading" className="text-lg font-semibold">
        Checks in the last 24 hours
      </h2>
      {checks.isPending ? (
        <LoadingBlock label="Loading checks" />
      ) : checks.isError && !checks.data ? (
        <ErrorState
          title="Couldn't load checks"
          error={checks.error}
          onRetry={() => void checks.refetch()}
        />
      ) : checks.data.items.length === 0 ? (
        <p className="rounded-lg border border-dashed border-slate-300 p-6 text-center text-sm text-slate-600 dark:border-slate-700 dark:text-slate-400">
          No checks in the last 24 hours.
        </p>
      ) : (
        <>
          <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
            <table className="w-full text-left text-sm">
              <caption className="sr-only">Recent checks, newest first</caption>
              <thead className="border-b border-slate-200 text-xs uppercase text-slate-500 dark:border-slate-800 dark:text-slate-400">
                <tr>
                  <th scope="col" className="px-4 py-2">
                    Time
                  </th>
                  <th scope="col" className="px-4 py-2">
                    Result
                  </th>
                  <th scope="col" className="px-4 py-2">
                    Duration
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
                {checks.data.items.map((c) => (
                  <tr key={c.id}>
                    <td className="whitespace-nowrap px-4 py-2">
                      <LocalTime iso={c.startedAtUtc} />
                    </td>
                    <td className="px-4 py-2">
                      <span aria-hidden="true">{c.outcome === 'Success' ? '✓ ' : '✕ '}</span>
                      {describeOutcome(c.outcome, c.httpStatusCode)}
                      {c.errorSummary &&
                        c.outcome !== 'Success' &&
                        c.outcome !== 'HttpStatusFailure' && (
                          <span className="block text-xs text-slate-600 dark:text-slate-400">
                            {c.errorSummary}
                          </span>
                        )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2">{formatMs(c.durationMs)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            label="Check pages"
            page={page}
            pageSize={CHECKS_PAGE_SIZE}
            totalCount={checks.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
    </section>
  )
}

function IncidentsSection({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const incidents = useQuery({
    queryKey: queryKeys.monitorIncidents(id, page),
    queryFn: ({ signal }) => listMonitorIncidents(id, page, INCIDENTS_PAGE_SIZE, signal),
    refetchInterval: REFRESH_MS,
    placeholderData: keepPreviousData,
  })

  return (
    <section aria-labelledby="monitor-incidents-heading" className="space-y-3">
      <h2 id="monitor-incidents-heading" className="text-lg font-semibold">
        Incidents
      </h2>
      {incidents.isPending ? (
        <LoadingBlock label="Loading incidents" rows={2} />
      ) : incidents.isError && !incidents.data ? (
        <ErrorState
          title="Couldn't load incidents"
          error={incidents.error}
          onRetry={() => void incidents.refetch()}
        />
      ) : incidents.data.items.length === 0 ? (
        <p className="text-sm text-slate-600 dark:text-slate-400">No incidents for this monitor.</p>
      ) : (
        <>
          <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
            {incidents.data.items.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 p-3 text-sm">
                <IncidentStatusBadge status={i.status} />
                <Link
                  to={`/incidents/${i.id}`}
                  className="font-medium underline-offset-2 hover:underline"
                >
                  <LocalTime iso={i.firstFailureAtUtc} />
                </Link>
                <span className="text-slate-600 dark:text-slate-400">
                  {describeOutcome(i.openingOutcome, i.openingStatusCode)} ·{' '}
                  {formatDuration(i.durationSeconds)}
                  {i.status === 'Open' ? ' so far' : ''}
                </span>
              </li>
            ))}
          </ul>
          <Pagination
            label="Incident pages"
            page={page}
            pageSize={INCIDENTS_PAGE_SIZE}
            totalCount={incidents.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
    </section>
  )
}
