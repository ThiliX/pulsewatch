import { CircleAlert } from 'lucide-react'
import { useId, type ComponentProps, type ReactNode } from 'react'

const inputBase =
  'block w-full rounded-lg border bg-white px-3 py-2 text-sm text-zinc-900 shadow-xs transition-colors placeholder:text-zinc-400 focus:ring-2 focus:outline-none dark:bg-zinc-950 dark:text-zinc-100 dark:placeholder:text-zinc-500'
const inputOk =
  'border-zinc-300 focus:border-indigo-500 focus:ring-indigo-500/20 dark:border-zinc-700 dark:focus:border-indigo-400'
const inputError =
  'border-rose-500 focus:border-rose-500 focus:ring-rose-500/20 dark:border-rose-500'

type FieldProps = ComponentProps<'input'> & {
  label: string
  error?: string | undefined
  hint?: ReactNode
}

/** A labelled input with an optional hint and an error message linked for screen readers. */
export function Field({ label, error, hint, id, className = '', ...input }: FieldProps) {
  const generated = useId()
  const inputId = id ?? generated
  const hintId = `${inputId}-hint`
  const errorId = `${inputId}-error`
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ')

  return (
    <div className={className}>
      <label
        htmlFor={inputId}
        className="block text-sm font-medium text-zinc-800 dark:text-zinc-200"
      >
        {label}
      </label>
      <input
        id={inputId}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy || undefined}
        className={`mt-1.5 ${inputBase} ${error ? inputError : inputOk}`}
        {...input}
      />
      {hint && (
        <p id={hintId} className="mt-1.5 text-xs text-zinc-500 dark:text-zinc-400">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="mt-1.5 text-sm text-rose-600 dark:text-rose-400">
          {error}
        </p>
      )}
    </div>
  )
}

/** A styled native select (keeps keyboard and screen-reader behaviour for free). */
export function Select({ className = '', ...rest }: ComponentProps<'select'>) {
  return (
    <select
      className={`h-9 rounded-lg border border-zinc-300 bg-white pr-8 pl-3 text-sm text-zinc-900 shadow-xs focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500/20 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100 ${className}`}
      {...rest}
    />
  )
}

/** A form-level error (not tied to one field). */
export function FormError({ message }: { message: string | undefined }) {
  if (!message) return null
  return (
    <p
      role="alert"
      className="flex items-start gap-2 rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300"
    >
      <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <span>{message}</span>
    </p>
  )
}
