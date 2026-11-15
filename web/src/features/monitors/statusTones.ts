import type { MonitorState } from '../../api/types.ts'

export type Tone = 'good' | 'warn' | 'bad' | 'neutral' | 'info'

export const STATE_TONES: Record<MonitorState, Tone> = {
  Healthy: 'good',
  Degraded: 'warn',
  Down: 'bad',
  Unknown: 'neutral',
  Paused: 'neutral',
}

/** Dot and bar colours for compact status indicators. */
export const DOT_TONES: Record<Tone, string> = {
  good: 'bg-emerald-500',
  warn: 'bg-amber-500',
  bad: 'bg-rose-500',
  neutral: 'bg-zinc-400 dark:bg-zinc-500',
  info: 'bg-indigo-500',
}
