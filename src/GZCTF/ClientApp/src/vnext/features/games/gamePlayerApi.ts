import api, { AnswerResult, GameJoinModel, VmStatusResponse } from '@Api'

const swrOptions = { revalidateOnFocus: false } as const

export function usePlayerGame(gameId: number, enabled: boolean) {
  return api.game.useGameGame(gameId, swrOptions, enabled)
}

export function usePlayerTeams(enabled: boolean) {
  return api.team.useTeamGetTeamsInfo(swrOptions, enabled)
}

export function useGameNotices(gameId: number, count: number, enabled: boolean) {
  return api.game.useGameNotices(gameId, { count, skip: 0 }, swrOptions, enabled)
}

export function useGameJoinCheck(gameId: number, enabled: boolean) {
  return api.game.useGameGetGameJoinCheckInfo(gameId, swrOptions, enabled)
}

export function useGameChallengeCatalog(gameId: number, refreshInterval: number, enabled = true) {
  return api.game.useGameChallengesWithTeamInfo(
    gameId,
    { ...swrOptions, shouldRetryOnError: false, refreshInterval },
    enabled
  )
}

export function useGameChallenge(gameId: number, challengeId: number, refreshInterval: number, enabled: boolean) {
  return api.game.useGameGetChallenge(
    gameId,
    challengeId,
    { ...swrOptions, keepPreviousData: false, refreshInterval },
    enabled
  )
}

export function useGameScoreboard(gameId: number, refreshInterval: number) {
  return api.game.useGameScoreboard(gameId, { ...swrOptions, refreshInterval })
}

export const gamePlayerApi = {
  async join(gameId: number, data: GameJoinModel) {
    await api.game.gameJoinGame(gameId, data)
  },
  async leave(gameId: number) {
    await api.game.gameLeaveGame(gameId)
  },
  async createInstance(gameId: number, challengeId: number) {
    const response = await api.game.gameCreateContainer(gameId, challengeId)
    return response.data
  },
  async extendInstance(gameId: number, challengeId: number) {
    const response = await api.game.gameExtendContainerLifetime(gameId, challengeId)
    return response.data
  },
  async destroyContainer(gameId: number, challengeId: number) {
    await api.game.gameDeleteContainer(gameId, challengeId)
  },
  async destroyVm(gameId: number, challengeId: number) {
    await api.game.gameDestroyVm(gameId, challengeId)
  },
  async vmStatus(gameId: number, challengeId: number) {
    const response = await fetch(`/api/Game/${gameId}/Vm/${challengeId}`, { credentials: 'include' })
    if (response.status === 404) return null
    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { title?: string } | null
      throw new Error(body?.title || `Windows 靶机状态读取失败 (${response.status})`)
    }
    return (await response.json()) as VmStatusResponse
  },
  async submitFlag(gameId: number, challengeId: number, flag: string, flagId: number | null) {
    const response = await api.game.gameSubmit(gameId, challengeId, { flag, ...(flagId ? { flagId } : {}) })
    return response.data
  },
  async submissionStatus(gameId: number, challengeId: number, submitId: number) {
    const response = await api.game.gameStatus(gameId, challengeId, submitId)
    return response.data ?? AnswerResult.NotFound
  },
  async refreshAcceptedSubmission(gameId: number, challengeId: number) {
    await Promise.all([
      api.game.mutateGameChallengesWithTeamInfo(gameId),
      api.game.mutateGameGetChallenge(gameId, challengeId),
      api.game.mutateGameScoreboard(gameId),
    ])
  },
}
