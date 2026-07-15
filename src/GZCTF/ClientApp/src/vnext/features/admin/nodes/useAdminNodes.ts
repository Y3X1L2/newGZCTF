import useSWR from 'swr'
import { NodeCapability, NodeStatus, TeamLabTunnelStatus } from '@Api'
import { nodeAdminApi } from '../api'

export function useAdminNodes() {
  const result = useSWR('vnext:admin:nodes', () => nodeAdminApi.list(), {
    revalidateOnFocus: false,
    refreshInterval: 10_000,
  })
  return {
    nodes: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useAdminNode(id?: string) {
  const result = useSWR(id ? ['vnext:admin:node', id] : null, () => nodeAdminApi.detail(id as string), {
    revalidateOnFocus: false,
    refreshInterval: 10_000,
  })
  return {
    node: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useNodeResources(
  id: string | undefined,
  query: { type?: string; status?: string; page?: number; pageSize?: number }
) {
  const result = useSWR(
    id
      ? [
          'vnext:admin:node-resources',
          id,
          query.type ?? 'all',
          query.status ?? 'all',
          query.page ?? 1,
          query.pageSize ?? 12,
        ]
      : null,
    () => nodeAdminApi.resources(id as string, query),
    { revalidateOnFocus: false, refreshInterval: 10_000 }
  )
  return {
    resources: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function hasNodeCapability(capabilities: NodeCapability, capability: NodeCapability) {
  return (capabilities & capability) === capability
}

export function nodeStatusMeta(status: NodeStatus) {
  if (status === NodeStatus.Online) return { label: '在线', tone: 'success' as const }
  if (status === NodeStatus.Busy) return { label: '繁忙', tone: 'warning' as const }
  if (status === NodeStatus.Error) return { label: '异常', tone: 'danger' as const }
  if (status === NodeStatus.Offline) return { label: '离线', tone: 'neutral' as const }
  return { label: '未知', tone: 'neutral' as const }
}

export function tunnelStatusMeta(status: TeamLabTunnelStatus) {
  if (status === TeamLabTunnelStatus.Healthy) return { label: '隧道正常', tone: 'success' as const }
  if (status === TeamLabTunnelStatus.Probing) return { label: '检测中', tone: 'info' as const }
  if (status === TeamLabTunnelStatus.Error) return { label: '隧道异常', tone: 'danger' as const }
  if (status === TeamLabTunnelStatus.Disabled) return { label: '未启用', tone: 'neutral' as const }
  return { label: '未知', tone: 'neutral' as const }
}

export function formatLoad(value: number) {
  return `${Math.round(value * 100)}%`
}

export function formatHeartbeat(value: number | null) {
  if (!value) return '从未上报'
  const elapsed = Date.now() - value
  if (elapsed < 60_000) return `${Math.max(0, Math.round(elapsed / 1000))} 秒前`
  if (elapsed < 3_600_000) return `${Math.round(elapsed / 60_000)} 分钟前`
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}
