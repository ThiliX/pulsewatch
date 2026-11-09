import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiStatus } from './ApiStatus.tsx'

describe('ApiStatus', () => {
  it('shows "API reachable" when the readiness endpoint returns 200', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response('Healthy', { status: 200 }))

    render(<ApiStatus />)

    expect(await screen.findByRole('heading', { name: 'API reachable' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/health/ready', expect.anything())
  })

  it('shows "API not ready" with the status code when readiness fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('Unhealthy', { status: 503 }))

    render(<ApiStatus />)

    expect(await screen.findByRole('heading', { name: 'API not ready' })).toBeInTheDocument()
    expect(screen.getByText(/HTTP 503/)).toBeInTheDocument()
  })

  it('shows "API unreachable" on a network error, and recovers on retry', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(new Response('Healthy', { status: 200 }))

    render(<ApiStatus />)
    expect(await screen.findByRole('heading', { name: 'API unreachable' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Check again' }))

    expect(await screen.findByRole('heading', { name: 'API reachable' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
