import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleCheck, CircleX, Globe, Pause, Pencil, Play, RefreshCw, Trash2 } from 'lucide-react'
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
import { Button, ButtonLink, SegmentedControl } from '../../components/Button.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import {
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
  Pagination,
} from '../../components/States.tsx'
import { divider, mutedText, quietLink } from '../../components/styles.ts'
import { useToast } from '../../components/toastContext.ts'
import { Panel, Th } from '../../components/ui.tsx'
import { describeOutcome, displayUrl, formatInterval, formatMs } from '../../lib/format.ts'
import { formatDuration } from '../../lib/time.ts'
import { StatTiles } from './StatTiles.tsx'
import { IncidentStatusBadge, StaleBadge, StatusBadge } from './StatusBadge.tsx'
import { UptimeBar } from './UptimeBar.tsx'

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
    <div className="space-y-6">
      <PageHeader
        title={m.name}
        back={{ to: '/monitors', label: 'Monitors' }}
        actions={<MonitorActions monitor={m} />}
      >
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <span className="flex items-center gap-1">
            <StatusBadge state={m.state} />
            {m.isStale && <StaleBadge />}
          </span>
          <span className="inline-flex min-w-0 items-center gap-1.5 break-all">
            <Globe aria-hidden="true" className="size-3.5 shrink-0" />
            {displayUrl(m.url)}
          </span>
        </div>
        <div className="mt-2">
          <LastUpdated updatedAt={monitor.dataUpdatedAt} refreshFailed={monitor.isError} />
        </div>
      </PageHeader>

      <Availability id={id} />
      <Stats id={id} />
      <Chart id={id} />
      <MonitorFacts monitor={m} />
      <div className="grid gap-6 xl:grid-cols-5">
        <ChecksSection id={id} className="xl:col-span-3" />
        <IncidentsSection id={id} className="xl:col-span-2" />
      </div>
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
        <RefreshCw aria-hidden="true" />
        Check now
      </Button>
      <Button
        onClick={() => action.mutate(paused ? 'resume' : 'pause')}
        disabled={action.isPending}
      >
        {paused ? <Play aria-hidden="true" /> : <Pause aria-hidden="true" />}
        {paused ? 'Resume' : 'Pause'}
      </Button>
      <ButtonLink to={`/monitors/${monitor.id}/edit`}>
        <Pencil aria-hidden="true" />
        Edit
      </ButtonLink>
      <Button variant="danger" onClick={() => setConfirmDelete(true)}>
        <Trash2 aria-hidden="true" />
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

