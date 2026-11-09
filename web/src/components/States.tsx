import type { ReactNode } from 'react'
import { errorMessage } from '../api/client.ts'
import { formatLocal, formatRelative, formatUtc } from '../lib/time.ts'
import { Button } from './Button.tsx'

export function Skeleton({ className = 'h-4 w-full' }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      className={`animate-pulse rounded bg-slate-200 dark:bg-slate-800 ${className}`}
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
  children,
  action,
}: {
  title: string
  children?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="rounded-lg border border-dashed border-slate-300 p-8 text-center dark:border-slate-700">
      <h2 className="font-medium">{title}</h2>
      {children && (
        <div className="mt-1 text-sm text-slate-600 dark:text-slate-400">{children}</div>
      )}
      {action && <div className="mt-4 flex justify-center">{action}</div>}
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
      className="flex flex-col gap-3 rounded-lg border border-rose-300 bg-rose-50 p-4 text-rose-900 sm:flex-row sm:items-center sm:justify-between dark:border-rose-800 dark:bg-rose-950 dark:text-rose-100"
    >
      <div>
        <p className="font-medium">
          <span aria-hidden="true">✕ </span>
          {title}
        </p>
        <p className="text-sm">{errorMessage(error)}</p>
      </div>
      {onRetry && (
        <Button onClick={onRetry} className="self-start sm:self-center">
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
      <p role="status" className="text-xs font-medium text-amber-800 dark:text-amber-300">
        <span aria-hidden="true">⚠ </span>
        Couldn't refresh. Showing data from <LocalTime iso={iso} relative />.
      </p>
    )
  }
  return (
    <p className="text-xs text-slate-500 dark:text-slate-400">
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
      <Button onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        Previous
      </Button>
      <span>
        Page {page} of {pages}
      </span>
      <Button onClick={() => onPageChange(page + 1)} disabled={page >= pages}>
        Next
      </Button>
    </nav>
  )
}

export function PageHeader({
  title,
  children,
  actions,
}: {
  title: string
  children?: ReactNode
  actions?: ReactNode
}) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <h1 className="truncate text-2xl font-semibold tracking-tight">{title}</h1>
        {children && (
          <div className="mt-1 text-sm text-slate-600 dark:text-slate-400">{children}</div>
        )}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
