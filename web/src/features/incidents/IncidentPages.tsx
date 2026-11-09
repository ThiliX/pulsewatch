import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'
import { isApiError } from '../../api/client.ts'
import { getIncident, listIncidents } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import type { Incident, IncidentNotification } from '../../api/types.ts'
import { Button, ButtonLink } from '../../components/Button.tsx'
import {
  EmptyState,
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
  Pagination,
} from '../../components/States.tsx'
import { describeOutcome } from '../../lib/format.ts'
import { formatDuration } from '../../lib/time.ts'
import { IncidentStatusBadge, NotificationStatusBadge } from '../monitors/StatusBadge.tsx'

const PAGE_SIZE = 25
const REFRESH_MS = 30_000
type StatusFilter = 'open' | 'resolved' | undefined

const FILTERS: { value: StatusFilter; label: string }[] = [
  { value: undefined, label: 'All' },
  { value: 'open', label: 'Open' },
  { value: 'resolved', label: 'Resolved' },
]

export function IncidentListPage() {
  const [params, setParams] = useSearchParams()
  const raw = params.get('status')
  const status: StatusFilter = raw === 'open' || raw === 'resolved' ? raw : undefined
  const page = Math.max(1, Number(params.get('page')) || 1)
  const listParams = { page, pageSize: PAGE_SIZE, status }

  const incidents = useQuery({
    queryKey: queryKeys.incidentList(listParams),
    queryFn: ({ signal }) => listIncidents(listParams, signal),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
  })

  function update(next: { status?: StatusFilter; page?: number }) {
    const search = new URLSearchParams()
    const nextStatus = 'status' in next ? next.status : status
    if (nextStatus) search.set('status', nextStatus)
    if (next.page && next.page > 1) search.set('page', String(next.page))
    setParams(search)
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Incidents">
        {incidents.data && (
          <LastUpdated updatedAt={incidents.dataUpdatedAt} refreshFailed={incidents.isError} />
        )}
      </PageHeader>

      <div role="group" aria-label="Filter by status" className="flex gap-1">
        {FILTERS.map((f) => (
          <Button
            key={f.label}
            variant={f.value === status ? 'primary' : 'secondary'}
            aria-pressed={f.value === status}
            onClick={() => update({ status: f.value, page: 1 })}
          >
            {f.label}
          </Button>
        ))}
      </div>

      {incidents.isPending ? (
        <LoadingBlock label="Loading incidents" />
      ) : incidents.isError && !incidents.data ? (
        <ErrorState
          title="Couldn't load incidents"
          error={incidents.error}
          onRetry={() => void incidents.refetch()}
        />
      ) : incidents.data.items.length === 0 ? (
        <EmptyState title={status === 'open' ? 'No open incidents' : 'No incidents'}>
          An incident opens when a monitor fails its failure threshold of checks in a row.
        </EmptyState>
      ) : (
        <>
          <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
            {incidents.data.items.map((incident) => (
              <IncidentRow key={incident.id} incident={incident} />
            ))}
          </ul>
          <Pagination
            label="Incident pages"
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={incidents.data.totalCount}
            onPageChange={(p) => update({ page: p })}
          />
        </>
      )}
    </div>
  )
}

function IncidentRow({ incident }: { incident: Incident }) {
  return (
    <li className="flex flex-col gap-1 p-4 sm:flex-row sm:items-center sm:gap-4">
      <IncidentStatusBadge status={incident.status} />
      <div className="min-w-0 flex-1">
        <Link
          to={`/incidents/${incident.id}`}
          className="font-medium underline-offset-2 hover:underline"
        >
          {incident.monitorName}
        </Link>
        <p className="text-sm text-slate-600 dark:text-slate-400">
          {describeOutcome(incident.openingOutcome, incident.openingStatusCode)} · started{' '}
          <LocalTime iso={incident.firstFailureAtUtc} />
        </p>
      </div>
      <p className="text-sm">
        {formatDuration(incident.durationSeconds)}
        {incident.status === 'Open' ? ' so far' : ''}
      </p>
    </li>
  )
}

