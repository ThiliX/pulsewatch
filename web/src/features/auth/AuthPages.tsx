import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Activity, BellRing, ShieldCheck, type LucideIcon } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { login, register, type Credentials } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import { Button } from '../../components/Button.tsx'
import { Field, FormError } from '../../components/Field.tsx'
import { linkText } from '../../components/styles.ts'
import { Logo } from '../../components/ui.tsx'
import { LoadingBlock } from '../../components/States.tsx'
import { applyServerErrors } from '../../lib/serverErrors.ts'
import { zodResolver } from '../../lib/zodResolver.ts'
import { safeReturnTo, useMe } from './auth.ts'

// Mirrors the server: Identity requires at least 12 characters; the API caps length at 256.
const PASSWORD_MIN = 12
const email = z
  .string()
  .trim()
  .min(1, 'Email is required.')
  .email('Enter a valid email address.')
  .max(256)

const loginSchema = z.object({
  email,
  password: z.string().min(1, 'Password is required.').max(256),
})

const registerSchema = z
  .object({
    email,
    password: z
      .string()
      .min(PASSWORD_MIN, `Password must be at least ${PASSWORD_MIN} characters.`)
      .max(256, 'Password must be at most 256 characters.'),
    confirmPassword: z.string(),
  })
  .refine((v) => v.password === v.confirmPassword, {
    path: ['confirmPassword'],
    message: 'The passwords do not match.',
  })

type Mode = 'login' | 'register'

const HIGHLIGHTS: { icon: LucideIcon; title: string; text: string }[] = [
  {
    icon: Activity,
    title: 'Scheduled HTTP checks',
    text: 'Every minute or hour, with timeouts, expected status codes and safe redirects.',
  },
  {
    icon: BellRing,
    title: 'Alerts that mean something',
    text: 'Incidents open after repeated failures; you get one email when it breaks and one when it recovers.',
  },
  {
    icon: ShieldCheck,
    title: 'Secure by design',
    text: 'Four layers of SSRF protection, cookie auth and a strict Content Security Policy.',
  },
]

