import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../../test/render.tsx'
import {
  emptyStats,
  monitor,
  monitorDetail,
  paged,
  problem,
  server,
  signedIn,
} from '../../test/server.ts'

describe('monitor list', () => {
  it('shows an empty state with a way to add the first monitor', async () => {
    server.use(
      ...signedIn(),
      http.get('*/api/v1/monitors', () => HttpResponse.json(paged([]))),
    )

    renderApp('/monitors')

    expect(await screen.findByRole('heading', { name: 'No monitors yet' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Add your first monitor' })).toHaveAttribute(
      'href',
      '/monitors/new',
    )
  })

  it('shows state badges with text and a stale badge', async () => {
    server.use(
      ...signedIn(),
      http.get('*/api/v1/monitors', () =>
        HttpResponse.json(
          paged([
            monitor({
              id: 'a',
              name: 'Down API',
              state: 'Down',
              lastOutcome: 'HttpStatusFailure',
              lastStatusCode: 503,
            }),
            monitor({ id: 'b', name: 'Quiet API', state: 'Healthy', isStale: true }),
          ]),
        ),
      ),
    )

    renderApp('/monitors')

    // The list renders both a card view (phones) and a table (wider screens).
    const table = await screen.findByRole('table', { name: 'Your monitors' })
    const rows = within(table).getAllByRole('row')
    expect(within(rows[1]!).getByText('Down')).toBeInTheDocument()
    expect(within(rows[1]!).getByText('Unexpected status 503')).toBeInTheDocument()
    expect(within(rows[2]!).getByText('Healthy')).toBeInTheDocument()
    expect(within(rows[2]!).getByText('Stale')).toBeInTheDocument()
  })

  it('filters by state through the URL', async () => {
    const requested: (string | null)[] = []
    server.use(
      ...signedIn(),
      http.get('*/api/v1/monitors', ({ request }) => {
        requested.push(new URL(request.url).searchParams.get('state'))
        return HttpResponse.json(paged([]))
      }),
    )
    const { user } = renderApp('/monitors')

    await user.selectOptions(await screen.findByLabelText('State'), 'Down')

    expect(await screen.findByRole('heading', { name: 'No down monitors' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/monitors?state=Down')
    expect(requested).toContain('Down')
  })
})

describe('monitor form', () => {
  it('shows server validation errors on the matching fields', async () => {
    server.use(
      ...signedIn(),
      http.post('*/api/v1/monitors', () =>
        problem(400, 'One or more validation errors occurred.', {
          errors: { url: ['The URL resolves to a private or reserved address.'] },
        }),
      ),
    )
    const { user } = renderApp('/monitors/new')

    await user.type(await screen.findByLabelText('Name'), 'Internal')
    await user.type(screen.getByLabelText('URL'), 'https://internal.example.com/')
    await user.click(screen.getByRole('button', { name: 'Create monitor' }))

    expect(
      await screen.findByText('The URL resolves to a private or reserved address.'),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('URL')).toHaveAttribute('aria-invalid', 'true')
  })

  it('mirrors server rules on the client', async () => {
    server.use(...signedIn())
    const { user } = renderApp('/monitors/new')

    await user.type(await screen.findByLabelText('Name'), 'API')
    await user.type(screen.getByLabelText('URL'), 'ftp://example.com')
    const timeout = screen.getByLabelText('Timeout (ms)')
    await user.clear(timeout)
    await user.type(timeout, '60000')
    await user.click(screen.getByRole('button', { name: 'Create monitor' }))

    expect(await screen.findByText('Enter a full http:// or https:// URL.')).toBeInTheDocument()
    expect(screen.getByText('Timeout must be between 1000 and 15000.')).toBeInTheDocument()
  })

  it('creates a monitor and opens its page', async () => {
    let body: Record<string, unknown> = {}
    const created = monitor({
      id: 'new-1',
      name: 'Payments',
      state: 'Unknown',
      lastCheckedAtUtc: null,
      lastOutcome: null,
    })
    server.use(
      ...signedIn(),
      http.post('*/api/v1/monitors', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(created, { status: 201 })
      }),
      ...monitorDetail(created),
    )
    const { user } = renderApp('/monitors/new')

    await user.type(await screen.findByLabelText('Name'), 'Payments')
    await user.type(screen.getByLabelText('URL'), 'https://pay.example.com/health')
    await user.click(screen.getByRole('button', { name: 'Create monitor' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Payments' })).toBeInTheDocument()
    expect(body).toMatchObject({
      name: 'Payments',
      url: 'https://pay.example.com/health',
      intervalSeconds: 60,
      failureThreshold: 3,
    })
    expect(screen.getByText('"Payments" was created.')).toBeInTheDocument()
  })
})

describe('monitor detail', () => {
  it('shows "No data" instead of 100 % when there are no checks', async () => {
    server.use(
      ...signedIn(),
      ...monitorDetail(monitor({ state: 'Unknown', lastCheckedAtUtc: null, lastOutcome: null })),
    )

    renderApp('/monitors/mon-1')

    expect(await screen.findByTestId('uptime-24h')).toHaveTextContent('No data')
    expect(screen.getByTestId('uptime-7d')).toHaveTextContent('No data')
    expect(screen.getAllByText('No checks in this window')).toHaveLength(3)
    for (const window of ['24h', '7d', '30d']) {
      expect(screen.getByTestId(`uptime-${window}`)).not.toHaveTextContent('100')
    }
  })

  it('shows uptime with its sample count and never rounds a failure up to 100 %', async () => {
    const stats = emptyStats()
    stats.windows[0] = {
      ...stats.windows[0]!,
      totalChecks: 10_000,
      successfulChecks: 9_999,
      uptimePercent: 99.99999,
      p95DurationMs: 240,
    }
    server.use(...signedIn(), ...monitorDetail(monitor(), stats))

    renderApp('/monitors/mon-1')

    expect(await screen.findByTestId('uptime-24h')).toHaveTextContent('99.99 %')
    expect(screen.getByText('9999 of 10000 checks succeeded')).toBeInTheDocument()
    expect(screen.getByText('240 ms')).toBeInTheDocument()
  })

  it('shows the state as text and marks a stale monitor', async () => {
    server.use(...signedIn(), ...monitorDetail(monitor({ state: 'Degraded', isStale: true })))

    renderApp('/monitors/mon-1')

    expect(await screen.findByText('Degraded')).toBeInTheDocument()
    expect(screen.getByText('Stale')).toBeInTheDocument()
  })

  it('asks for confirmation naming the monitor before deleting it', async () => {
    let deleted = false
    server.use(
      ...signedIn(),
      ...monitorDetail(monitor()),
      http.delete('*/api/v1/monitors/mon-1', () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
      http.get('*/api/v1/monitors', () => HttpResponse.json(paged([]))),
    )
    const { user } = renderApp('/monitors/mon-1')

    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    const dialog = screen.getByRole('alertdialog', { name: 'Delete "Orders API"?' })
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(deleted).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await user.click(screen.getByRole('button', { name: 'Delete monitor' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/monitors$/))
    expect(deleted).toBe(true)
    expect(await screen.findByText('"Orders API" was deleted.')).toBeInTheDocument()
  })

  it('explains when a manual check was requested too recently', async () => {
    server.use(
      ...signedIn(),
      ...monitorDetail(monitor()),
      http.post('*/api/v1/monitors/mon-1/check-now', () =>
        HttpResponse.json(
          { title: 'A check was requested recently.', status: 429 },
          {
            status: 429,
            headers: { 'Retry-After': '42', 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    )
    const { user } = renderApp('/monitors/mon-1')

    await user.click(await screen.findByRole('button', { name: 'Check now' }))

    expect(
      await screen.findByText('A check was requested recently. Try again in 42 s.'),
    ).toBeInTheDocument()
  })

  it('sends only changed fields with If-Match when editing', async () => {
    let patch: unknown
    let ifMatch: string | null = null
    const m = monitor()
    server.use(
      ...signedIn(),
      ...monitorDetail(m),
      http.patch('*/api/v1/monitors/mon-1', async ({ request }) => {
        patch = await request.json()
        ifMatch = request.headers.get('If-Match')
        return HttpResponse.json({ ...m, name: 'Orders API v2' })
      }),
    )
    const { user } = renderApp('/monitors/mon-1/edit')

    const name = await screen.findByLabelText('Name')
    await user.clear(name)
    await user.type(name, 'Orders API v2')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(patch).toEqual({ name: 'Orders API v2' }))
    expect(ifMatch).toBe('"42"')
  })

  it('explains a conflicting edit and offers to reload', async () => {
    server.use(
      ...signedIn(),
      ...monitorDetail(monitor()),
      http.patch('*/api/v1/monitors/mon-1', () =>
        problem(412, 'The monitor was changed by another request.', {
          detail: 'Reload it and apply your change again.',
        }),
      ),
    )
    const { user } = renderApp('/monitors/mon-1/edit')

    const name = await screen.findByLabelText('Name')
    await user.type(name, '!')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The monitor was changed by another request.',
    )
    expect(screen.getByRole('button', { name: 'Reload the latest version' })).toBeInTheDocument()
  })
})
