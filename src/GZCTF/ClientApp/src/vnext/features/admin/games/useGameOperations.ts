import useSWR from 'swr'
import { gameOperationsAdminApi, gameOperationsKeys } from '../api'

function validGameId(gameId: number) {
  return Number.isInteger(gameId) && gameId > 0
}

export function useAdminGamePhases(gameId: number) {
  const result = useSWR(
    validGameId(gameId) ? gameOperationsKeys.phases(gameId) : null,
    () => gameOperationsAdminApi.listPhases(gameId),
    { revalidateOnFocus: false }
  )
  return { phases: result.data, error: result.error, isLoading: result.isLoading, isRefreshing: result.isValidating, mutate: result.mutate }
}

export function useAdminGameDivisions(gameId: number) {
  const result = useSWR(
    validGameId(gameId) ? gameOperationsKeys.divisions(gameId) : null,
    () => gameOperationsAdminApi.listDivisions(gameId),
    { revalidateOnFocus: false }
  )
  return { divisions: result.data, error: result.error, isLoading: result.isLoading, isRefreshing: result.isValidating, mutate: result.mutate }
}

export function useAdminGameParticipations(gameId: number) {
  const result = useSWR(
    validGameId(gameId) ? gameOperationsKeys.participations(gameId) : null,
    () => gameOperationsAdminApi.listParticipations(gameId),
    { revalidateOnFocus: false }
  )
  return { participations: result.data, error: result.error, isLoading: result.isLoading, isRefreshing: result.isValidating, mutate: result.mutate }
}

export function useAdminGameNotices(gameId: number) {
  const result = useSWR(
    validGameId(gameId) ? gameOperationsKeys.notices(gameId) : null,
    () => gameOperationsAdminApi.listNotices(gameId),
    { revalidateOnFocus: false }
  )
  return { notices: result.data, error: result.error, isLoading: result.isLoading, isRefreshing: result.isValidating, mutate: result.mutate }
}
