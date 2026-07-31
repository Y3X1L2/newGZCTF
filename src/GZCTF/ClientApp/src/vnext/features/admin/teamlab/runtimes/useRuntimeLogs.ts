import useSWR from 'swr'
import { teamLabRuntimeApi, type TeamLabRuntimeStatus } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

const logLimit = 100

export function useRuntimeLogs(runtimeId: string, status: TeamLabRuntimeStatus | undefined) {
  const request = useSWR(
    runtimeId ? ['vnext:admin:teamlab:runtime-logs', runtimeId, logLimit] : null,
    () => teamLabRuntimeApi.listLogs(runtimeId, null, logLimit),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: () => runtimeRefreshInterval(status),
    }
  )

  return {
    logs: request.data?.items ?? [],
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
  }
}
