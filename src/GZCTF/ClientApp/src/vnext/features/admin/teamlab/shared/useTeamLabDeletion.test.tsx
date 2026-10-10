import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { teamLabAdminApi, teamLabRuntimeApi } from '../api'
import { RuntimeApiError } from '../../api/runtimeJsonClient'
import { useTeamLabDeletion } from './useTeamLabDeletion'

const refresh = vi.hoisted(() => vi.fn().mockResolvedValue([]))
vi.mock('swr', async importOriginal => ({ ...await importOriginal<typeof import('swr')>(), useSWRConfig: () => ({ mutate: refresh }) }))

describe('TeamLab record deletion', () => {
  beforeEach(() => { vi.restoreAllMocks(); refresh.mockClear() })

  it('keeps errors and the target for retry, without claiming deletion or refreshing', async () => {
    const failure = new RuntimeApiError('请先解除比赛引用。', { kind: 'http', status: 409, code: 'topology_in_use' })
    const remove = vi.spyOn(teamLabAdminApi, 'deleteTopology').mockRejectedValueOnce(failure).mockResolvedValueOnce(undefined)
    const deleted = vi.fn()
    const { result } = renderHook(() => useTeamLabDeletion(deleted))
    act(() => result.current.open({ kind: 'scene', id: 'scene-a', name: '企业域演练' }))
    await act(() => result.current.confirm())
    expect(result.current.target?.id).toBe('scene-a')
    expect(result.current.error).toBe(failure)
    expect(deleted).not.toHaveBeenCalled()
    expect(refresh).not.toHaveBeenCalled()
    await act(() => result.current.confirm())
    expect(remove).toHaveBeenCalledTimes(2)
    expect(result.current.target).toBeNull()
    expect(deleted).toHaveBeenCalledOnce()
  })

  it('locks closing and repeated confirmation while deleting and never invokes Destroy', async () => {
    let complete!: () => void
    const remove = vi.spyOn(teamLabRuntimeApi, 'deleteRuntimeRecord').mockReturnValue(new Promise<void>(resolve => { complete = resolve }))
    const destroy = vi.spyOn(teamLabRuntimeApi, 'destroyRuntime')
    const deleted = vi.fn()
    const { result } = renderHook(() => useTeamLabDeletion(deleted))
    act(() => result.current.open({ kind: 'runtime', id: 'runtime-a', name: '企业域演练' }))
    let request!: Promise<void>
    act(() => { request = result.current.confirm() })
    act(() => { result.current.close(); void result.current.confirm(); result.current.open({ kind: 'scene', id: 'wrong', name: 'Wrong' }) })
    expect(result.current.target?.id).toBe('runtime-a')
    expect(result.current.isDeleting).toBe(true)
    expect(remove).toHaveBeenCalledOnce()
    expect(destroy).not.toHaveBeenCalled()
    await act(async () => { complete(); await request })
    await waitFor(() => expect(deleted).toHaveBeenCalledOnce())
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('does not turn a successful delete into a failure when list refresh fails', async () => {
    vi.spyOn(teamLabRuntimeApi, 'deleteRuntimeRecord').mockResolvedValue(undefined)
    refresh.mockRejectedValueOnce(new Error('Read unavailable'))
    const deleted = vi.fn()
    const { result } = renderHook(() => useTeamLabDeletion(deleted))
    act(() => result.current.open({ kind: 'runtime', id: 'runtime-a', name: '企业域演练' }))
    await act(() => result.current.confirm())
    expect(result.current.error).toBeNull()
    expect(deleted).toHaveBeenCalledOnce()
  })
})
