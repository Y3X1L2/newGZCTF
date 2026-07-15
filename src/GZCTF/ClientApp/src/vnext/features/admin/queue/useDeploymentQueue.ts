import useSWR from 'swr'
import { deploymentQueueAdminApi } from '../api'
import { activeDeploymentStatuses } from './deploymentQueuePresentation'

export interface DeploymentQueueQuery {
  status?: string
  page: number
  pageSize: number
  cursor?: string | null
}

export function deploymentQueueKey(query: DeploymentQueueQuery) {
  return ['vnext:admin:deployment-queue', query.status ?? 'all', query.page, query.pageSize, query.cursor ?? ''] as const
}

export function useDeploymentQueue(query: DeploymentQueueQuery) {
  const result = useSWR(deploymentQueueKey(query), () => deploymentQueueAdminApi.list(query), {
    keepPreviousData: true,
    revalidateOnFocus: true,
    refreshWhenHidden: false,
    refreshInterval: (latest) =>
      latest?.items.some((task) => activeDeploymentStatuses.has(task.statusKey.toLowerCase())) ? 3_000 : 15_000,
  })

  return {
    queue: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useDeploymentTask(id: string | null) {
  const result = useSWR(id ? ['vnext:admin:deployment-task', id] : null, () => deploymentQueueAdminApi.detail(id as string), {
    revalidateOnFocus: false,
  })
  return {
    task: result.data,
    error: result.error,
    isLoading: Boolean(id) && !result.data && !result.error,
    mutate: result.mutate,
  }
}
