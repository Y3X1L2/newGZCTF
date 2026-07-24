import useSWR from 'swr'
import { adminLogApi, type AdminLogQuery } from '../api'

export function adminLogsKey(query: AdminLogQuery) {
  return [
    'vnext:admin:logs',
    query.level ?? 'All',
    query.count ?? 50,
    query.offset ?? 0,
    query.cursor ?? '',
    query.correlationId ?? '',
    query.workerNodeId ?? '',
    query.deploymentTicketId ?? '',
    query.eventCode ?? '',
    query.resourceType ?? '',
    query.resourceId ?? '',
  ] as const
}

export function useAdminLogs(query: AdminLogQuery) {
  const result = useSWR(adminLogsKey(query), () => adminLogApi.list(query), {
    keepPreviousData: true,
    revalidateOnFocus: true,
    refreshInterval: 0,
  })
  return {
    logs: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}
