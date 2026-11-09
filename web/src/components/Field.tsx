import { useId, type ComponentProps, type ReactNode } from 'react'

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
      <label htmlFor={inputId} className="block text-sm font-medium">
        {label}
      </label>
      <input
        id={inputId}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy || undefined}
        className={`mt-1 block w-full rounded-md border bg-white px-3 py-2 text-sm dark:bg-slate-900 ${
          error ? 'border-rose-600 dark:border-rose-500' : 'border-slate-300 dark:border-slate-600'
        }`}
        {...input}
      />
      {hint && (
        <p id={hintId} className="mt-1 text-xs text-slate-600 dark:text-slate-400">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="mt-1 text-sm text-rose-700 dark:text-rose-400">
          {error}
        </p>
      )}
    </div>
  )
}

/** A form-level error (not tied to one field). */
export function FormError({ message }: { message: string | undefined }) {
  if (!message) return null
  return (
    <p
      role="alert"
      className="rounded-md border border-rose-300 bg-rose-50 p-3 text-sm text-rose-900 dark:border-rose-800 dark:bg-rose-950 dark:text-rose-100"
    >
      <span aria-hidden="true">✕ </span>
      {message}
    </p>
  )
}
