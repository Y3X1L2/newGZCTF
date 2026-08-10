import { useCallback, useState } from 'react'
import useSWR from 'swr'
import { teamLabRuntimeApi, type TeamLabRuntimeStatus } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

const logLimit = 100

export function useRuntimeLogs(
  runtimeId: string,
  status: TeamLabRuntimeStatus | undefined,
  filters: { level: string; eventCode: string; keyword: string },
) {
  const filterKey = `${runtimeId}:${filters.level}:${filters.eventCode}:${filters.keyword}`
  const [cursorState, setCursorState] = useState(() => ({ filterKey, cursor: null as string | null }))
  const cursor = cursorState.filterKey === filterKey ? cursorState.cursor : null
  const request = useSWR(
    runtimeId
      ? ['vnext:admin:teamlab:runtime-logs', runtimeId, logLimit, cursor, filters.level, filters.eventCode, filters.keyword]
      : null,
    () =>
      teamLabRuntimeApi.listLogs(runtimeId, cursor, logLimit, {
        level: filters.level || undefined,
        eventCode: filters.eventCode || undefined,
        keyword: filters.keyword || undefined,
      }),
    {
      revalidateOnFocus: true,
      refreshInterval: () => runtimeRefreshInterval(status),
    }
  )

  const hasMore = Boolean(request.data?.nextCursor)
  const loadMore = useCallback(() => {
    if (request.data?.nextCursor) setCursorState({ filterKey, cursor: request.data.nextCursor })
  }, [filterKey, request.data?.nextCursor])

  return {
    logs: request.data?.items ?? [],
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    hasMore,
    loadMore,
  }
}
