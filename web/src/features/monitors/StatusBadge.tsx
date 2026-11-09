import type { IncidentStatus, MonitorState, NotificationStatus } from '../../api/types.ts'
import { STATE_DESCRIPTIONS } from '../../lib/format.ts'

type Look = { icon: string; className: string }

const neutral =
  'border-slate-300 bg-slate-100 text-slate-800 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100'
const good =
  'border-emerald-300 bg-emerald-50 text-emerald-900 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-100'
const warn =
  'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-100'
const bad =
  'border-rose-300 bg-rose-50 text-rose-900 dark:border-rose-700 dark:bg-rose-950 dark:text-rose-100'

const STATE_LOOKS: Record<MonitorState, Look> = {
  Healthy: { icon: '✓', className: good },
  Degraded: { icon: '!', className: warn },
  Down: { icon: '✕', className: bad },
  Unknown: { icon: '?', className: neutral },
  Paused: { icon: '‖', className: neutral },
}

function Badge({ look, label, title }: { look: Look; label: string; title?: string }) {
  return (
    <span
      title={title}
      className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-semibold ${look.className}`}
    >
      <span aria-hidden="true">{look.icon}</span>
      {label}
    </span>
  )
}

/** Monitor state: always text + icon + colour, never colour alone. */
export function StatusBadge({ state }: { state: MonitorState }) {
  return <Badge look={STATE_LOOKS[state]} label={state} title={STATE_DESCRIPTIONS[state]} />
}

/** The worker hasn't checked this monitor for more than two intervals. */
export function StaleBadge() {
  return (
    <Badge
      look={{ icon: '⏱', className: warn }}
      label="Stale"
      title="No check result for more than two intervals. The PulseWatch worker may be behind or stopped."
    />
  )
}

export function IncidentStatusBadge({ status }: { status: IncidentStatus }) {
  return status === 'Open' ? (
    <Badge look={{ icon: '✕', className: bad }} label="Open" />
  ) : (
    <Badge look={{ icon: '✓', className: good }} label="Resolved" />
  )
}

const NOTIFICATION_LOOKS: Record<NotificationStatus, Look & { label: string }> = {
  Pending: { icon: '…', className: neutral, label: 'Pending' },
  Sending: { icon: '↗', className: neutral, label: 'Sending' },
  Sent: { icon: '✓', className: good, label: 'Sent' },
  Failed: { icon: '↻', className: warn, label: 'Failed, will retry' },
  Exhausted: { icon: '✕', className: bad, label: 'Gave up' },
}

export function NotificationStatusBadge({ status }: { status: NotificationStatus }) {
  const look = NOTIFICATION_LOOKS[status]
  return <Badge look={look} label={look.label} />
}
