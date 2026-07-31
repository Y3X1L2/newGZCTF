import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RuntimeApiError } from '../../../api/runtimeJsonClient'
import type { TeamLabTopologyDetail } from '../../api'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
import { useTopologyAutosave } from './useTopologyAutosave'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}

const detail = (revision: number) => ({ revision }) as TeamLabTopologyDetail

describe('useTopologyAutosave', () => {
  it('does not save the unchanged initial document when validation flushes it', async () => {
    const initialDocument = createEmptyTopologyDocument('Initial')
    const save = vi.fn()
    const { result } = renderHook(() =>
      useTopologyAutosave({ initialRevision: 3, initialDocument, save })
    )

    let saved = false
    await act(async () => {
      saved = await result.current.flush(initialDocument)
    })

    expect(saved).toBe(true)
    expect(save).not.toHaveBeenCalled()
  })

  it('flushes only changes already registered by editor actions', async () => {
    const initialDocument = createEmptyTopologyDocument('Initial')
    const staleDocument = createEmptyTopologyDocument('Stale browser copy')
    const save = vi.fn()
    const { result } = renderHook(() =>
      useTopologyAutosave({ initialRevision: 3, initialDocument, save })
    )

    await act(async () => {
      await result.current.flush()
    })

    expect(staleDocument).not.toBe(initialDocument)
    expect(save).not.toHaveBeenCalled()
  })

  it('serializes saves and advances the expected revision', async () => {
    vi.useFakeTimers()
    const first = deferred<TeamLabTopologyDetail>()
    const second = deferred<TeamLabTopologyDetail>()
    const save = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result } = renderHook(() => useTopologyAutosave({ initialRevision: 4, delay: 20, save }))
    const firstDocument = createEmptyTopologyDocument('First')
    const secondDocument = createEmptyTopologyDocument('Second')

    act(() => result.current.schedule(firstDocument))
    await act(() => vi.advanceTimersByTimeAsync(20))
    expect(save).toHaveBeenCalledWith(firstDocument, 4)

    act(() => result.current.schedule(secondDocument))
    await act(async () => first.resolve(detail(5)))
    expect(save).toHaveBeenLastCalledWith(secondDocument, 5)

    await act(async () => second.resolve(detail(6)))
    expect(result.current.status).toBe('saved')
    vi.useRealTimers()
  })

  it('stops autosave and retains the local draft on revision conflict', async () => {
    const save = vi.fn().mockRejectedValue(
      new RuntimeApiError('conflict', { kind: 'http', status: 409, code: 'topology_revision_conflict' })
    )
    const { result } = renderHook(() => useTopologyAutosave({ initialRevision: 7, save }))
    const document = createEmptyTopologyDocument('Local')

    let saved = true
    await act(async () => {
      result.current.schedule(document)
      saved = await result.current.flush()
    })

    expect(saved).toBe(false)
    expect(result.current.status).toBe('conflict')
    expect(result.current.conflict).toEqual({ localDocument: document, expectedRevision: 7 })
  })
})
