import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChallengeDetailModel, ChallengeType, ContainerStatus, EnvironmentType } from '@Api'
import { gamePlayerApi } from '../gamePlayerApi'
import { useGameInstance } from './useGameInstance'

function dockerChallenge(entry: string | null): ChallengeDetailModel {
  return {
    id: 19,
    type: ChallengeType.DynamicContainer,
    environment: EnvironmentType.Docker,
    context: {
      closeTime: entry ? Date.now() + 60_000 : null,
      instanceEntry: entry,
    },
  } as ChallengeDetailModel
}

describe('useGameInstance', () => {
  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('preserves a newly created marker until polling applies the public entry grace period', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-20T08:00:00Z'))

    const initialChallenge = dockerChallenge(null)
    const runningChallenge = dockerChallenge('203.195.157.191:30000')
    const refreshChallenge = vi.fn().mockResolvedValue(runningChallenge)

    vi.spyOn(gamePlayerApi, 'createInstance').mockResolvedValue({
      entry: undefined,
      expectStopAt: Date.now() + 60_000,
      status: ContainerStatus.Pending,
    })

    const { result, rerender, unmount } = renderHook(
      ({ challenge }) =>
        useGameInstance({
          challenge,
          gameId: 23,
          refreshChallenge,
          updateChallenge: vi.fn(),
        }),
      { initialProps: { challenge: initialChallenge } }
    )

    await act(async () => {
      await result.current.create()
    })
    expect(result.current.phase).toBe('provisioning')

    rerender({ challenge: runningChallenge })
    expect(result.current.phase).toBe('provisioning')

    await act(async () => {
      await result.current.refresh()
    })

    expect(result.current.phase).toBe('running')
    expect(result.current.entryAvailableAt).toBe(Date.now() + 8_000)
    unmount()
  })
})
