import {
  CircleAlert,
  CircleCheck,
  CircleHelp,
  CirclePause,
  CircleX,
  Clock,
  Loader,
  RotateCw,
  Send,
  type LucideIcon,
} from 'lucide-react'
import type { IncidentStatus, MonitorState, NotificationStatus } from '../../api/types.ts'
import { STATE_DESCRIPTIONS } from '../../lib/format.ts'
import { DOT_TONES, STATE_TONES, type Tone } from './statusTones.ts'

type Look = { icon: LucideIcon; tone: Tone }

const TONES: Record<Tone, string> = {
  good: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20 dark:bg-emerald-500/10 dark:text-emerald-400 dark:ring-emerald-500/25',
  warn: 'bg-amber-50 text-amber-800 ring-amber-600/25 dark:bg-amber-500/10 dark:text-amber-400 dark:ring-amber-500/25',
  bad: 'bg-rose-50 text-rose-700 ring-rose-600/20 dark:bg-rose-500/10 dark:text-rose-400 dark:ring-rose-500/25',
  neutral:
    'bg-zinc-100 text-zinc-700 ring-zinc-500/20 dark:bg-zinc-500/10 dark:text-zinc-300 dark:ring-zinc-500/25',
  info: 'bg-indigo-50 text-indigo-700 ring-indigo-600/20 dark:bg-indigo-500/10 dark:text-indigo-300 dark:ring-indigo-500/25',
}

const STATE_ICONS: Record<MonitorState, LucideIcon> = {
  Healthy: CircleCheck,
  Degraded: CircleAlert,
  Down: CircleX,
  Unknown: CircleHelp,
  Paused: CirclePause,
}

const stateLook = (state: MonitorState): Look => ({
  icon: STATE_ICONS[state],
  tone: STATE_TONES[state],
})

function Badge({ look, label, title }: { look: Look; label: string; title?: string }) {
  const Icon = look.icon
  return (
    <span
      title={title}
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap ring-1 ring-inset ${TONES[look.tone]}`}
    >
      <Icon aria-hidden="true" className="size-3.5" strokeWidth={2.25} />
      {label}
    </span>
  )
}

/** Monitor state: always text + icon + colour, never colour alone. */
export function StatusBadge({ state }: { state: MonitorState }) {
  return <Badge look={stateLook(state)} label={state} title={STATE_DESCRIPTIONS[state]} />
}

/** A small coloured dot; down monitors pulse. Decorative: pair it with visible text. */
export function StatusDot({ state }: { state: MonitorState }) {
  const tone = STATE_TONES[state]
  return (
    <span aria-hidden="true" className="relative flex size-2.5 shrink-0">
      {state === 'Down' && (
        <span
          className={`absolute inline-flex size-full animate-ping rounded-full opacity-60 ${DOT_TONES[tone]}`}
        />
      )}
      <span className={`relative inline-flex size-2.5 rounded-full ${DOT_TONES[tone]}`} />
    </span>
  )
}

/** The worker hasn't checked this monitor for more than two intervals. */
export function StaleBadge() {
  return (
    <Badge
      look={{ icon: Clock, tone: 'warn' }}
      label="Stale"
      title="No check result for more than two intervals. The PulseWatch worker may be behind or stopped."
    />
  )
}

export function IncidentStatusBadge({ status }: { status: IncidentStatus }) {
  return status === 'Open' ? (
    <Badge look={{ icon: CircleX, tone: 'bad' }} label="Open" />
  ) : (
    <Badge look={{ icon: CircleCheck, tone: 'good' }} label="Resolved" />
  )
}

const NOTIFICATION_LOOKS: Record<NotificationStatus, Look & { label: string }> = {
  Pending: { icon: Clock, tone: 'neutral', label: 'Pending' },
  Sending: { icon: Loader, tone: 'info', label: 'Sending' },
  Sent: { icon: Send, tone: 'good', label: 'Sent' },
  Failed: { icon: RotateCw, tone: 'warn', label: 'Failed, will retry' },
  Exhausted: { icon: CircleX, tone: 'bad', label: 'Gave up' },
}

export function NotificationStatusBadge({ status }: { status: NotificationStatus }) {
  const look = NOTIFICATION_LOOKS[status]
  return <Badge look={look} label={look.label} />
}
