import { useApiStatus, type ApiStatusState } from './useApiStatus.ts'

type Presentation = { label: string; detail: string; icon: string; tone: string }

function present(state: ApiStatusState): Presentation {
  switch (state.kind) {
    case 'loading':
      return {
        label: 'Checking...',
        detail: 'Contacting the PulseWatch API.',
        icon: '⋯',
        tone: 'border-slate-300 bg-slate-100 text-slate-800 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200',
      }
    case 'ready':
      return {
        label: 'API reachable',
        detail: 'The PulseWatch API responded and reports it is ready.',
        icon: '✓',
        tone: 'border-emerald-300 bg-emerald-50 text-emerald-900 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-100',
      }
    case 'not-ready':
      return {
        label: 'API not ready',
        detail: `The API responded with HTTP ${state.httpStatus}. A dependency such as the database may be down.`,
        icon: '!',
        tone: 'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100',
      }
    case 'unreachable':
      return {
        label: 'API unreachable',
        detail: 'Could not contact the PulseWatch API. Is it running on port 5080?',
        icon: '✕',
        tone: 'border-rose-300 bg-rose-50 text-rose-900 dark:border-rose-800 dark:bg-rose-950 dark:text-rose-100',
      }
  }
}

export function ApiStatus() {
  const { state, retry } = useApiStatus()
  const { label, detail, icon, tone } = present(state)

  return (
    <section
      aria-labelledby="api-status-heading"
      aria-live="polite"
      aria-busy={state.kind === 'loading'}
      className={`flex flex-col gap-3 rounded-lg border p-5 sm:flex-row sm:items-center sm:justify-between ${tone}`}
    >
      <div className="flex items-start gap-3">
        <span aria-hidden="true" className="mt-0.5 text-lg font-bold leading-none">
          {icon}
        </span>
        <div>
          <h2 id="api-status-heading" className="font-semibold">
            {label}
          </h2>
          <p className="text-sm opacity-90">{detail}</p>
          {state.kind !== 'loading' && (
            <p className="mt-1 text-xs opacity-75">
              Checked at{' '}
              <time dateTime={state.checkedAt.toISOString()}>
                {state.checkedAt.toLocaleTimeString()}
              </time>
            </p>
          )}
        </div>
      </div>
      <button
        type="button"
        onClick={retry}
        disabled={state.kind === 'loading'}
        className="self-start rounded-md border border-current px-3 py-1.5 text-sm font-medium disabled:opacity-50 sm:self-center"
      >
        Check again
      </button>
    </section>
  )
}
