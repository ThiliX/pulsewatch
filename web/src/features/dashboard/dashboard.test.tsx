import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../../test/render.tsx'
import { incident, paged, server, signedIn, summary } from '../../test/server.ts'

describe('dashboard', () => {
  it('shows summary counts and open incidents', async () => {
    server.use(
      ...signedIn(),
      http.get('*/api/v1/dashboard/summary', () => HttpResponse.json(summary({ stale: 2 }))),
      http.get('*/api/v1/incidents', () => HttpResponse.json(paged([incident()]))),
    )

    renderApp('/')

    const cards = await screen.findByRole('region', { name: 'Summary' })
    expect(within(cards).getByRole('link', { name: /Healthy\s*2/ })).toHaveAttribute(
      'href',
      '/monitors?state=Healthy',
    )
    expect(within(cards).getByText('Stale').parentElement).toHaveTextContent('2')
    const open = await screen.findByRole('link', { name: 'Orders API' })
    expect(open).toHaveAttribute('href', '/incidents/inc-1')
    expect(screen.getByText(/Unexpected status 503/)).toBeInTheDocument()
    expect(screen.queryByText(/worker is offline/)).not.toBeInTheDocument()
  })

  it('warns when the PulseWatch worker is offline', async () => {
    server.use(
      ...signedIn(),
      http.get('*/api/v1/dashboard/summary', () =>
        HttpResponse.json(
          summary({
            workerOnline: false,
            workerLastSeenAtUtc: new Date(Date.now() - 600_000).toISOString(),
          }),
        ),
      ),
      http.get('*/api/v1/incidents', () => HttpResponse.json(paged([]))),
    )

    renderApp('/')

    const banner = await screen.findByText('The PulseWatch worker is offline')
    expect(banner.closest('[role="alert"]')).toHaveTextContent('10 min ago')
    expect(screen.getByText('No open incidents.')).toBeInTheDocument()
  })

  it('invites the user to add a first monitor', async () => {
    server.use(
      ...signedIn(),
      http.get('*/api/v1/dashboard/summary', () =>
        HttpResponse.json(summary({ totalMonitors: 0, healthy: 0, down: 0, openIncidents: 0 })),
      ),
      http.get('*/api/v1/incidents', () => HttpResponse.json(paged([]))),
    )

    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'No monitors yet' })).toBeInTheDocument()
  })

  it('shows an error with a retry when the API fails', async () => {
    let calls = 0
    server.use(
      ...signedIn(),
      http.get('*/api/v1/dashboard/summary', () => {
        calls++
        return calls === 1 ? HttpResponse.error() : HttpResponse.json(summary())
      }),
      http.get('*/api/v1/incidents', () => HttpResponse.json(paged([]))),
    )
    const { user } = renderApp('/')

    expect(await screen.findByText("Couldn't load the dashboard")).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('region', { name: 'Summary' })).toBeInTheDocument()
  })
})
