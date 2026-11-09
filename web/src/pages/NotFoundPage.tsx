import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <div className="space-y-3">
      <h1 className="text-2xl font-semibold tracking-tight">Page not found</h1>
      <p className="text-slate-600 dark:text-slate-400">This page doesn't exist.</p>
      <Link to="/" className="text-sky-700 underline hover:text-sky-900 dark:text-sky-400">
        Back to the dashboard
      </Link>
    </div>
  )
}
