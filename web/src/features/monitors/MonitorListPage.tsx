import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { listMonitors } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import { MONITOR_STATES, type Monitor, type MonitorState } from '../../api/types.ts'
import { ButtonLink } from '../../components/Button.tsx'
import {
  EmptyState,
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
  Pagination,
} from '../../components/States.tsx'
import { describeOutcome, displayUrl, formatInterval, formatMs } from '../../lib/format.ts'
import { StaleBadge, StatusBadge } from './StatusBadge.tsx'

const PAGE_SIZE = 25

function parseState(value: string | null): MonitorState | undefined {
  return MONITOR_STATES.find((s) => s === value)
}

export function MonitorListPage() {
  const [params, setParams] = useSearchParams()
  const state = parseState(params.get('state'))
  const page = Math.max(1, Number(params.get('page')) || 1)
  const listParams = { page, pageSize: PAGE_SIZE, state }

  const monitors = useQuery({
    queryKey: queryKeys.monitorList(listParams),
    queryFn: ({ signal }) => listMonitors(listParams, signal),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  })

  function update(next: { state?: MonitorState | undefined; page?: number }) {
    const search = new URLSearchParams()
    const nextState = 'state' in next ? next.state : state
    if (nextState) search.set('state', nextState)
    if (next.page && next.page > 1) search.set('page', String(next.page))
    setParams(search)
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="Monitors"
        actions={
          <ButtonLink to="/monitors/new" variant="primary">
            Add monitor
          </ButtonLink>
        }
      >
        {monitors.data && (
          <LastUpdated updatedAt={monitors.dataUpdatedAt} refreshFailed={monitors.isError} />
        )}
      </PageHeader>

      <div className="flex items-center gap-2">
        <label htmlFor="state-filter" className="text-sm font-medium">
          State
        </label>
        <select
          id="state-filter"
          value={state ?? ''}
          onChange={(e) => update({ state: parseState(e.target.value), page: 1 })}
          className="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm dark:border-slate-600 dark:bg-slate-900"
        >
          <option value="">All</option>
          {MONITOR_STATES.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </div>

      {monitors.isPending ? (
        <LoadingBlock label="Loading monitors" />
      ) : monitors.isError && !monitors.data ? (
        <ErrorState
          title="Couldn't load monitors"
          error={monitors.error}
          onRetry={() => void monitors.refetch()}
        />
      ) : monitors.data.items.length === 0 ? (
        state ? (
          <EmptyState title={`No ${state.toLowerCase()} monitors`}>
            <button
              type="button"
              className="font-medium text-sky-700 underline dark:text-sky-400"
              onClick={() => update({ state: undefined })}
            >
              Show all monitors
            </button>
          </EmptyState>
        ) : (
          <EmptyState
            title="No monitors yet"
            action={
              <ButtonLink to="/monitors/new" variant="primary">
                Add your first monitor
              </ButtonLink>
            }
          >
            Add an HTTP endpoint and PulseWatch will check it on a schedule.
          </EmptyState>
        )
      ) : (
        <>
          <MonitorCards monitors={monitors.data.items} />
          <MonitorTable monitors={monitors.data.items} />
          <Pagination
            label="Monitor pages"
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={monitors.data.totalCount}
            onPageChange={(p) => update({ page: p })}
          />
        </>
      )}
    </div>
  )
}

function LastResult({ monitor }: { monitor: Monitor }) {
  if (!monitor.lastCheckedAtUtc || !monitor.lastOutcome) {
    return <span className="text-slate-500 dark:text-slate-400">Not checked yet</span>
  }
  return (
    <span>
      {describeOutcome(monitor.lastOutcome, monitor.lastStatusCode)}
      {monitor.lastOutcome === 'Success' && ` · ${formatMs(monitor.lastDurationMs)}`}
      <span className="block text-xs text-slate-500 dark:text-slate-400">
        <LocalTime iso={monitor.lastCheckedAtUtc} relative />
      </span>
    </span>
  )
}

function Badges({ monitor }: { monitor: Monitor }) {
  return (
    <span className="flex flex-wrap gap-1">
      <StatusBadge state={monitor.state} />
      {monitor.isStale && <StaleBadge />}
    </span>
  )
}

/** Phones: one card per monitor. */
function MonitorCards({ monitors }: { monitors: Monitor[] }) {
  return (
    <ul className="space-y-3 md:hidden">
      {monitors.map((m) => (
        <li
          key={m.id}
          className="rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900"
        >
          <div className="flex items-start justify-between gap-2">
            <Link
              to={`/monitors/${m.id}`}
              className="font-medium underline-offset-2 hover:underline"
            >
              {m.name}
            </Link>
            <Badges monitor={m} />
          </div>
          <p className="mt-1 truncate text-xs text-slate-600 dark:text-slate-400">
            {displayUrl(m.url)}
          </p>
          <div className="mt-2 text-sm">
            <LastResult monitor={m} />
          </div>
        </li>
      ))}
    </ul>
  )
}

/** Wider screens: a table. */
function MonitorTable({ monitors }: { monitors: Monitor[] }) {
  return (
    <div className="hidden overflow-x-auto rounded-lg border border-slate-200 bg-white md:block dark:border-slate-800 dark:bg-slate-900">
      <table className="w-full text-left text-sm">
        <caption className="sr-only">Your monitors</caption>
        <thead className="border-b border-slate-200 text-xs uppercase text-slate-500 dark:border-slate-800 dark:text-slate-400">
          <tr>
            <th scope="col" className="px-4 py-2">
              Name
            </th>
            <th scope="col" className="px-4 py-2">
              State
            </th>
            <th scope="col" className="px-4 py-2">
              Last result
            </th>
            <th scope="col" className="px-4 py-2">
              Schedule
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
          {monitors.map((m) => (
            <tr key={m.id}>
              <td className="max-w-72 px-4 py-3">
                <Link
                  to={`/monitors/${m.id}`}
                  className="font-medium underline-offset-2 hover:underline"
                >
                  {m.name}
                </Link>
                <span className="block truncate text-xs text-slate-600 dark:text-slate-400">
                  {displayUrl(m.url)}
                </span>
              </td>
              <td className="px-4 py-3">
                <Badges monitor={m} />
              </td>
              <td className="px-4 py-3">
                <LastResult monitor={m} />
              </td>
              <td className="px-4 py-3 text-slate-600 dark:text-slate-400">
                {formatInterval(m.intervalSeconds)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