export function IncidentDetailPage() {
  const { id = '' } = useParams()
  const detail = useQuery({
    queryKey: queryKeys.incident(id),
    queryFn: ({ signal }) => getIncident(id, signal),
    refetchInterval: REFRESH_MS,
  })

  if (detail.isPending) return <LoadingBlock label="Loading the incident" rows={4} />
  if (detail.isError && !detail.data) {
    return isApiError(detail.error, 404) ? (
      <div className="space-y-4">
        <PageHeader title="Incident not found">
          It may have been deleted with its monitor.
        </PageHeader>
        <ButtonLink to="/incidents">Back to incidents</ButtonLink>
      </div>
    ) : (
      <ErrorState
        title="Couldn't load the incident"
        error={detail.error}
        onRetry={() => void detail.refetch()}
      />
    )
  }

  const { incident, notifications } = detail.data
  return (
    <div className="space-y-8">
      <PageHeader title={`Incident: ${incident.monitorName}`}>
        <div className="flex flex-wrap items-center gap-2">
          <IncidentStatusBadge status={incident.status} />
          <Link
            to={`/monitors/${incident.monitorId}`}
            className="font-medium text-sky-700 underline dark:text-sky-400"
          >
            View monitor
          </Link>
        </div>
        <div className="mt-1">
          <LastUpdated updatedAt={detail.dataUpdatedAt} refreshFailed={detail.isError} />
        </div>
      </PageHeader>

      <dl className="grid gap-x-6 gap-y-3 rounded-lg border border-slate-200 bg-white p-4 text-sm sm:grid-cols-3 dark:border-slate-800 dark:bg-slate-900">
        <div>
          <dt className="text-slate-600 dark:text-slate-400">What failed</dt>
          <dd className="font-medium">
            {describeOutcome(incident.openingOutcome, incident.openingStatusCode)}
          </dd>
        </div>
        <div>
          <dt className="text-slate-600 dark:text-slate-400">
            {incident.status === 'Open' ? 'Down for' : 'Outage length'}
          </dt>
          <dd className="font-medium">
            {formatDuration(incident.durationSeconds)}
            {incident.status === 'Open' ? ' so far' : ''}
          </dd>
        </div>
        <div>
          <dt className="text-slate-600 dark:text-slate-400">Failed checks</dt>
          <dd className="font-medium">{incident.failureCount}</dd>
        </div>
      </dl>

      <IncidentTimeline incident={incident} />
      <Deliveries notifications={notifications} />
    </div>
  )
}

function IncidentTimeline({ incident }: { incident: Incident }) {
  const events: { at: string; title: string; detail?: string }[] = [
    {
      at: incident.firstFailureAtUtc,
      title: 'First failed check',
      detail: describeOutcome(incident.openingOutcome, incident.openingStatusCode),
    },
    {
      at: incident.openedAtUtc,
      title: 'Incident opened',
      detail: 'The failure threshold was reached; an alert email was queued.',
    },
  ]
  if (incident.lastFailureAtUtc && incident.lastFailureAtUtc !== incident.openedAtUtc) {
    events.push({ at: incident.lastFailureAtUtc, title: 'Latest failed check' })
  }
  if (incident.resolvedAtUtc) {
    events.push({
      at: incident.resolvedAtUtc,
      title: 'Recovered',
      detail: 'The recovery threshold was reached; a recovery email was queued.',
    })
  }
  events.sort((a, b) => Date.parse(a.at) - Date.parse(b.at))

  return (
    <section aria-labelledby="timeline-heading" className="space-y-3">
      <h2 id="timeline-heading" className="text-lg font-semibold">
        Timeline
      </h2>
      <ol className="space-y-4 border-l-2 border-slate-200 pl-5 dark:border-slate-700">
        {events.map((event) => (
          <li key={`${event.title}-${event.at}`} className="relative">
            <span
              aria-hidden="true"
              className="absolute -left-[27px] top-1.5 h-3 w-3 rounded-full border-2 border-white bg-slate-400 dark:border-slate-950"
            />
            <p className="font-medium">{event.title}</p>
            <p className="text-sm text-slate-600 dark:text-slate-400">
              <LocalTime iso={event.at} />
              {event.detail && ` · ${event.detail}`}
            </p>
          </li>
        ))}
        {incident.status === 'Open' && (
          <li className="text-sm text-slate-600 dark:text-slate-400">
            Still open: waiting for successful checks.
          </li>
        )}
      </ol>
    </section>
  )
}

const EVENT_LABELS = {
  IncidentOpened: '"Down" email',
  IncidentResolved: '"Recovered" email',
} as const

function Deliveries({ notifications }: { notifications: IncidentNotification[] }) {
  return (
    <section aria-labelledby="deliveries-heading" className="space-y-3">
      <h2 id="deliveries-heading" className="text-lg font-semibold">
        Email alerts
      </h2>
      {notifications.length === 0 ? (
        <p className="text-sm text-slate-600 dark:text-slate-400">No emails for this incident.</p>
      ) : (
        <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
          {notifications.map((n) => (
            <li
              key={`${n.eventType}-${n.recipient}`}
              className="flex flex-col gap-1 p-4 text-sm sm:flex-row sm:items-center sm:gap-4"
            >
              <span className="font-medium sm:w-40">{EVENT_LABELS[n.eventType]}</span>
              <NotificationStatusBadge status={n.status} />
              <span className="min-w-0 flex-1 truncate text-slate-600 dark:text-slate-400">
                to {n.recipient}
              </span>
              <span className="text-slate-600 dark:text-slate-400">
                {n.attemptCount} {n.attemptCount === 1 ? 'attempt' : 'attempts'}
                {n.sentAtUtc && (
                  <>
                    {' · sent '}
                    <LocalTime iso={n.sentAtUtc} relative />
                  </>
                )}
                {n.lastErrorCategory &&
                  n.status !== 'Sent' &&
                  ` · last error: ${n.lastErrorCategory}`}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
