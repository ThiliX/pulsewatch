import type { ComponentProps, ReactNode } from 'react'
import { mutedText, surface } from './styles.ts'

export function Card({ className = '', ...rest }: ComponentProps<'div'>) {
  return <div className={`${surface} ${className}`} {...rest} />
}

/** A titled panel: heading row (with optional actions) above the content. */
export function Panel({
  title,
  titleId,
  description,
  actions,
  children,
  className = '',
  bodyClassName = 'p-5',
}: {
  title: ReactNode
  titleId?: string
  description?: ReactNode
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}) {
  return (
    <section aria-labelledby={titleId} className={`${surface} min-w-0 ${className}`}>
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-200 px-5 py-3.5 dark:border-zinc-800">
        <div className="min-w-0">
          <h2 id={titleId} className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">
            {title}
          </h2>
          {description && <p className={`mt-0.5 text-xs ${mutedText}`}>{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      <div className={bodyClassName}>{children}</div>
    </section>
  )
}

/** The PulseWatch mark: a pulse line on an indigo tile. */
export function Logo({ size = 'md' }: { size?: 'sm' | 'md' | 'lg' }) {
  const box = { sm: 'h-7 w-7', md: 'h-8 w-8', lg: 'h-10 w-10' }[size]
  return <img src="/favicon.svg" alt="" className={`${box} shrink-0`} />
}

/** Table header cell with the shared look. */
export function Th({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <th
      scope="col"
      className={`px-5 py-2.5 text-left text-xs font-medium tracking-wide text-zinc-500 uppercase dark:text-zinc-400 ${className}`}
    >
      {children}
    </th>
  )
}
