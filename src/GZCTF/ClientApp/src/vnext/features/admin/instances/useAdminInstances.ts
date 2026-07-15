import useSWR from 'swr'
import type { NodeSummary } from '../api'
import { instanceAdminApi } from '../api'

export interface AdminInstanceQuery {
  status: 'active' | 'history'
  nodeId?: string
}

export function adminInstancesKey(nodes: NodeSummary[] | undefined, query: AdminInstanceQuery, nodesFailed: boolean) {
  if (query.status === 'history' && !query.nodeId) return null
  if (!nodes && !nodesFailed) return null
  return [
    'vnext:admin:global-instances',
    query.status,
    query.nodeId ?? 'all',
    nodesFailed ? 'legacy' : nodes?.map((node) => node.id).join(','),
  ] as const
}

export function useAdminInstances(
  nodes: NodeSummary[] | undefined,
  nodesError: unknown,
  query: AdminInstanceQuery
) {
  const key = adminInstancesKey(nodes, query, Boolean(nodesError))
  const result = useSWR(
    key,
    () => {
      if (nodesError) {
        if (query.status === 'history') throw nodesError
        return instanceAdminApi.legacyInventory()
      }
      return instanceAdminApi.inventory(nodes ?? [], query.status, query.nodeId)
    },
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshWhenHidden: false,
      refreshInterval: query.status === 'active' ? 10_000 : 0,
    }
  )

  return {
    inventory: result.data,
    error: result.error,
    isLoading: Boolean(key) && !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}
