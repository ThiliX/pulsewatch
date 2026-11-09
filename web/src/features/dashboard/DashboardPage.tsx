import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { getSummary, listIncidents } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import type { DashboardSummary } from '../../api/types.ts'
import { ButtonLink } from '../../components/Button.tsx'
import {
  EmptyState,
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
} from '../../components/States.tsx'
import { describeOutcome } from '../../lib/format.ts'
import { formatDuration } from '../../lib/time.ts'
import { IncidentStatusBadge } from '../monitors/StatusBadge.tsx'

const REFRESH_MS = 30_000

export function DashboardPage() {
  const summary = useQuery({
    queryKey: queryKeys.summary,
    queryFn: ({ signal }) => getSummary(signal),
    refetchInterval: REFRESH_MS,
  })

  return (
    <div className="space-y-6">
      <PageHeader
        title="Dashboard"
        actions={
          <ButtonLink to="/monitors/new" variant="primary">
            Add monitor
          </ButtonLink>
        }
      >
        {summary.data && (
          <LastUpdated updatedAt={summary.dataUpdatedAt} refreshFailed={summary.isError} />
        )}
      </PageHeader>

      {summary.isPending ? (
        <LoadingBlock label="Loading the summary" rows={2} />
      ) : summary.isError && !summary.data ? (
        <ErrorState
          title="Couldn't load the dashboard"
          error={summary.error}
          onRetry={() => void summary.refetch()}
        />
      ) : (
        <>
          <WorkerBanner summary={summary.data} />
          {summary.data.totalMonitors === 0 ? (
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
          ) : (
            <SummaryCards summary={summary.data} />
          )}
        </>
      )}

      <OpenIncidents />
    </div>
  )
}

/** Tells "your API is down" apart from "PulseWatch's own worker is down". */
export function WorkerBanner({ summary }: { summary: DashboardSummary }) {
  if (summary.workerOnline) return null
  return (
    <div
      role="alert"
      className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-900 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-100"
    >
      <p className="font-semibold">
        <span aria-hidden="true">⚠ </span>
        The PulseWatch worker is offline
      </p>
      <p className="mt-1 text-sm">
        {summary.workerLastSeenAtUtc ? (
          <>
            It was last seen <LocalTime iso={summary.workerLastSeenAtUtc} relative />.
          </>
        ) : (
          <>No worker has reported in yet.</>
        )}{' '}
        No checks run and no alerts are sent until it is back. The states below may be out of date.
      </p>
    </div>
  )
}

type Card = { label: string; value: number; icon: string; tone: string; to?: string }

function SummaryCards({ summary }: { summary: DashboardSummary }) {
  const cards: Card[] = [
    { label: 'Monitors', value: summary.totalMonitors, icon: '◎', tone: '', to: '/monitors' },
    {
      label: 'Healthy',
      value: summary.healthy,
      icon: '✓',
      tone: 'text-emerald-800 dark:text-emerald-300',
      to: '/monitors?state=Healthy',
    },
    {
      label: 'Degraded',
      value: summary.degraded,
      icon: '!',
      tone: 'text-amber-800 dark:text-amber-300',
      to: '/monitors?state=Degraded',
    },
    {
      label: 'Down',
      value: summary.down,
      icon: '✕',
      tone: 'text-rose-800 dark:text-rose-300',
      to: '/monitors?state=Down',
    },
    {
      label: 'Unknown',
      value: summary.unknown,
      icon: '?',
      tone: '',
      to: '/monitors?state=Unknown',
    },
    { label: 'Paused', value: summary.paused, icon: '‖', tone: '', to: '/monitors?state=Paused' },
    {
      label: 'Stale',
      value: summary.stale,
      icon: '⏱',
      tone: summary.stale > 0 ? 'text-amber-800 dark:text-amber-300' : '',
    },
    {
      label: 'Open incidents',
      value: summary.openIncidents,
      icon: '✕',
      tone: summary.openIncidents > 0 ? 'text-rose-800 dark:text-rose-300' : '',
      to: '/incidents?status=open',
    },
  ]

  return (
    <section aria-label="Summary">
      <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {cards.map((card) => {
          const body = (
            <>
              <span className="text-sm text-slate-600 dark:text-slate-400">
                <span aria-hidden="true">{card.icon} </span>
                {card.label}
              </span>
              <span className={`block text-2xl font-semibold ${card.tone}`}>{card.value}</span>
            </>
          )
          const box =
            'block rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900'
          return (
            <li key={card.label}>
              {card.to ? (
                <Link to={card.to} className={`${box} hover:border-sky-400`}>
                  {body}
                </Link>
              ) : (
                <div className={box}>{body}</div>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

function OpenIncidents() {
  const params = { page: 1, pageSize: 5, status: 'open' as const }
  const incidents = useQuery({
    queryKey: queryKeys.incidentList(params),
    queryFn: ({ signal }) => listIncidents(params, signal),
    refetchInterval: REFRESH_MS,
  })

  return (
    <section aria-labelledby="open-incidents-heading" className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 id="open-incidents-heading" className="text-lg font-semibold">
          Open incidents
        </h2>
        <Link
          to="/incidents"
          className="text-sm font-medium text-sky-700 underline dark:text-sky-400"
        >
          All incidents
        </Link>
      </div>
      {incidents.isPending ? (
        <LoadingBlock label="Loading open incidents" rows={2} />
      ) : incidents.isError && !incidents.data ? (
        <ErrorState
          title="Couldn't load incidents"
          error={incidents.error}
          onRetry={() => void incidents.refetch()}
        />
      ) : incidents.data.items.length === 0 ? (
        <p className="rounded-lg border border-slate-200 p-4 text-sm text-slate-600 dark:border-slate-800 dark:text-slate-400">
          <span aria-hidden="true">✓ </span>
          No open incidents.
        </p>
      ) : (
        <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
          {incidents.data.items.map((incident) => (
            <li key={incident.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 p-3">
              <IncidentStatusBadge status={incident.status} />
              <Link
                to={`/incidents/${incident.id}`}
                className="font-medium underline-offset-2 hover:underline"
              >
                {incident.monitorName}
              </Link>
              <span className="text-sm text-slate-600 dark:text-slate-400">
                {describeOutcome(incident.openingOutcome, incident.openingStatusCode)} · down for{' '}
                {formatDuration(incident.durationSeconds)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
