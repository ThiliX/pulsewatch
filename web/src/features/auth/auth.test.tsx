import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../../test/render.tsx'
import {
  antiforgery,
  ME,
  paged,
  problem,
  server,
  signedOut,
  XSRF_TOKEN,
} from '../../test/server.ts'
import { safeReturnTo } from './auth.ts'

describe('sign-in flow', () => {
  it('sends a signed-out visitor to sign in, then back to the page they wanted', async () => {
    let signedIn = false
    let loginBody: unknown
    let loginXsrf: string | null = null
    server.use(
      http.get('*/api/v1/auth/me', () =>
        signedIn ? HttpResponse.json(ME) : problem(401, 'Not signed in.'),
      ),
      antiforgery(),
      http.post('*/api/v1/auth/login', async ({ request }) => {
        loginBody = await request.json()
        loginXsrf = request.headers.get('X-XSRF-TOKEN')
        signedIn = true
        return HttpResponse.json(ME)
      }),
      http.get('*/api/v1/monitors', () => HttpResponse.json(paged([]))),
    )

    const { user } = renderApp('/monitors')

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fmonitors')

    await user.type(screen.getByLabelText('Email'), 'owner@example.com')
    await user.type(screen.getByLabelText('Password'), 'correct horse battery staple')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Monitors' })).toBeInTheDocument()
    expect(loginBody).toEqual({
      email: 'owner@example.com',
      password: 'correct horse battery staple',
    })
    expect(loginXsrf).toBe(XSRF_TOKEN)
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('shows the server message for wrong credentials', async () => {
    server.use(
      ...signedOut(),
      http.post('*/api/v1/auth/login', () => problem(401, 'Invalid email or password.')),
    )
    const { user } = renderApp('/login')

    await user.type(await screen.findByLabelText('Email'), 'owner@example.com')
    await user.type(screen.getByLabelText('Password'), 'wrong password!!')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
  })

  it('validates on the client before sending anything', async () => {
    server.use(...signedOut())
    const { user } = renderApp('/login')

    await user.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Email is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true')
  })

  it('maps server field errors onto the registration form', async () => {
    server.use(
      ...signedOut(),
      http.post('*/api/v1/auth/register', () =>
        problem(400, 'One or more validation errors occurred.', {
          errors: { password: ['Passwords must use at least 4 different characters.'] },
        }),
      ),
    )
    const { user } = renderApp('/register')

    await user.type(await screen.findByLabelText('Email'), 'new@example.com')
    await user.type(screen.getByLabelText('Password'), 'aaaaaaaaaaaa')
    await user.type(screen.getByLabelText('Confirm password'), 'aaaaaaaaaaaa')
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    expect(
      await screen.findByText('Passwords must use at least 4 different characters.'),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true')
  })

  it('refreshes a stale anti-forgery token once and retries the request', async () => {
    document.cookie = 'XSRF-TOKEN=old-token; path=/'
    const seen: (string | null)[] = []
    server.use(
      ...signedOut(),
      http.post('*/api/v1/auth/login', ({ request }) => {
        const token = request.headers.get('X-XSRF-TOKEN')
        seen.push(token)
        return token === XSRF_TOKEN
          ? HttpResponse.json(ME)
          : problem(400, 'Missing or invalid anti-forgery token.')
      }),
      http.get('*/api/v1/dashboard/summary', () => problem(500, 'Server error')),
      http.get('*/api/v1/incidents', () => HttpResponse.json(paged([]))),
    )
    const { user } = renderApp('/login')

    await user.type(await screen.findByLabelText('Email'), 'owner@example.com')
    await user.type(screen.getByLabelText('Password'), 'correct horse battery staple')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/$/))
    expect(seen).toEqual(['old-token', XSRF_TOKEN])
  })
})

describe('safeReturnTo', () => {
  it.each([
    ['/monitors?state=Down', '/monitors?state=Down'],
    [null, '/'],
    ['https://evil.example', '/'],
    ['//evil.example', '/'],
    ['/\\evil.example', '/'],
  ])('%s → %s', (input, expected) => {
    expect(safeReturnTo(input)).toBe(expected)
  })
})
