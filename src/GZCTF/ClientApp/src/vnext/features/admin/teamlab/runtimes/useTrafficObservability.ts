import useSWR from 'swr'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { teamLabRuntimeApi, teamLabRuntimeKeys, type TeamLabRuntimeStatus } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

const pageSize = 50

export function useTrafficObservability(runtimeId: string, status: TeamLabRuntimeStatus | undefined) {
  const flowsCursor = useAdminCursorState(`${runtimeId}:flows`)
  const pathsCursor = useAdminCursorState(`${runtimeId}:paths`)
  const liveInterval = runtimeRefreshInterval(status) > 0 ? 6_000 : 0

  const flows = useSWR(
    runtimeId
      ? [...teamLabRuntimeKeys.flows(runtimeId), flowsCursor.cursor ?? '', pageSize]
      : null,
    () => teamLabRuntimeApi.listFlows(runtimeId, flowsCursor.cursor ?? undefined, pageSize),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: flowsCursor.cursor ? 0 : liveInterval,
    }
  )
  const paths = useSWR(
    runtimeId
      ? [...teamLabRuntimeKeys.paths(runtimeId), pathsCursor.cursor ?? '', pageSize]
      : null,
    () => teamLabRuntimeApi.listPaths(runtimeId, pathsCursor.cursor ?? undefined, pageSize),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: pathsCursor.cursor ? 0 : liveInterval,
    }
  )

  return {
    flows: {
      page: flows.data,
      error: flows.error,
      isLoading: !flows.data && !flows.error,
      isRefreshing: flows.isValidating && Boolean(flows.data),
      cursor: flowsCursor,
      mutate: flows.mutate,
    },
    paths: {
      page: paths.data,
      error: paths.error,
      isLoading: !paths.data && !paths.error,
      isRefreshing: paths.isValidating && Boolean(paths.data),
      cursor: pathsCursor,
      mutate: paths.mutate,
    },
  }
}
