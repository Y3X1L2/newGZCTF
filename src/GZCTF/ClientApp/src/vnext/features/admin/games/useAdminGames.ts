import useSWR from 'swr'
import { adminGameKeys, gameAdminApi } from '../api'

export function useAdminGames(page: number, pageSize: number) {
  const result = useSWR(adminGameKeys.list({ page, pageSize }), () => gameAdminApi.list({ page, pageSize }), {
    keepPreviousData: true,
    revalidateOnFocus: false,
  })
  return {
    page: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useAdminGame(gameId: number) {
  const valid = Number.isInteger(gameId) && gameId > 0
  const result = useSWR(valid ? adminGameKeys.detail(gameId) : null, () => gameAdminApi.detail(gameId), {
    revalidateOnFocus: false,
  })
  return {
    game: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useAdminGameChallenges(gameId: number) {
  const valid = Number.isInteger(gameId) && gameId > 0
  const result = useSWR(valid ? adminGameKeys.challenges(gameId) : null, () => gameAdminApi.listChallenges(gameId), {
    revalidateOnFocus: false,
  })
  return {
    challenges: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useAdminGameChallenge(gameId: number, challengeId: number, poll = false) {
  const valid = Number.isInteger(gameId) && gameId > 0 && Number.isInteger(challengeId) && challengeId > 0
  const result = useSWR(
    valid ? adminGameKeys.challenge(gameId, challengeId) : null,
    () => gameAdminApi.challenge(gameId, challengeId),
    { revalidateOnFocus: false, refreshInterval: poll ? 3_000 : 0 }
  )
  return {
    challenge: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}
