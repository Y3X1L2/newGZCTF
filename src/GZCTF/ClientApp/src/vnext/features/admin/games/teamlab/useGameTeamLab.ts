import useSWR from 'swr'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { teamLabGameAdminApi, teamLabGameAdminKeys } from '../../api/teamlabGameAdminApi'
import { teamLabAdminApi, teamLabAdminKeys } from '../../teamlab/api/teamlabAdminApi'
import { rolloutPollInterval } from './teamLabGamePresentation'

const targetPageSize = 30

export function useGameTeamLab(gameId: number) {
  const valid = Number.isInteger(gameId) && gameId > 0
  const state = useSWR(
    valid ? teamLabGameAdminKeys.state(gameId) : null,
    () => teamLabGameAdminApi.state(gameId),
    {
      revalidateOnFocus: true,
      refreshInterval: (latest) => rolloutPollInterval(latest?.rollout?.status),
    }
  )
  const releases = useSWR(
    valid ? teamLabGameAdminKeys.releases(gameId) : null,
    () => teamLabGameAdminApi.releases(gameId),
    { revalidateOnFocus: true }
  )
  const topologyId = state.data?.binding?.topologyId
  const topology = useSWR(
    valid && topologyId ? teamLabAdminKeys.topology(topologyId) : null,
    () => teamLabAdminApi.getTopology(topologyId!),
    { revalidateOnFocus: false }
  )
  const cursor = useAdminCursorState(`${gameId}:${state.data?.rollout?.id ?? 'unbound'}`)
  const targets = useSWR(
    valid && state.data?.rollout
      ? teamLabGameAdminKeys.targets(gameId, cursor.cursor, targetPageSize)
      : null,
    () => teamLabGameAdminApi.targets(gameId, cursor.cursor ?? undefined, targetPageSize),
    {
      revalidateOnFocus: true,
      refreshInterval: cursor.cursor ? 0 : rolloutPollInterval(state.data?.rollout?.status),
    }
  )

  return {
    state: state.data,
    stateError: state.error,
    releases: releases.data ?? [],
    releasesError: releases.error,
    topology: topology.data,
    topologyError: topology.error,
    topologyLoading: Boolean(topologyId) && !topology.data && !topology.error,
    isLoading: (!state.data && !state.error) || (!releases.data && !releases.error),
    isRefreshing: state.isValidating || releases.isValidating || topology.isValidating,
    mutateState: state.mutate,
    targets: {
      page: targets.data,
      error: targets.error,
      isLoading: Boolean(state.data?.rollout) && !targets.data && !targets.error,
      isRefreshing: targets.isValidating && Boolean(targets.data),
      cursor,
      mutate: targets.mutate,
    },
  }
}
