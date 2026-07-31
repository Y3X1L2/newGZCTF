import useSWR from 'swr'
import { teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

export function useTeamLabRuntime(runtimeId: string) {
  const request = useSWR(
    runtimeId ? teamLabRuntimeKeys.runtime(runtimeId) : null,
    () => teamLabRuntimeApi.getRuntime(runtimeId),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: (latest) => runtimeRefreshInterval(latest?.status),
    }
  )

  return {
    runtime: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
  }
}