/** The 24-hour status bar. Shares its data with the 24h chart, so no extra request. */
function Availability({ id }: { id: string }) {
  const series = useQuery({
    queryKey: queryKeys.monitorSeries(id, '24h'),
    queryFn: ({ signal }) => getCheckSeries(id, '24h', signal),
    refetchInterval: REFRESH_MS,
  })
  return (
    <Panel
      title="Availability"
      titleId="availability-heading"
      description="Last 24 hours, in 30-minute segments"
    >
      {series.isPending ? (
        <LoadingBlock label="Loading availability" rows={1} />
      ) : series.isError && !series.data ? (
        <ErrorState
          title="Couldn't load availability"
          error={series.error}
          onRetry={() => void series.refetch()}
        />
      ) : (
        <UptimeBar series={series.data} />
      )}
    </Panel>
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
    <Panel title="Configuration" titleId="configuration-heading">
      <dl className="grid gap-x-8 gap-y-4 text-sm sm:grid-cols-2 lg:grid-cols-3">
        {facts.map((f) => (
          <div key={f.label}>
            <dt className={`text-xs font-medium tracking-wide uppercase ${mutedText}`}>
              {f.label}
            </dt>
            <dd className="mt-1 font-medium text-zinc-900 dark:text-zinc-100">{f.value}</dd>
          </div>
        ))}
      </dl>
    </Panel>
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

const WINDOWS: { value: StatsWindowName; label: string }[] = [
  { value: '24h', label: '24h' },
  { value: '7d', label: '7d' },
  { value: '30d', label: '30d' },
]

function Chart({ id }: { id: string }) {
  const [window, setWindow] = useState<StatsWindowName>('24h')
  const series = useQuery({
    queryKey: queryKeys.monitorSeries(id, window),
    queryFn: ({ signal }) => getCheckSeries(id, window, signal),
    refetchInterval: REFRESH_MS,
    placeholderData: keepPreviousData,
  })

  return (
    <Panel
      title="Response time"
      titleId="chart-heading"
      description="Average and maximum of successful checks; failures as red bars"
      actions={
        <SegmentedControl
          label="Chart window"
          options={WINDOWS}
          value={window}
          onChange={setWindow}
        />
      }
    >
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
    </Panel>
  )
}

function ChecksSection({ id, className = '' }: { id: string; className?: string }) {
  const [page, setPage] = useState(1)
  const checks = useQuery({
    queryKey: queryKeys.monitorChecks(id, page),
    queryFn: ({ signal }) => listChecks(id, page, CHECKS_PAGE_SIZE, signal),
    refetchInterval: page === 1 ? REFRESH_MS : false,
    placeholderData: keepPreviousData,
  })

  return (
    <Panel
      title="Checks in the last 24 hours"
      titleId="checks-heading"
      className={className}
      bodyClassName=""
    >
      {checks.isPending ? (
        <div className="p-5">
          <LoadingBlock label="Loading checks" />
        </div>
      ) : checks.isError && !checks.data ? (
        <div className="p-5">
          <ErrorState
            title="Couldn't load checks"
            error={checks.error}
            onRetry={() => void checks.refetch()}
          />
        </div>
      ) : checks.data.items.length === 0 ? (
        <p className={`p-8 text-center text-sm ${mutedText}`}>No checks in the last 24 hours.</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <caption className="sr-only">Recent checks, newest first</caption>
              <thead className="border-b border-zinc-200 bg-zinc-50/60 dark:border-zinc-800 dark:bg-zinc-900/60">
                <tr>
                  <Th>Time</Th>
                  <Th>Result</Th>
                  <Th className="text-right">Duration</Th>
                </tr>
              </thead>
              <tbody className={divider}>
                {checks.data.items.map((c) => {
                  const ok = c.outcome === 'Success'
                  return (
                    <tr key={c.id}>
                      <td className={`px-5 py-2.5 whitespace-nowrap ${mutedText}`}>
                        <LocalTime iso={c.startedAtUtc} />
                      </td>
                      <td className="px-5 py-2.5">
                        <span className="inline-flex items-center gap-1.5 font-medium">
                          {ok ? (
                            <CircleCheck aria-hidden="true" className="size-4 text-emerald-500" />
                          ) : (
                            <CircleX aria-hidden="true" className="size-4 text-rose-500" />
                          )}
                          {describeOutcome(c.outcome, c.httpStatusCode)}
                        </span>
                        {c.errorSummary && !ok && c.outcome !== 'HttpStatusFailure' && (
                          <span className={`block pl-5.5 text-xs ${mutedText}`}>
                            {c.errorSummary}
                          </span>
                        )}
                      </td>
                      <td className="px-5 py-2.5 text-right whitespace-nowrap tabular-nums">
                        {formatMs(c.durationMs)}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div className="border-t border-zinc-200 px-5 py-3 empty:hidden dark:border-zinc-800">
            <Pagination
              label="Check pages"
              page={page}
              pageSize={CHECKS_PAGE_SIZE}
              totalCount={checks.data.totalCount}
              onPageChange={setPage}
            />
          </div>
        </>
      )}
    </Panel>
  )
}

function IncidentsSection({ id, className = '' }: { id: string; className?: string }) {
  const [page, setPage] = useState(1)
  const incidents = useQuery({
    queryKey: queryKeys.monitorIncidents(id, page),
    queryFn: ({ signal }) => listMonitorIncidents(id, page, INCIDENTS_PAGE_SIZE, signal),
    refetchInterval: REFRESH_MS,
    placeholderData: keepPreviousData,
  })

  return (
    <Panel
      title="Incidents"
      titleId="monitor-incidents-heading"
      className={`self-start ${className}`}
      bodyClassName=""
    >
      {incidents.isPending ? (
        <div className="p-5">
          <LoadingBlock label="Loading incidents" rows={2} />
        </div>
      ) : incidents.isError && !incidents.data ? (
        <div className="p-5">
          <ErrorState
            title="Couldn't load incidents"
            error={incidents.error}
            onRetry={() => void incidents.refetch()}
          />
        </div>
      ) : incidents.data.items.length === 0 ? (
        <p className={`p-8 text-center text-sm ${mutedText}`}>No incidents for this monitor.</p>
      ) : (
        <>
          <ul className={divider}>
            {incidents.data.items.map((i) => (
              <li key={i.id} className="flex items-start gap-3 px-5 py-3.5 text-sm">
                <IncidentStatusBadge status={i.status} />
                <div className="min-w-0">
                  <Link to={`/incidents/${i.id}`} className={quietLink}>
                    <LocalTime iso={i.firstFailureAtUtc} />
                  </Link>
                  <p className={`text-xs ${mutedText}`}>
                    {describeOutcome(i.openingOutcome, i.openingStatusCode)} ·{' '}
                    {formatDuration(i.durationSeconds)}
                    {i.status === 'Open' ? ' so far' : ''}
                  </p>
                </div>
              </li>
            ))}
          </ul>
          <div className="border-t border-zinc-200 px-5 py-3 empty:hidden dark:border-zinc-800">
            <Pagination
              label="Incident pages"
              page={page}
              pageSize={INCIDENTS_PAGE_SIZE}
              totalCount={incidents.data.totalCount}
              onPageChange={setPage}
            />
          </div>
        </>
      )}
    </Panel>
  )
}
