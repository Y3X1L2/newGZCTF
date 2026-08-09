import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChallengeDetailModel, ChallengeType, ContainerEntryStatus, EnvironmentType } from '@Api'
import { gamePlayerApi } from '../gamePlayerApi'
import { useGameInstance } from './useGameInstance'

function dockerChallenge(entry: string | null, status?: ContainerEntryStatus): ChallengeDetailModel {
  return {
    id: 19,
    type: ChallengeType.DynamicContainer,
    environment: EnvironmentType.Docker,
    context: {
      closeTime: entry ? Date.now() + 60_000 : null,
      instanceEntry: entry,
      instanceEntryStatus: status,
    },
  } as ChallengeDetailModel
}

function windowsChallenge(): ChallengeDetailModel {
  return {
    id: 40,
    type: ChallengeType.DynamicContainer,
    environment: EnvironmentType.WindowsVM,
    context: {},
  } as ChallengeDetailModel
}

describe('useGameInstance', () => {
  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('keeps provisioning until the server confirms the public entry', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-20T08:00:00Z'))

    const initialChallenge = dockerChallenge(null)
    const runningChallenge = dockerChallenge('203.195.157.191:30000', ContainerEntryStatus.Ready)
    const refreshChallenge = vi.fn().mockResolvedValue(runningChallenge)
    const updateChallenge = vi.fn()

    vi.spyOn(gamePlayerApi, 'createInstance').mockResolvedValue({
      entry: undefined,
      expectStopAt: Date.now() + 60_000,
      entryStatus: ContainerEntryStatus.Pending,
    })

    const { result, rerender, unmount } = renderHook(
      ({ challenge }) =>
        useGameInstance({
          challenge,
          gameId: 23,
          refreshChallenge,
          updateChallenge,
        }),
      { initialProps: { challenge: initialChallenge } }
    )

    await act(async () => {
      await result.current.create()
    })
    expect(result.current.phase).toBe('provisioning')

    await act(async () => {
      await result.current.refresh()
    })

    expect(result.current.phase).toBe('running')
    expect(updateChallenge).toHaveBeenLastCalledWith(runningChallenge)
    rerender({ challenge: runningChallenge })
    expect(result.current.entryStatus).toBe(ContainerEntryStatus.Ready)
    expect(result.current.entry).toBe('203.195.157.191:30000')
    unmount()
  })

  it('does not poll indefinitely after gateway publication fails', async () => {
    vi.useFakeTimers()
    const failedChallenge = dockerChallenge(null, ContainerEntryStatus.Error)
    const refreshChallenge = vi.fn().mockResolvedValue(failedChallenge)

    const { result, unmount } = renderHook(() =>
      useGameInstance({
        challenge: failedChallenge,
        gameId: 23,
        refreshChallenge,
        updateChallenge: vi.fn(),
      })
    )

    expect(result.current.phase).toBe('failed')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000)
    })
    expect(refreshChallenge).not.toHaveBeenCalled()
    unmount()
  })

  it('transitions a queued deployment to the server-projected failure state', async () => {
    vi.useFakeTimers()
    const initialChallenge = dockerChallenge(null)
    const failedChallenge = dockerChallenge(null, ContainerEntryStatus.Error)
    failedChallenge.context!.instanceEntryError = '题目镜像暂不可用，请联系管理员。'
    const refreshChallenge = vi.fn().mockResolvedValue(failedChallenge)

    vi.spyOn(gamePlayerApi, 'createInstance').mockResolvedValue({
      entry: undefined,
      expectStopAt: undefined,
      entryStatus: ContainerEntryStatus.Pending,
    })

    const { result, unmount } = renderHook(() =>
      useGameInstance({
        challenge: initialChallenge,
        gameId: 23,
        refreshChallenge,
        updateChallenge: vi.fn(),
      })
    )

    await act(async () => {
      await result.current.create()
      await vi.advanceTimersByTimeAsync(1_200)
    })

    expect(result.current.phase).toBe('failed')
    expect(result.current.error).toBe('题目镜像暂不可用，请联系管理员。')
    expect(refreshChallenge).toHaveBeenCalledOnce()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000)
    })
    expect(refreshChallenge).toHaveBeenCalledOnce()
    unmount()
  })

  it('surfaces the deployment queue error for a failed Windows VM', async () => {
    const queueError = 'Windows image has no enabled fixed-account RDP configuration.'
    vi.spyOn(gamePlayerApi, 'vmStatus').mockResolvedValue({
      vmInstanceId: '2a44adae-da82-4b52-98aa-cbf436b448bc',
      status: 'Error',
      stage: 'error',
      stageMessage: queueError,
      queue: { errorMessage: queueError },
      createdAt: Date.now(),
    })

    const { result, unmount } = renderHook(() =>
      useGameInstance({
        challenge: windowsChallenge(),
        gameId: 23,
        refreshChallenge: vi.fn(),
        updateChallenge: vi.fn(),
      })
    )

    await act(async () => {
      await result.current.refresh()
    })

    expect(result.current.phase).toBe('failed')
    expect(result.current.error).toBe(queueError)
    unmount()
  })
})
