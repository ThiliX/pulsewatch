import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../../test/render.tsx'
import { incident, paged, server, signedIn } from '../../test/server.ts'

describe('incidents', () => {
  it('shows the timeline and the delivery status of each email', async () => {
    const resolved = incident({
      status: 'Resolved',
      resolvedAtUtc: new Date().toISOString(),
      durationSeconds: 3900,
    })
    server.use(
      ...signedIn(),
      http.get('*/api/v1/incidents/inc-1', () =>
        HttpResponse.json({
          incident: resolved,
          notifications: [
            {
              eventType: 'IncidentOpened',
              channel: 'Email',
              recipient: 'owner@example.com',
              status: 'Sent',
              attemptCount: 1,
              lastAttemptAtUtc: resolved.openedAtUtc,
              sentAtUtc: resolved.openedAtUtc,
              lastErrorCategory: null,
            },
            {
              eventType: 'IncidentResolved',
              channel: 'Email',
              recipient: 'owner@example.com',
              status: 'Exhausted',
              attemptCount: 6,
              lastAttemptAtUtc: resolved.resolvedAtUtc,
              sentAtUtc: null,
              lastErrorCategory: 'ConnectionFailed',
            },
          ],
        }),
      ),
    )

    renderApp('/incidents/inc-1')

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Incident: Orders API' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Resolved')).toBeInTheDocument()
    expect(screen.getByText('1 h 5 min')).toBeInTheDocument()
    expect(screen.getByText('First failed check')).toBeInTheDocument()
    expect(screen.getByText('Recovered')).toBeInTheDocument()
    expect(screen.getByText('Sent')).toBeInTheDocument()
    expect(screen.getByText('Gave up')).toBeInTheDocument()
    expect(screen.getByText(/last error: ConnectionFailed/)).toBeInTheDocument()
  })

  it('filters the list by status', async () => {
    const statuses: (string | null)[] = []
    server.use(
      ...signedIn(),
      http.get('*/api/v1/incidents', ({ request }) => {
        statuses.push(new URL(request.url).searchParams.get('status'))
        return HttpResponse.json(paged([]))
      }),
    )
    const { user } = renderApp('/incidents')

    await user.click(await screen.findByRole('button', { name: 'Open' }))

    expect(await screen.findByRole('heading', { name: 'No open incidents' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open' })).toHaveAttribute('aria-pressed', 'true')
    expect(statuses).toEqual([null, 'open'])
  })

  it('says an unknown incident was not found', async () => {
    server.use(
      ...signedIn(),
      http.get('*/api/v1/incidents/missing', () =>
        HttpResponse.json(
          { title: 'Incident not found.', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )

    renderApp('/incidents/missing')

    expect(await screen.findByRole('heading', { name: 'Incident not found' })).toBeInTheDocument()
  })
})
