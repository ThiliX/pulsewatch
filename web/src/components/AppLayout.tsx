import { Link, NavLink, Outlet } from 'react-router'
import { useMe, useSignOut } from '../features/auth/auth.ts'
import { Button } from './Button.tsx'

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-1.5 text-sm font-medium ${
    isActive
      ? 'bg-slate-100 text-slate-900 dark:bg-slate-800 dark:text-white'
      : 'text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
  }`

export function AppLayout() {
  const me = useMe()
  const signOut = useSignOut()

  return (
    <div className="flex min-h-screen flex-col">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded focus:bg-white focus:px-3 focus:py-2 focus:text-slate-900"
      >
        Skip to content
      </a>
      <header className="border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
          <Link to="/" className="flex items-center gap-2 font-semibold tracking-tight">
            <img src="/favicon.svg" alt="" className="h-7 w-7" />
            <span>PulseWatch</span>
          </Link>
          {me.data && (
            <>
              <nav
                aria-label="Main"
                className="order-last flex w-full gap-1 sm:order-none sm:w-auto"
              >
                <NavLink to="/" end className={navLinkClass}>
                  Dashboard
                </NavLink>
                <NavLink to="/monitors" className={navLinkClass}>
                  Monitors
                </NavLink>
                <NavLink to="/incidents" className={navLinkClass}>
                  Incidents
                </NavLink>
              </nav>
              <div className="ml-auto flex items-center gap-2">
                <span className="hidden max-w-48 truncate text-sm text-slate-600 md:inline dark:text-slate-400">
                  {me.data.email}
                </span>
                <Button variant="ghost" onClick={() => signOut.mutate()} busy={signOut.isPending}>
                  Sign out
                </Button>
              </div>
            </>
          )}
        </div>
      </header>
      <main id="main" className="mx-auto w-full max-w-6xl flex-1 px-4 py-8">
        <Outlet />
      </main>
      <footer className="border-t border-slate-200 py-4 text-center text-xs text-slate-500 dark:border-slate-800 dark:text-slate-400">
        PulseWatch · open-source API monitoring · checks run from a single location
      </footer>
    </div>
  )
}
