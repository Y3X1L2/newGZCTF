import useSWR from 'swr'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { teamLabRuntimeApi, teamLabRuntimeKeys, type TeamLabRuntimeStatus } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

const pageSize = 50

export type TrafficFlowFilters = { query: string; protocol: string; networkKey: string }
export type TrafficPathFilters = { query: string; protocol: string; confidence: string }

export function useTrafficObservability(
  runtimeId: string,
  status: TeamLabRuntimeStatus | undefined,
  flowFilters: TrafficFlowFilters,
  pathFilters: TrafficPathFilters,
) {
  const flowsCursor = useAdminCursorState(`${runtimeId}:flows:${JSON.stringify(flowFilters)}`)
  const pathsCursor = useAdminCursorState(`${runtimeId}:paths:${JSON.stringify(pathFilters)}`)
  const liveInterval = runtimeRefreshInterval(status) > 0 ? 6_000 : 0

  const flows = useSWR(
    runtimeId
      ? [...teamLabRuntimeKeys.flows(runtimeId), flowsCursor.cursor ?? '', pageSize, flowFilters.query, flowFilters.protocol, flowFilters.networkKey]
      : null,
    () => teamLabRuntimeApi.listFlows(runtimeId, flowsCursor.cursor ?? undefined, pageSize, {
      query: flowFilters.query || undefined,
      protocol: flowFilters.protocol || undefined,
      networkKey: flowFilters.networkKey || undefined,
    }),
    {
      revalidateOnFocus: true,
      refreshInterval: flowsCursor.cursor ? 0 : liveInterval,
    }
  )
  const paths = useSWR(
    runtimeId
      ? [...teamLabRuntimeKeys.paths(runtimeId), pathsCursor.cursor ?? '', pageSize, pathFilters.query, pathFilters.protocol, pathFilters.confidence]
      : null,
    () => teamLabRuntimeApi.listPaths(runtimeId, pathsCursor.cursor ?? undefined, pageSize, {
      query: pathFilters.query || undefined,
      protocol: pathFilters.protocol || undefined,
      confidence: pathFilters.confidence || undefined,
    }),
    {
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
