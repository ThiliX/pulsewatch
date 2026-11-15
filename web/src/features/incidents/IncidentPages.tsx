import { keepPreviousData, useQuery } from '@tanstack/react-query'
import {
  ArrowUpRight,
  BellRing,
  CircleCheck,
  CircleX,
  Mail,
  Siren,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { isApiError } from '../../api/client.ts'
import { getIncident, listIncidents } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import type { Incident, IncidentNotification } from '../../api/types.ts'
import { ButtonLink, SegmentedControl } from '../../components/Button.tsx'
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
import { Panel } from '../../components/ui.tsx'
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
        {incidents.data ? (
          <LastUpdated updatedAt={incidents.dataUpdatedAt} refreshFailed={incidents.isError} />
        ) : (
          'Every outage PulseWatch detected, with its timeline and alerts.'
        )}
      </PageHeader>

      <SegmentedControl
        label="Filter by status"
        options={FILTERS}
        value={status}
        onChange={(value) => update({ status: value, page: 1 })}
      />

      {incidents.isPending ? (
        <LoadingBlock label="Loading incidents" />
      ) : incidents.isError && !incidents.data ? (
        <ErrorState
          title="Couldn't load incidents"
          error={incidents.error}
          onRetry={() => void incidents.refetch()}
        />
      ) : incidents.data.items.length === 0 ? (
        <EmptyState
          title={status === 'open' ? 'No open incidents' : 'No incidents'}
          icon={status === 'open' ? CircleCheck : Siren}
        >
          An incident opens when a monitor fails its failure threshold of checks in a row.
        </EmptyState>
      ) : (
        <>
          <ul className={`${surface} ${divider} overflow-hidden`}>
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
  const open = incident.status === 'Open'
  return (
    <li className="flex flex-col gap-2 px-5 py-4 transition-colors hover:bg-zinc-50 sm:flex-row sm:items-center sm:gap-4 dark:hover:bg-zinc-800/40">
      <span
        aria-hidden="true"
        className={`hidden size-9 shrink-0 items-center justify-center rounded-lg sm:flex ${
          open
            ? 'bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-400'
            : 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400'
        }`}
      >
        {open ? <TriangleAlert className="size-4" /> : <CircleCheck className="size-4" />}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <Link to={`/incidents/${incident.id}`} className={`text-sm ${quietLink}`}>
            {incident.monitorName}
          </Link>
          <IncidentStatusBadge status={incident.status} />
        </div>
        <p className={`mt-0.5 text-sm ${mutedText}`}>
          {describeOutcome(incident.openingOutcome, incident.openingStatusCode)} · started{' '}
          <LocalTime iso={incident.firstFailureAtUtc} />
        </p>
      </div>
      <p className="text-sm font-medium text-zinc-900 tabular-nums sm:text-right dark:text-zinc-100">
        {formatDuration(incident.durationSeconds)}
        {open ? ' so far' : ''}
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
  const open = incident.status === 'Open'
  return (
    <div className="space-y-6">
      <PageHeader
        title={`Incident: ${incident.monitorName}`}
        back={{ to: '/incidents', label: 'Incidents' }}
      >
        <div className="flex flex-wrap items-center gap-3">
          <IncidentStatusBadge status={incident.status} />
          <Link
            to={`/monitors/${incident.monitorId}`}
            className={`inline-flex items-center gap-1 ${linkText}`}
          >
            View monitor
            <ArrowUpRight aria-hidden="true" className="size-3.5" />
          </Link>
        </div>
        <div className="mt-2">
          <LastUpdated updatedAt={detail.dataUpdatedAt} refreshFailed={detail.isError} />
        </div>
      </PageHeader>

      <dl className="grid gap-4 sm:grid-cols-3">
        <Fact label="What failed" icon={CircleX} tone="red">
          {describeOutcome(incident.openingOutcome, incident.openingStatusCode)}
        </Fact>
        <Fact label={open ? 'Down for' : 'Outage length'} icon={Siren} tone={open ? 'red' : 'grey'}>
          {formatDuration(incident.durationSeconds)}
          {open ? ' so far' : ''}
        </Fact>
        <Fact label="Failed checks" icon={BellRing} tone="grey">
          {incident.failureCount}
        </Fact>
      </dl>

      <div className="grid gap-6 lg:grid-cols-2">
        <IncidentTimeline incident={incident} />
        <Deliveries notifications={notifications} />
      </div>
    </div>
  )
}

const FACT_TONES = {
  red: 'bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-400',
  grey: 'bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400',
}

function Fact({
  label,
  icon: Icon,
  tone,
  children,
}: {
  label: string
  icon: LucideIcon
  tone: keyof typeof FACT_TONES
  children: ReactNode
}) {
  return (
    <div className={`${surface} flex items-start gap-3 p-5`}>
      <span
        aria-hidden="true"
        className={`flex size-9 shrink-0 items-center justify-center rounded-lg ${FACT_TONES[tone]}`}
      >
        <Icon className="size-4" />
      </span>
      <div>
        <dt className={`text-sm ${mutedText}`}>{label}</dt>
        <dd className="mt-0.5 text-base font-semibold text-zinc-900 dark:text-white">{children}</dd>
      </div>
    </div>
  )
}

type TimelineEvent = { at: string; title: string; detail?: string; tone: 'red' | 'amber' | 'green' }

const DOT = {
  red: 'bg-rose-500 ring-rose-100 dark:ring-rose-500/20',
  amber: 'bg-amber-500 ring-amber-100 dark:ring-amber-500/20',
  green: 'bg-emerald-500 ring-emerald-100 dark:ring-emerald-500/20',
}

function IncidentTimeline({ incident }: { incident: Incident }) {
  const events: TimelineEvent[] = [
    {
      at: incident.firstFailureAtUtc,
      title: 'First failed check',
      detail: describeOutcome(incident.openingOutcome, incident.openingStatusCode),
      tone: 'amber',
    },
    {
      at: incident.openedAtUtc,
      title: 'Incident opened',
      detail: 'The failure threshold was reached; an alert email was queued.',
      tone: 'red',
    },
  ]
  if (incident.lastFailureAtUtc && incident.lastFailureAtUtc !== incident.openedAtUtc) {
    events.push({ at: incident.lastFailureAtUtc, title: 'Latest failed check', tone: 'red' })
  }
  if (incident.resolvedAtUtc) {
    events.push({
      at: incident.resolvedAtUtc,
      title: 'Recovered',
      detail: 'The recovery threshold was reached; a recovery email was queued.',
      tone: 'green',
    })
  }
  events.sort((a, b) => Date.parse(a.at) - Date.parse(b.at))

  return (
    <Panel title="Timeline" titleId="timeline-heading" className="self-start">
      <ol className="relative space-y-6 border-l border-zinc-200 pl-6 dark:border-zinc-800">
        {events.map((event) => (
          <li key={`${event.title}-${event.at}`} className="relative">
            <span
              aria-hidden="true"
              className={`absolute top-1 -left-[29px] size-2.5 rounded-full ring-4 ${DOT[event.tone]}`}
            />
            <p className="text-sm font-medium text-zinc-900 dark:text-zinc-100">{event.title}</p>
            <p className={`mt-0.5 text-sm ${mutedText}`}>
              <LocalTime iso={event.at} />
              {event.detail && ` · ${event.detail}`}
            </p>
          </li>
        ))}
        {incident.status === 'Open' && (
          <li className={`relative text-sm ${mutedText}`}>
            <span
              aria-hidden="true"
              className="absolute top-1 -left-[29px] size-2.5 animate-pulse rounded-full bg-zinc-300 ring-4 ring-zinc-100 dark:bg-zinc-600 dark:ring-zinc-800"
            />
            Still open: waiting for successful checks.
          </li>
        )}
      </ol>
    </Panel>
  )
}

const EVENT_LABELS = {
  IncidentOpened: '"Down" email',
  IncidentResolved: '"Recovered" email',
} as const

function Deliveries({ notifications }: { notifications: IncidentNotification[] }) {
  return (
    <Panel
      title="Email alerts"
      titleId="deliveries-heading"
      className="self-start"
      bodyClassName=""
    >
      {notifications.length === 0 ? (
        <p className={`p-8 text-center text-sm ${mutedText}`}>No emails for this incident.</p>
      ) : (
        <ul className={divider}>
          {notifications.map((n) => (
            <li key={`${n.eventType}-${n.recipient}`} className="flex gap-3 px-5 py-4 text-sm">
              <Mail aria-hidden="true" className={`mt-0.5 size-4 shrink-0 ${mutedText}`} />
              <div className="min-w-0 flex-1 space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium text-zinc-900 dark:text-zinc-100">
                    {EVENT_LABELS[n.eventType]}
                  </span>
                  <NotificationStatusBadge status={n.status} />
                </div>
                <p className={`truncate ${mutedText}`}>to {n.recipient}</p>
                <p className={`text-xs ${mutedText}`}>
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
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}
