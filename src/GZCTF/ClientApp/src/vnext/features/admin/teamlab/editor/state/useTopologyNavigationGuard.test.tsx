import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider, useNavigate } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { useTopologyNavigationGuard } from './useTopologyNavigationGuard'

function GuardedRoute({ flush }: { flush: () => Promise<boolean> }) {
  const navigate = useNavigate()
  useTopologyNavigationGuard(true, flush)
  return <button onClick={() => void navigate('/next')}>leave</button>
}

describe('useTopologyNavigationGuard', () => {
  it('continues navigation after the pending draft is saved', async () => {
    const flush = vi.fn().mockResolvedValue(true)
    const router = createMemoryRouter([
      { path: '/', element: <GuardedRoute flush={flush} /> },
      { path: '/next', element: <span>next</span> },
    ])

    render(<RouterProvider router={router} />)
    fireEvent.click(screen.getByRole('button', { name: 'leave' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/next'))
    expect(flush).toHaveBeenCalledOnce()
  })

  it('cancels navigation when the draft cannot be saved', async () => {
    const flush = vi.fn().mockResolvedValue(false)
    const router = createMemoryRouter([
      { path: '/', element: <GuardedRoute flush={flush} /> },
      { path: '/next', element: <span>next</span> },
    ])

    render(<RouterProvider router={router} />)
    fireEvent.click(screen.getByRole('button', { name: 'leave' }))

    await waitFor(() => expect(flush).toHaveBeenCalledOnce())
    expect(router.state.location.pathname).toBe('/')
  })
})
