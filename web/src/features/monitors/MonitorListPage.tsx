import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Activity, Filter, Plus } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { listMonitors } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import { MONITOR_STATES, type Monitor, type MonitorState } from '../../api/types.ts'
import { ButtonLink } from '../../components/Button.tsx'
import { Select } from '../../components/Field.tsx'
import {
  EmptyState,
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
  Pagination,
} from '../../components/States.tsx'
import { divider, linkText, mutedText, quietLink, surface } from '../../components/styles.ts'
import { Th } from '../../components/ui.tsx'
import { describeOutcome, displayUrl, formatInterval, formatMs } from '../../lib/format.ts'
import { StaleBadge, StatusBadge, StatusDot } from './StatusBadge.tsx'

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
            <Plus aria-hidden="true" />
            Add monitor
          </ButtonLink>
        }
      >
        {monitors.data ? (
          <LastUpdated updatedAt={monitors.dataUpdatedAt} refreshFailed={monitors.isError} />
        ) : (
          'Every endpoint PulseWatch checks for you.'
        )}
      </PageHeader>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Filter aria-hidden="true" className={`size-4 ${mutedText}`} />
          <label
            htmlFor="state-filter"
            className="text-sm font-medium text-zinc-700 dark:text-zinc-300"
          >
            State
          </label>
          <Select
            id="state-filter"
            value={state ?? ''}
            onChange={(e) => update({ state: parseState(e.target.value), page: 1 })}
          >
            <option value="">All</option>
            {MONITOR_STATES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
        </div>
        {monitors.data && (
          <p className={`text-sm ${mutedText}`}>
            {monitors.data.totalCount} {monitors.data.totalCount === 1 ? 'monitor' : 'monitors'}
          </p>
        )}
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
            <button type="button" className={linkText} onClick={() => update({ state: undefined })}>
              Show all monitors
            </button>
          </EmptyState>
        ) : (
          <EmptyState
            title="No monitors yet"
            icon={Activity}
            action={
              <ButtonLink to="/monitors/new" variant="primary">
                <Plus aria-hidden="true" />
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
    return <span className={mutedText}>Not checked yet</span>
  }
  const ok = monitor.lastOutcome === 'Success'
  return (
    <span>
      <span
        className={`font-medium tabular-nums ${ok ? 'text-zinc-900 dark:text-zinc-100' : 'text-rose-600 dark:text-rose-400'}`}
      >
        {ok
          ? formatMs(monitor.lastDurationMs)
          : describeOutcome(monitor.lastOutcome, monitor.lastStatusCode)}
      </span>
      {ok && (
        <span className={`ml-1.5 ${mutedText}`}>
          {describeOutcome(monitor.lastOutcome, monitor.lastStatusCode)}
        </span>
      )}
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
        <li key={m.id} className={`${surface} p-4`}>
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 items-center gap-2.5">
              <StatusDot state={m.state} />
              <Link to={`/monitors/${m.id}`} className={`truncate text-sm ${quietLink}`}>
                {m.name}
              </Link>
            </div>
            <Badges monitor={m} />
          </div>
          <p className={`mt-1 truncate pl-5 text-xs ${mutedText}`}>{displayUrl(m.url)}</p>
          <div className="mt-3 flex items-center justify-between pl-5 text-sm">
            <LastResult monitor={m} />
            <span className={`text-xs ${mutedText}`}>
              {m.lastCheckedAtUtc && <LocalTime iso={m.lastCheckedAtUtc} relative />}
            </span>
          </div>
        </li>
      ))}
    </ul>
  )
}

/** Wider screens: a table. */
function MonitorTable({ monitors }: { monitors: Monitor[] }) {
  return (
    <div className={`${surface} hidden overflow-x-auto md:block`}>
      <table className="w-full text-sm">
        <caption className="sr-only">Your monitors</caption>
        <thead className="border-b border-zinc-200 bg-zinc-50/60 dark:border-zinc-800 dark:bg-zinc-900/60">
          <tr>
            <Th>Name</Th>
            <Th>State</Th>
            <Th>Last result</Th>
            <Th>Checked</Th>
            <Th>Schedule</Th>
          </tr>
        </thead>
        <tbody className={divider}>
          {monitors.map((m) => (
            <tr key={m.id} className="transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/40">
              <td className="max-w-80 px-5 py-3.5">
                <div className="flex items-center gap-3">
                  <StatusDot state={m.state} />
                  <div className="min-w-0">
                    <Link to={`/monitors/${m.id}`} className={`block truncate ${quietLink}`}>
                      {m.name}
                    </Link>
                    <span className={`block truncate text-xs ${mutedText}`}>
                      {displayUrl(m.url)}
                    </span>
                  </div>
                </div>
              </td>
              <td className="px-5 py-3.5">
                <Badges monitor={m} />
              </td>
              <td className="px-5 py-3.5 whitespace-nowrap">
                <LastResult monitor={m} />
              </td>
              <td className={`px-5 py-3.5 whitespace-nowrap ${mutedText}`}>
                {m.lastCheckedAtUtc ? <LocalTime iso={m.lastCheckedAtUtc} relative /> : '—'}
              </td>
              <td className={`px-5 py-3.5 whitespace-nowrap ${mutedText}`}>
                {formatInterval(m.intervalSeconds)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
