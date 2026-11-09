import type { ComponentProps } from 'react'
import { Link, type LinkProps } from 'react-router'

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost'

const base =
  'inline-flex items-center justify-center gap-1.5 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50'

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-sky-700 text-white hover:bg-sky-800 dark:bg-sky-600 dark:hover:bg-sky-500',
  secondary:
    'border border-slate-300 bg-white text-slate-800 hover:bg-slate-50 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:hover:bg-slate-800',
  danger: 'bg-rose-700 text-white hover:bg-rose-800 dark:bg-rose-600 dark:hover:bg-rose-500',
  ghost: 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800',
}

function buttonClass(variant: ButtonVariant = 'secondary', extra = ''): string {
  return `${base} ${variants[variant]} ${extra}`.trim()
}

type ButtonProps = ComponentProps<'button'> & {
  variant?: ButtonVariant
  busy?: boolean
}

export function Button({
  variant = 'secondary',
  busy = false,
  className = '',
  children,
  disabled,
  ...rest
}: ButtonProps) {
  return (
    <button
      type="button"
      {...rest}
      disabled={disabled === true || busy}
      aria-busy={busy || undefined}
      className={buttonClass(variant, className)}
    >
      {busy && <Spinner />}
      {children}
    </button>
  )
}

export function ButtonLink({
  variant = 'secondary',
  className = '',
  ...rest
}: LinkProps & { variant?: ButtonVariant }) {
  return <Link {...rest} className={buttonClass(variant, className)} />
}

function Spinner() {
  return (
    <span
      aria-hidden="true"
      className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-r-transparent"
    />
  )
}
