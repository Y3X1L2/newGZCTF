import useSWR from 'swr'
import { teamLabPlayerApi, teamLabPlayerKeys } from './api'

const terminalStatuses = new Set(['failed', 'destroyed'])

export function usePlayerTeamLab(gameId: number) {
  const valid = Number.isInteger(gameId) && gameId > 0
  const request = useSWR(
    valid ? teamLabPlayerKeys.workspace(gameId) : null,
    () => teamLabPlayerApi.getWorkspace(gameId),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: (workspace) => workspace && !terminalStatuses.has(workspace.status) ? 3_000 : 0,
    }
  )

  return {
    workspace: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
  }
}
