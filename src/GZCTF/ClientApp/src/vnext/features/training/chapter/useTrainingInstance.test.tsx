import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChallengeType, ContainerEntryStatus, EnvironmentType, TrainingCourseChallengeDetailModel } from '@Api'
import { trainingChapterApi } from './trainingChapterApi'
import { useTrainingInstance } from './useTrainingInstance'

function dockerChallenge(entry: string | null, status?: ContainerEntryStatus): TrainingCourseChallengeDetailModel {
  return {
    id: 7,
    type: ChallengeType.DynamicContainer,
    environment: EnvironmentType.Docker,
    context: {
      closeTime: entry ? Date.now() + 60_000 : null,
      instanceEntry: entry,
      instanceEntryStatus: status,
    },
  } as TrainingCourseChallengeDetailModel
}

describe('useTrainingInstance', () => {
  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('uses the same pending-to-ready gateway contract as game instances', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-20T08:00:00Z'))

    const initialChallenge = dockerChallenge(null)
    const runningChallenge = dockerChallenge('203.195.157.191:30001', ContainerEntryStatus.Ready)
    const refreshChallenge = vi.fn().mockResolvedValue(runningChallenge)
    const updateChallenge = vi.fn()

    vi.spyOn(trainingChapterApi, 'createInstance').mockResolvedValue({
      entry: undefined,
      expectStopAt: Date.now() + 60_000,
      entryStatus: ContainerEntryStatus.Pending,
    })

    const { result, rerender, unmount } = renderHook(
      ({ challenge }) =>
        useTrainingInstance({
          challenge,
          chapterId: 8,
          courseId: 3,
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
    expect(result.current.entry).toBe('203.195.157.191:30001')
    unmount()
  })

  it('does not poll indefinitely after gateway publication fails', async () => {
    vi.useFakeTimers()
    const failedChallenge = dockerChallenge(null, ContainerEntryStatus.Error)
    const refreshChallenge = vi.fn().mockResolvedValue(failedChallenge)

    const { result, unmount } = renderHook(() =>
      useTrainingInstance({
        challenge: failedChallenge,
        chapterId: 8,
        courseId: 3,
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

  it('transitions a queued course deployment to the server-projected failure state', async () => {
    vi.useFakeTimers()
    const initialChallenge = dockerChallenge(null)
    const failedChallenge = dockerChallenge(null, ContainerEntryStatus.Error)
    failedChallenge.context!.instanceEntryError = '题目镜像暂不可用，请联系管理员。'
    const refreshChallenge = vi.fn().mockResolvedValue(failedChallenge)

    vi.spyOn(trainingChapterApi, 'createInstance').mockResolvedValue({
      entry: undefined,
      expectStopAt: undefined,
      entryStatus: ContainerEntryStatus.Pending,
    })

    const { result, unmount } = renderHook(() =>
      useTrainingInstance({
        challenge: initialChallenge,
        chapterId: 8,
        courseId: 3,
        refreshChallenge,
        updateChallenge: vi.fn(),
      })
    )

    await act(async () => {
      await result.current.create()
      await vi.advanceTimersByTimeAsync(900)
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
})
