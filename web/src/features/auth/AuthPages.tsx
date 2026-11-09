import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { login, register, type Credentials } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import { Button } from '../../components/Button.tsx'
import { Field, FormError } from '../../components/Field.tsx'
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

function AuthLayout({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="mx-auto mt-6 w-full max-w-sm">
      <div className="mb-6 flex items-center justify-center gap-2 text-lg font-semibold">
        <img src="/favicon.svg" alt="" className="h-8 w-8" />
        PulseWatch
      </div>
      <div className="rounded-lg border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
        <h1 className="mb-4 text-xl font-semibold">{title}</h1>
        {children}
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
    return <LoadingBlock label="Checking your session" rows={2} />
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
    <AuthLayout title="Sign in">
      <form
        noValidate
        className="space-y-4"
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
    <AuthLayout title="Create an account">
      <form
        noValidate
        className="space-y-4"
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
    <p className="mt-4 text-center text-sm text-slate-600 dark:text-slate-400">
      {mode === 'login' ? (
        <>
          New to PulseWatch?{' '}
          <Link
            className="font-medium text-sky-700 underline dark:text-sky-400"
            to={`/register${suffix}`}
          >
            Create an account
          </Link>
        </>
      ) : (
        <>
          Already have an account?{' '}
          <Link
            className="font-medium text-sky-700 underline dark:text-sky-400"
            to={`/login${suffix}`}
          >
            Sign in
          </Link>
        </>
      )}
    </p>
  )
}
