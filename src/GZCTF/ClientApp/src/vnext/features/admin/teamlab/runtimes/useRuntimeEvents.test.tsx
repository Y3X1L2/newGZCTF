import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { SWRConfig } from 'swr'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { teamLabRuntimeApi, type TeamLabRuntimeEvent } from '../api'
import { useRuntimeEvents } from './useRuntimeEvents'

const event = (cursor: number): TeamLabRuntimeEvent => ({
  cursor,
  generation: 1,
  stage: 'deploying',
  level: 'info',
  message: `event-${cursor}`,
  objectType: null,
  objectId: null,
  createdAt: cursor,
})

// SWR keeps a process-wide cache keyed by the hook key; isolate each hook instance
// so pages drained by one test can never leak into another test's assertion.
const isolatedSwr = ({ children }: { children: ReactNode }) => (
  <SWRConfig value={{ provider: () => new Map() }}>{children}</SWRConfig>
)

describe('useRuntimeEvents', () => {
  afterEach(() => vi.restoreAllMocks())

  it('drains a full page with the latest cursor and merges events once', async () => {
    const firstPage = Array.from({ length: 200 }, (_, index) => event(index + 1))
    const list = vi.spyOn(teamLabRuntimeApi, 'listEvents')
      .mockResolvedValueOnce(firstPage)
      .mockResolvedValueOnce([event(201)])
    const { result } = renderHook(() => useRuntimeEvents('019f-runtime-events', 'running', { generation: 1, stage: 'deploying' }), { wrapper: isolatedSwr })

    await waitFor(() => expect(result.current.events).toHaveLength(201))
    expect(list).toHaveBeenNthCalledWith(1, '019f-runtime-events', 0, 200, { generation: 1, stage: 'deploying' })
    expect(list).toHaveBeenNthCalledWith(2, '019f-runtime-events', 200, 200, { generation: 1, stage: 'deploying' })
    expect(new Set(result.current.events.map((item) => item.cursor)).size).toBe(201)
  })

  it('resets events when filters change', async () => {
    const first = Array.from({ length: 3 }, (_, index) => event(index + 1))
    const second = [{ ...event(10), stage: 'probing' }]
    vi.spyOn(teamLabRuntimeApi, 'listEvents').mockResolvedValueOnce(first).mockResolvedValueOnce(second)
    const { result, rerender } = renderHook(
      ({ filters }) => useRuntimeEvents('019f-runtime-events', 'running', filters),
      { initialProps: { filters: { generation: 1, stage: 'deploying' } }, wrapper: isolatedSwr }
    )

    await waitFor(() => expect(result.current.events).toHaveLength(3))
    rerender({ filters: { generation: 2, stage: 'probing' } })
    expect(result.current.events).toEqual([])
    await waitFor(() => expect(result.current.events).toHaveLength(1))
    expect(result.current.events[0].stage).toBe('probing')
  })

  it('never merges a previous filter page after paging deep into history', async () => {
    const firstPage = Array.from({ length: 200 }, (_, index) => event(index + 1))
    const nextPage = [{ ...event(201), stage: 'deploying' }]
    const fresh = [{ ...event(5), generation: 2, stage: 'probing' }]
    vi.spyOn(teamLabRuntimeApi, 'listEvents')
      .mockResolvedValueOnce(firstPage)
      .mockResolvedValueOnce(nextPage)
      .mockResolvedValueOnce(fresh)
    const { result, rerender } = renderHook(
      ({ filters }) => useRuntimeEvents('019f-runtime-events', 'running', filters),
      { initialProps: { filters: { generation: 1, stage: 'deploying' } }, wrapper: isolatedSwr }
    )

    // Drain two full pages (201 events) with the old filter.
    await waitFor(() => expect(result.current.events).toHaveLength(201))

    // Switch the filter: the stale page must never be merged back, and the cursor
    // must start from zero again (the fresh response is a single-page, non-full page).
    rerender({ filters: { generation: 2, stage: 'probing' } })
    await waitFor(() => expect(result.current.events).toHaveLength(1))
    expect(result.current.events[0]).toMatchObject({ cursor: 5, generation: 2, stage: 'probing' })
  })
})
