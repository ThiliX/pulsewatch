import { useQuery } from '@tanstack/react-query'
import {
  Activity,
  ArrowRight,
  CircleAlert,
  CircleCheck,
  CircleHelp,
  CirclePause,
  CircleX,
  Clock,
  Plus,
  Siren,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react'
import { Link } from 'react-router'
import { getSummary, listIncidents, listMonitors } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import type { DashboardSummary, Monitor } from '../../api/types.ts'
import { ButtonLink } from '../../components/Button.tsx'
import {
  EmptyState,
  ErrorState,
  LastUpdated,
  LoadingBlock,
  LocalTime,
  PageHeader,
} from '../../components/States.tsx'
import { divider, linkText, mutedText, quietLink, surface } from '../../components/styles.ts'
import { Panel } from '../../components/ui.tsx'
import { describeOutcome, displayUrl, formatMs } from '../../lib/format.ts'
import { formatDuration } from '../../lib/time.ts'
import { IncidentStatusBadge, StatusBadge, StatusDot } from '../monitors/StatusBadge.tsx'

const REFRESH_MS = 30_000
const OVERVIEW_SIZE = 8

export function DashboardPage() {
  const summary = useQuery({
    queryKey: queryKeys.summary,
    queryFn: ({ signal }) => getSummary(signal),
    refetchInterval: REFRESH_MS,
  })

  return (
    <div className="space-y-8">
      <PageHeader
        title="Dashboard"
        actions={
          <ButtonLink to="/monitors/new" variant="primary">
            <Plus aria-hidden="true" />
            Add monitor
          </ButtonLink>
        }
      >
        {summary.data ? (
          <LastUpdated updatedAt={summary.dataUpdatedAt} refreshFailed={summary.isError} />
        ) : (
          'An overview of your monitors and incidents.'
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
          ) : (
            <SummaryCards summary={summary.data} />
          )}
        </>
      )}

      <div className="grid gap-6 lg:grid-cols-5">
        {summary.data && summary.data.totalMonitors > 0 && (
          <MonitorsOverview className="lg:col-span-3" />
        )}
        <OpenIncidents
          className={
            summary.data && summary.data.totalMonitors > 0 ? 'lg:col-span-2' : 'lg:col-span-5'
          }
        />
      </div>
    </div>
  )
}

/** Tells "your API is down" apart from "PulseWatch's own worker is down". */
export function WorkerBanner({ summary }: { summary: DashboardSummary }) {
  if (summary.workerOnline) return null
  return (
    <div
      role="alert"
      className="flex gap-3 rounded-xl border border-amber-200 bg-amber-50 p-4 text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200"
    >
      <TriangleAlert
        aria-hidden="true"
        className="mt-0.5 size-5 shrink-0 text-amber-600 dark:text-amber-400"
      />
      <div>
        <p className="text-sm font-semibold">The PulseWatch worker is offline</p>
        <p className="mt-1 text-sm opacity-90">
          {summary.workerLastSeenAtUtc ? (
            <>
              It was last seen <LocalTime iso={summary.workerLastSeenAtUtc} relative />.
            </>
          ) : (
            <>No worker has reported in yet.</>
          )}{' '}
          No checks run and no alerts are sent until it is back. The states below may be out of
          date.
        </p>
      </div>
    </div>
  )
}

type Card = {
  label: string
  value: number
  icon: LucideIcon
  /** Icon chip colours. */
  chip: string
  /** Value colour when it deserves attention. */
  tone?: string | undefined
  to?: string
}

const CHIP = {
  indigo: 'bg-indigo-50 text-indigo-600 dark:bg-indigo-500/10 dark:text-indigo-400',
  green: 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400',
  amber: 'bg-amber-50 text-amber-600 dark:bg-amber-500/10 dark:text-amber-400',
  red: 'bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-400',
  grey: 'bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400',
}

