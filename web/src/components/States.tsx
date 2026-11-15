import {
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../api/client.ts'
import { formatLocal, formatRelative, formatUtc } from '../lib/time.ts'
import { Button } from './Button.tsx'
import { mutedText } from './styles.ts'

export function Skeleton({ className = 'h-4 w-full' }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      className={`animate-pulse rounded-lg bg-zinc-200/70 dark:bg-zinc-800 ${className}`}
    />
  )
}

/** Placeholder rows while a view loads. */
export function LoadingBlock({ label, rows = 3 }: { label: string; rows?: number }) {
  return (
    <div role="status" aria-label={label} className="space-y-3">
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className="h-12 w-full" />
      ))}
    </div>
  )
}

export function EmptyState({
  title,
  icon: Icon,
  children,
  action,
}: {
  title: string
  icon?: LucideIcon
  children?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="rounded-xl border border-dashed border-zinc-300 bg-white px-6 py-12 text-center dark:border-zinc-700 dark:bg-zinc-900/40">
      {Icon && (
        <div className="mx-auto mb-4 flex size-11 items-center justify-center rounded-full bg-indigo-50 text-indigo-600 dark:bg-indigo-500/10 dark:text-indigo-400">
          <Icon aria-hidden="true" className="size-5" />
        </div>
      )}
      <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">{title}</h2>
      {children && <div className={`mx-auto mt-1 max-w-sm text-sm ${mutedText}`}>{children}</div>}
      {action && <div className="mt-5 flex justify-center">{action}</div>}
    </div>
  )
}

export function ErrorState({
  error,
  onRetry,
  title = 'Something went wrong',
}: {
  error: unknown
  onRetry?: () => void
  title?: string
}) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-xl border border-rose-200 bg-rose-50 p-4 text-rose-900 sm:flex-row sm:items-center sm:justify-between dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-200"
    >
      <div className="flex gap-3">
        <CircleAlert
          aria-hidden="true"
          className="mt-0.5 size-5 shrink-0 text-rose-600 dark:text-rose-400"
        />
        <div>
          <p className="text-sm font-semibold">{title}</p>
          <p className="text-sm opacity-90">{errorMessage(error)}</p>
        </div>
      </div>
      {onRetry && (
        <Button onClick={onRetry} size="sm" className="self-start sm:self-center">
          Try again
        </Button>
      )}
    </div>
  )
}

/** A time in the viewer's zone with its zone name; hover shows the exact UTC time. */
export function LocalTime({ iso, relative = false }: { iso: string; relative?: boolean }) {
  return (
    <time dateTime={iso} title={formatUtc(iso)}>
      {relative ? formatRelative(iso) : formatLocal(iso)}
    </time>
  )
}

/**
 * "Updated 14:03:10". When the latest refresh failed, says so plainly instead of presenting
 * the cached data as current.
 */
export function LastUpdated({
  updatedAt,
  refreshFailed,
}: {
  updatedAt: number
  refreshFailed: boolean
}) {
  if (!updatedAt) return null
  const iso = new Date(updatedAt).toISOString()
  if (refreshFailed) {
    return (
      <p
        role="status"
        className="inline-flex items-center gap-1.5 text-xs font-medium text-amber-700 dark:text-amber-400"
      >
        <TriangleAlert aria-hidden="true" className="size-3.5" />
        Couldn't refresh. Showing data from <LocalTime iso={iso} relative />.
      </p>
    )
  }
  return (
    <p className={`inline-flex items-center gap-1.5 text-xs ${mutedText}`}>
      <span aria-hidden="true" className="relative flex size-1.5">
        <span className="absolute inline-flex size-full animate-ping rounded-full bg-emerald-400 opacity-75" />
        <span className="relative inline-flex size-1.5 rounded-full bg-emerald-500" />
      </span>
      Updated{' '}
      <time dateTime={iso} title={formatUtc(iso)}>
        {new Date(updatedAt).toLocaleTimeString()}
      </time>
    </p>
  )
}

export function Pagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
  label,
}: {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  label: string
}) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize))
  if (pages <= 1) return null
  return (
    <nav aria-label={label} className="flex items-center justify-between gap-3 text-sm">
      <span className={mutedText}>
        Page <span className="font-medium text-zinc-900 dark:text-zinc-100">{page}</span> of {pages}
      </span>
      <div className="flex gap-2">
        <Button size="sm" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
          <ChevronLeft aria-hidden="true" />
          Previous
        </Button>
        <Button size="sm" onClick={() => onPageChange(page + 1)} disabled={page >= pages}>
          Next
          <ChevronRight aria-hidden="true" />
        </Button>
      </div>
    </nav>
  )
}

export function PageHeader({
  title,
  children,
  actions,
  back,
}: {
  title: string
  children?: ReactNode
  actions?: ReactNode
  /** Optional "back" link shown above the title, e.g. { to: '/monitors', label: 'Monitors' }. */
  back?: { to: string; label: string }
}) {
  return (
    <div className="space-y-3">
      {back && (
        <Link
          to={back.to}
          className={`inline-flex items-center gap-1 text-sm ${mutedText} hover:text-zinc-900 dark:hover:text-zinc-100`}
        >
          <ChevronLeft aria-hidden="true" className="size-4" />
          {back.label}
        </Link>
      )}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-zinc-900 dark:text-white">
            {title}
          </h1>
          {children && <div className={`mt-1.5 text-sm ${mutedText}`}>{children}</div>}
        </div>
        {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
      </div>
    </div>
  )
}
