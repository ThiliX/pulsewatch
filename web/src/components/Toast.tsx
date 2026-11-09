import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react'
import { ToastContext, type ToastTone } from './toastContext.ts'

type ToastItem = { id: number; tone: ToastTone; message: string }

const TONES: Record<ToastTone, { icon: string; className: string }> = {
  success: {
    icon: '✓',
    className:
      'border-emerald-300 bg-emerald-50 text-emerald-900 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-100',
  },
  error: {
    icon: '✕',
    className:
      'border-rose-300 bg-rose-50 text-rose-900 dark:border-rose-700 dark:bg-rose-950 dark:text-rose-100',
  },
  info: {
    icon: 'i',
    className:
      'border-slate-300 bg-white text-slate-900 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100',
  },
}

const TOAST_MS = 6000

/** Short confirmations and errors, announced to screen readers. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([])
  const nextId = useRef(1)

  const dismiss = useCallback(
    (id: number) => setToasts((all) => all.filter((t) => t.id !== id)),
    [],
  )

  const show = useCallback(
    (message: string, tone: ToastTone = 'info') => {
      const id = nextId.current++
      setToasts((all) => [...all.slice(-3), { id, tone, message }])
      setTimeout(() => dismiss(id), TOAST_MS)
    },
    [dismiss],
  )

  const api = useMemo(() => ({ show }), [show])

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        role="status"
        aria-live="polite"
        className="pointer-events-none fixed inset-x-0 bottom-4 z-50 flex flex-col items-center gap-2 px-4 sm:items-end"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-md border p-3 text-sm shadow-lg ${TONES[toast.tone].className}`}
          >
            <span aria-hidden="true" className="font-bold">
              {TONES[toast.tone].icon}
            </span>
            <p className="flex-1">{toast.message}</p>
            <button
              type="button"
              onClick={() => dismiss(toast.id)}
              className="rounded px-1 opacity-70 hover:opacity-100"
              aria-label="Dismiss notification"
            >
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
