import { renderHook, waitFor } from '@testing-library/react'
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

describe('useRuntimeEvents', () => {
  afterEach(() => vi.restoreAllMocks())

  it('drains a full page with the latest cursor and merges events once', async () => {
    const firstPage = Array.from({ length: 200 }, (_, index) => event(index + 1))
    const list = vi.spyOn(teamLabRuntimeApi, 'listEvents')
      .mockResolvedValueOnce(firstPage)
      .mockResolvedValueOnce([event(201)])
    const { result } = renderHook(() => useRuntimeEvents('019f-runtime-events', 'running', 1))

    await waitFor(() => expect(result.current.events).toHaveLength(201))
    expect(list).toHaveBeenNthCalledWith(1, '019f-runtime-events', 0, 200)
    expect(list).toHaveBeenNthCalledWith(2, '019f-runtime-events', 200, 200)
    expect(new Set(result.current.events.map((item) => item.cursor)).size).toBe(201)
  })
})
