import { Activity, LayoutDashboard, LogOut, MapPin, Siren, type LucideIcon } from 'lucide-react'
import { Suspense } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import { useMe, useSignOut } from '../features/auth/auth.ts'
import { LoadingBlock } from './States.tsx'
import { Logo } from './ui.tsx'

const NAV: { to: string; label: string; icon: LucideIcon; end: boolean }[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/monitors', label: 'Monitors', icon: Activity, end: false },
  { to: '/incidents', label: 'Incidents', icon: Siren, end: false },
]

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `group flex shrink-0 items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
    isActive
      ? 'bg-zinc-100 text-zinc-900 dark:bg-zinc-800 dark:text-white'
      : 'text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/60 dark:hover:text-white'
  }`

/**
 * The signed-in shell. One navigation element serves both layouts: a fixed sidebar on large
 * screens and a compact top bar with a scrollable tab row on phones.
 */
export function AppLayout() {
  const me = useMe()
  const signOut = useSignOut()
  const email = me.data?.email ?? ''

  return (
    <div className="min-h-screen">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:rounded-lg focus:bg-white focus:px-3 focus:py-2 focus:text-zinc-900 focus:shadow"
      >
        Skip to content
      </a>

      <aside className="sticky top-0 z-30 flex flex-wrap items-center border-b border-zinc-200 bg-white/90 backdrop-blur lg:fixed lg:inset-y-0 lg:left-0 lg:w-64 lg:flex-col lg:flex-nowrap lg:items-stretch lg:border-r lg:border-b-0 lg:bg-white dark:border-zinc-800 dark:bg-zinc-950/90 lg:dark:bg-zinc-950">
        <Link
          to="/"
          className="order-1 flex h-14 items-center gap-2.5 px-4 lg:h-16 lg:px-5"
          aria-label="PulseWatch home"
        >
          <Logo size="sm" />
          <span className="text-[15px] font-semibold tracking-tight text-zinc-900 dark:text-white">
            PulseWatch
          </span>
        </Link>

        <nav
          aria-label="Main"
          className="order-3 flex w-full gap-1 overflow-x-auto px-3 pb-2 lg:order-2 lg:flex-1 lg:flex-col lg:overflow-visible lg:pt-2 lg:pb-0"
        >
          <p className="hidden px-3 pb-1 text-[11px] font-medium tracking-wider text-zinc-400 uppercase lg:block dark:text-zinc-500">
            Monitoring
          </p>
          {NAV.map(({ to, label, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end} className={navLinkClass}>
              <Icon
                aria-hidden="true"
                className="size-4 text-zinc-400 group-aria-[current=page]:text-indigo-600 dark:text-zinc-500 dark:group-aria-[current=page]:text-indigo-400"
              />
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="order-2 ml-auto flex items-center gap-2 px-3 lg:order-3 lg:ml-0 lg:block lg:space-y-3 lg:border-t lg:border-zinc-200 lg:p-4 dark:lg:border-zinc-800">
          <p className="hidden items-center gap-1.5 text-xs text-zinc-500 lg:flex dark:text-zinc-400">
            <MapPin aria-hidden="true" className="size-3.5" />
            Checks run from a single location
          </p>
          <div className="flex items-center gap-2.5">
            <span
              aria-hidden="true"
              className="hidden size-8 shrink-0 items-center justify-center rounded-full bg-indigo-100 text-sm font-semibold text-indigo-700 uppercase lg:flex dark:bg-indigo-500/15 dark:text-indigo-300"
            >
              {email.charAt(0) || '?'}
            </span>
            <span className="hidden min-w-0 flex-1 truncate text-sm text-zinc-700 lg:block dark:text-zinc-300">
              {email}
            </span>
            <button
              type="button"
              onClick={() => signOut.mutate()}
              disabled={signOut.isPending}
              title="Sign out"
              className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-sm font-medium text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900 disabled:opacity-50 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-white"
            >
              <LogOut aria-hidden="true" className="size-4" />
              <span className="lg:sr-only">Sign out</span>
            </button>
          </div>
        </div>
      </aside>

      <div className="lg:pl-64">
        <main id="main" className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 lg:px-10 lg:py-10">
          <Suspense fallback={<LoadingBlock label="Loading the page" rows={3} />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  )
}
