import { ApiStatus } from '../features/system/ApiStatus.tsx'

export function DashboardPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-400">
          Know when your APIs fail - and understand why.
        </p>
      </div>

      <ApiStatus />

      <section
        aria-labelledby="roadmap-heading"
        className="rounded-lg border border-dashed border-slate-300 p-5 text-sm text-slate-600 dark:border-slate-700 dark:text-slate-400"
      >
        <h2 id="roadmap-heading" className="font-medium text-slate-800 dark:text-slate-200">
          Not built yet
        </h2>
        <p className="mt-1">
          Sign-in, monitors, check history and incidents are added in later milestones. This page
          only shows whether the PulseWatch API is reachable.
        </p>
      </section>
    </div>
  )
}
