import useSWR from 'swr'
import { useEffect } from 'react'
import { teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

export function useTeamLabRuntime(runtimeId: string) {
  const request = useSWR(
    runtimeId ? teamLabRuntimeKeys.runtime(runtimeId) : null,
    () => teamLabRuntimeApi.getRuntime(runtimeId),
    { keepPreviousData: true, revalidateOnFocus: true }
  )
  const status = useSWR(
    runtimeId ? teamLabRuntimeKeys.runtimeStatus(runtimeId) : null,
    () => teamLabRuntimeApi.getRuntimeStatus(runtimeId),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: (latest) => latest?.queueStatus && ['pending', 'scheduling', 'scheduled', 'running'].includes(latest.queueStatus)
        ? 2500 : runtimeRefreshInterval(latest?.status),
    }
  )

  useEffect(() => {
    if (!request.data || !status.data) return
    if (request.data.generation !== status.data.generation ||
        request.data.status !== status.data.status ||
        request.data.updatedAt !== status.data.updatedAt ||
        request.data.queueStatus !== status.data.queueStatus)
      void request.mutate()
  }, [request, status.data])

  return {
    runtime: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: (request.isValidating || status.isValidating) && Boolean(request.data),
    mutate: request.mutate,
  }
}