function AuthLayout({
  title,
  subtitle,
  children,
}: {
  title: string
  subtitle: string
  children: ReactNode
}) {
  return (
    <div className="flex min-h-screen">
      <div className="relative hidden w-[44%] max-w-xl flex-col justify-between overflow-hidden bg-zinc-950 p-10 text-white lg:flex">
        <div
          aria-hidden="true"
          className="absolute inset-0 bg-[radial-gradient(ellipse_at_top_left,rgba(99,102,241,0.35),transparent_55%),radial-gradient(ellipse_at_bottom_right,rgba(16,185,129,0.15),transparent_50%)]"
        />
        <div className="relative flex items-center gap-2.5">
          <Logo />
          <span className="text-lg font-semibold tracking-tight">PulseWatch</span>
        </div>
        <div className="relative space-y-8">
          <p className="text-3xl leading-tight font-semibold tracking-tight">
            Know when your APIs fail,
            <br />
            <span className="text-indigo-300">and understand why.</span>
          </p>
          <ul className="space-y-5">
            {HIGHLIGHTS.map(({ icon: Icon, title: t, text }) => (
              <li key={t} className="flex gap-3.5">
                <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-white/10 ring-1 ring-white/15">
                  <Icon aria-hidden="true" className="size-4.5 text-indigo-200" />
                </span>
                <span>
                  <span className="block text-sm font-semibold">{t}</span>
                  <span className="block text-sm text-zinc-400">{text}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
        <p className="relative text-xs text-zinc-500">Open-source, self-hosted API monitoring.</p>
      </div>

      <div className="flex flex-1 items-center justify-center px-4 py-12 sm:px-8">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-2.5 lg:hidden">
            <Logo />
            <span className="text-lg font-semibold tracking-tight">PulseWatch</span>
          </div>
          <h1 className="text-2xl font-semibold tracking-tight text-zinc-900 dark:text-white">
            {title}
          </h1>
          <p className="mt-1.5 mb-8 text-sm text-zinc-500 dark:text-zinc-400">{subtitle}</p>
          {children}
        </div>
      </div>
    </div>
  )
}

function useCompleteSignIn() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  return (me: Awaited<ReturnType<typeof login>>) => {
    queryClient.clear()
    queryClient.setQueryData(queryKeys.me, me)
    void navigate(safeReturnTo(params.get('returnTo')), { replace: true })
  }
}

/** Redirects away from the auth pages when already signed in. */
function useAlreadySignedIn() {
  const me = useMe()
  const [params] = useSearchParams()
  if (me.data) {
    return <Navigate to={safeReturnTo(params.get('returnTo'))} replace />
  }
  if (me.isPending) {
    return (
      <div className="mx-auto max-w-md p-8">
        <LoadingBlock label="Checking your session" rows={2} />
      </div>
    )
  }
  return null
}

export function LoginPage() {
  const redirect = useAlreadySignedIn()
  const completeSignIn = useCompleteSignIn()
  const [params] = useSearchParams()
  const [formError, setFormError] = useState<string>()
  const form = useForm({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  const mutation = useMutation({
    mutationFn: (values: Credentials) => login(values),
    onSuccess: completeSignIn,
    onError: (error) =>
      setFormError(applyServerErrors(error, ['email', 'password'], form.setError)),
  })

  if (redirect) return redirect
  const returnTo = params.get('returnTo')

  return (
    <AuthLayout title="Sign in" subtitle="Welcome back. Sign in to your monitors.">
      <form
        noValidate
        className="space-y-5"
        onSubmit={form.handleSubmit((values) => {
          setFormError(undefined)
          mutation.mutate(values)
        })}
      >
        <FormError message={formError} />
        <Field
          label="Email"
          type="email"
          autoComplete="email"
          error={form.formState.errors.email?.message}
          {...form.register('email')}
        />
        <Field
          label="Password"
          type="password"
          autoComplete="current-password"
          error={form.formState.errors.password?.message}
          {...form.register('password')}
        />
        <Button type="submit" variant="primary" busy={mutation.isPending} className="w-full">
          Sign in
        </Button>
      </form>
      <AuthSwitch mode="login" returnTo={returnTo} />
    </AuthLayout>
  )
}

export function RegisterPage() {
  const redirect = useAlreadySignedIn()
  const completeSignIn = useCompleteSignIn()
  const [params] = useSearchParams()
  const [formError, setFormError] = useState<string>()
  const form = useForm({
    resolver: zodResolver(registerSchema),
    defaultValues: { email: '', password: '', confirmPassword: '' },
  })

  const mutation = useMutation({
    mutationFn: (values: Credentials) => register(values),
    onSuccess: completeSignIn,
    onError: (error) =>
      setFormError(applyServerErrors(error, ['email', 'password'], form.setError)),
  })

  if (redirect) return redirect

  return (
    <AuthLayout title="Create an account" subtitle="Start monitoring your APIs in a minute.">
      <form
        noValidate
        className="space-y-5"
        onSubmit={form.handleSubmit(({ email: e, password }) => {
          setFormError(undefined)
          mutation.mutate({ email: e, password })
        })}
      >
        <FormError message={formError} />
        <Field
          label="Email"
          type="email"
          autoComplete="email"
          error={form.formState.errors.email?.message}
          {...form.register('email')}
        />
        <Field
          label="Password"
          type="password"
          autoComplete="new-password"
          hint={`At least ${PASSWORD_MIN} characters. A passphrase works well.`}
          error={form.formState.errors.password?.message}
          {...form.register('password')}
        />
        <Field
          label="Confirm password"
          type="password"
          autoComplete="new-password"
          error={form.formState.errors.confirmPassword?.message}
          {...form.register('confirmPassword')}
        />
        <Button type="submit" variant="primary" busy={mutation.isPending} className="w-full">
          Create account
        </Button>
      </form>
      <AuthSwitch mode="register" returnTo={params.get('returnTo')} />
    </AuthLayout>
  )
}

function AuthSwitch({ mode, returnTo }: { mode: Mode; returnTo: string | null }) {
  const suffix = returnTo ? `?returnTo=${encodeURIComponent(safeReturnTo(returnTo))}` : ''
  return (
    <p className="mt-6 text-center text-sm text-zinc-500 dark:text-zinc-400">
      {mode === 'login' ? (
        <>
          New to PulseWatch?{' '}
          <Link className={linkText} to={`/register${suffix}`}>
            Create an account
          </Link>
        </>
      ) : (
        <>
          Already have an account?{' '}
          <Link className={linkText} to={`/login${suffix}`}>
            Sign in
          </Link>
        </>
      )}
    </p>
  )
}