function SummaryCards({ summary }: { summary: DashboardSummary }) {
  const cards: Card[] = [
    {
      label: 'Monitors',
      value: summary.totalMonitors,
      icon: Activity,
      chip: CHIP.indigo,
      to: '/monitors',
    },
    {
      label: 'Healthy',
      value: summary.healthy,
      icon: CircleCheck,
      chip: CHIP.green,
      to: '/monitors?state=Healthy',
    },
    {
      label: 'Degraded',
      value: summary.degraded,
      icon: CircleAlert,
      chip: CHIP.amber,
      tone: summary.degraded > 0 ? 'text-amber-600 dark:text-amber-400' : undefined,
      to: '/monitors?state=Degraded',
    },
    {
      label: 'Down',
      value: summary.down,
      icon: CircleX,
      chip: CHIP.red,
      tone: summary.down > 0 ? 'text-rose-600 dark:text-rose-400' : undefined,
      to: '/monitors?state=Down',
    },
    {
      label: 'Unknown',
      value: summary.unknown,
      icon: CircleHelp,
      chip: CHIP.grey,
      to: '/monitors?state=Unknown',
    },
    {
      label: 'Paused',
      value: summary.paused,
      icon: CirclePause,
      chip: CHIP.grey,
      to: '/monitors?state=Paused',
    },
    {
      label: 'Stale',
      value: summary.stale,
      icon: Clock,
      chip: summary.stale > 0 ? CHIP.amber : CHIP.grey,
      tone: summary.stale > 0 ? 'text-amber-600 dark:text-amber-400' : undefined,
    },
    {
      label: 'Open incidents',
      value: summary.openIncidents,
      icon: Siren,
      chip: summary.openIncidents > 0 ? CHIP.red : CHIP.grey,
      tone: summary.openIncidents > 0 ? 'text-rose-600 dark:text-rose-400' : undefined,
      to: '/incidents?status=open',
    },
  ]

  return (
    <section aria-label="Summary">
      <ul className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {cards.map((card) => {
          const Icon = card.icon
          const box = `${surface} relative block p-5`
          // The label and value share one parent so the card reads as "Healthy 2".
          const body = (
            <>
              <span className={`block text-sm font-medium ${mutedText}`}>{card.label}</span>
              <span
                className={`mt-2 block text-3xl font-semibold tracking-tight tabular-nums ${card.tone ?? 'text-zinc-900 dark:text-white'}`}
              >
                {card.value}
              </span>
              <span
                aria-hidden="true"
                className={`absolute top-4 right-4 flex size-8 items-center justify-center rounded-lg ${card.chip}`}
              >
                <Icon className="size-4" />
              </span>
            </>
          )
          return (
            <li key={card.label}>
              {card.to ? (
                <Link
                  to={card.to}
                  className={`${box} transition-colors hover:border-indigo-300 dark:hover:border-indigo-500/50`}
                >
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

function MonitorsOverview({ className = '' }: { className?: string }) {
  const params = { page: 1, pageSize: OVERVIEW_SIZE, state: undefined }
  const monitors = useQuery({
    queryKey: queryKeys.monitorList(params),
    queryFn: ({ signal }) => listMonitors(params, signal),
    refetchInterval: REFRESH_MS,
  })

  return (
    <Panel
      title="Monitors"
      titleId="overview-heading"
      description="Latest result for each monitor"
      className={className}
      bodyClassName=""
      actions={
        <Link to="/monitors" className={`inline-flex items-center gap-1 text-sm ${linkText}`}>
          View all
          <ArrowRight aria-hidden="true" className="size-3.5" />
        </Link>
      }
    >
      {monitors.isPending ? (
        <div className="p-5">
          <LoadingBlock label="Loading monitors" rows={3} />
        </div>
      ) : monitors.isError && !monitors.data ? (
        <div className="p-5">
          <ErrorState
            title="Couldn't load monitors"
            error={monitors.error}
            onRetry={() => void monitors.refetch()}
          />
        </div>
      ) : (
        <ul className={divider}>
          {monitors.data.items.map((m) => (
            <MonitorRow key={m.id} monitor={m} />
          ))}
        </ul>
      )}
    </Panel>
  )
}

function MonitorRow({ monitor: m }: { monitor: Monitor }) {
  const checked = m.lastCheckedAtUtc && m.lastOutcome
  return (
    <li className="flex items-center gap-3.5 px-5 py-3.5">
      <StatusDot state={m.state} />
      <div className="min-w-0 flex-1">
        <Link to={`/monitors/${m.id}`} className={`block truncate text-sm ${quietLink}`}>
          {m.name}
        </Link>
        <p className={`truncate text-xs ${mutedText}`}>{displayUrl(m.url)}</p>
      </div>
      <div className="hidden text-right sm:block">
        <p className="text-sm font-medium text-zinc-900 tabular-nums dark:text-zinc-100">
          {checked && m.lastOutcome === 'Success'
            ? formatMs(m.lastDurationMs)
            : checked
              ? describeOutcome(m.lastOutcome!, m.lastStatusCode)
              : '—'}
        </p>
        <p className={`text-xs ${mutedText}`}>
          {m.lastCheckedAtUtc ? <LocalTime iso={m.lastCheckedAtUtc} relative /> : 'Not checked yet'}
        </p>
      </div>
      <div className="w-24 text-right">
        <StatusBadge state={m.state} />
      </div>
    </li>
  )
}

function OpenIncidents({ className = '' }: { className?: string }) {
  const params = { page: 1, pageSize: 5, status: 'open' as const }
  const incidents = useQuery({
    queryKey: queryKeys.incidentList(params),
    queryFn: ({ signal }) => listIncidents(params, signal),
    refetchInterval: REFRESH_MS,
  })

  return (
    <Panel
      title="Open incidents"
      titleId="open-incidents-heading"
      description="Monitors that are down right now"
      className={className}
      bodyClassName=""
      actions={
        <Link to="/incidents" className={`inline-flex items-center gap-1 text-sm ${linkText}`}>
          All incidents
          <ArrowRight aria-hidden="true" className="size-3.5" />
        </Link>
      }
    >
      {incidents.isPending ? (
        <div className="p-5">
          <LoadingBlock label="Loading open incidents" rows={2} />
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
        <div className="flex flex-col items-center px-5 py-10 text-center">
          <span className="mb-3 flex size-10 items-center justify-center rounded-full bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400">
            <CircleCheck aria-hidden="true" className="size-5" />
          </span>
          <p className="text-sm font-medium text-zinc-900 dark:text-zinc-100">No open incidents.</p>
          <p className={`mt-0.5 text-xs ${mutedText}`}>All systems are running normally.</p>
        </div>
      ) : (
        <ul className={divider}>
          {incidents.data.items.map((incident) => (
            <li key={incident.id} className="flex items-start gap-3 px-5 py-3.5">
              <IncidentStatusBadge status={incident.status} />
              <div className="min-w-0 flex-1">
                <Link
                  to={`/incidents/${incident.id}`}
                  className={`block truncate text-sm ${quietLink}`}
                >
                  {incident.monitorName}
                </Link>
                <p className={`text-xs ${mutedText}`}>
                  {describeOutcome(incident.openingOutcome, incident.openingStatusCode)} · down for{' '}
                  {formatDuration(incident.durationSeconds)}
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}
