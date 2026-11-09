/** Local date and time with the time zone name, e.g. "9 Nov 2026, 14:03:12 GMT+5:30". */
export function formatLocal(iso: string, options: { seconds?: boolean } = {}): string {
  return new Date(iso).toLocaleString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    ...(options.seconds === false ? {} : { second: '2-digit' }),
    timeZoneName: 'short',
  })
}

/** Exact UTC time, shown on hover. */
export function formatUtc(iso: string): string {
  return `${new Date(iso).toISOString().replace('T', ' ').slice(0, 19)} UTC`
}

/** "just now", "5 min ago", "3 h ago", "2 days ago". */
export function formatRelative(iso: string, now: Date = new Date()): string {
  const seconds = Math.round((now.getTime() - new Date(iso).getTime()) / 1000)
  if (seconds < 0) {
    return formatIn(-seconds)
  }
  if (seconds < 45) return 'just now'
  if (seconds < 3600) return `${Math.max(1, Math.round(seconds / 60))} min ago`
  if (seconds < 86_400) return `${Math.round(seconds / 3600)} h ago`
  const days = Math.round(seconds / 86_400)
  return `${days} ${days === 1 ? 'day' : 'days'} ago`
}

function formatIn(seconds: number): string {
  if (seconds < 45) return 'in a moment'
  if (seconds < 3600) return `in ${Math.max(1, Math.round(seconds / 60))} min`
  return `in ${Math.round(seconds / 3600)} h`
}

/** Outage length: "45 s", "12 min", "3 h 5 min", "2 d 4 h". */
export function formatDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.round(totalSeconds))
  if (s < 60) return `${s} s`
  const minutes = Math.floor(s / 60)
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return minutes % 60 ? `${hours} h ${minutes % 60} min` : `${hours} h`
  const days = Math.floor(hours / 24)
  return hours % 24 ? `${days} d ${hours % 24} h` : `${days} d`
}

/** Short axis label for the latency chart. */
export function formatAxisTime(ms: number, bucketSeconds: number): string {
  const date = new Date(ms)
  return bucketSeconds >= 3600
    ? date.toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit' })
    : date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
}
