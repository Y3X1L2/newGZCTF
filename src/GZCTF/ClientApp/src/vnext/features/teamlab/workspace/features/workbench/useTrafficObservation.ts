import { useQueries, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useWorkspaceApi, type Environment, type PacketFilters } from '../../api/client'

export type { PacketFilters } from '../../api/client'
export function useTrafficObservation(
  environment: Environment,
  options: {
    range: number
    paused: boolean
    filters: PacketFilters
    packets: boolean
    history: boolean
    graph: boolean
  }
) {
  const api = useWorkspaceApi()
  const id = environment.id
  const key = JSON.stringify(options.filters)
  const [paging, setPaging] = useState<{ key: string; cursors: string[] }>({
    key: '',
    cursors: [],
  })
  const cursors = paging.key === key ? paging.cursors : []
  const latest = useQuery({
    queryKey: ['environment-packets', id, options.filters],
    queryFn: ({ signal }) => api.environmentPackets(id, options.filters, signal),
    enabled: options.packets,
    subscribed: options.packets,
    refetchInterval: options.paused ? false : 3000,
  })
  const earlier = useQueries({
    queries: cursors.map((cursor) => ({
      queryKey: ['environment-packets', id, options.filters, cursor],
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        api.environmentPackets(id, { ...options.filters, cursor }, signal),
      staleTime: Infinity,
      enabled: options.packets,
    })),
    subscribed: options.packets,
  })
  const traffic = useQuery({
    queryKey: ['traffic', id],
    queryFn: ({ signal }) => api.traffic(id, signal),
    enabled: options.graph,
    subscribed: options.graph,
    refetchInterval: options.paused ? false : 5000,
  })
  const history = useQuery({
    queryKey: ['traffic-timeline', id, options.range],
    queryFn: ({ signal }) =>
      api.environmentMetrics(id, options.range, signal, undefined, false, ['receive', 'transmit']),
    enabled: options.history,
    subscribed: options.history,
    refetchInterval: options.paused ? false : 10000,
  })
  const packets = [
    ...new Map(
      [...earlier.flatMap((page) => page.data?.packets ?? []), ...(latest.data?.packets ?? [])].map((packet) => [
        packet.id,
        packet,
      ])
    ).values(),
  ].sort((a, b) => Date.parse(a.time) - Date.parse(b.time) || a.number - b.number)
  const cursor = earlier.length ? earlier.at(-1)?.data?.nextCursor : latest.data?.nextCursor
  const errors = [
    ...(options.packets ? [latest, ...earlier] : []),
    ...(options.graph ? [traffic] : []),
    ...(options.history ? [history] : []),
  ].flatMap((query) => (query.error ? [query.error.message] : []))
  if (options.graph) errors.push(...Object.values(traffic.data?.errors ?? {}))
  for (const page of options.packets ? [latest, ...earlier] : []) errors.push(...Object.values(page.data?.errors ?? {}))
  return {
    packets,
    traffic,
    history,
    retention: latest.dataUpdatedAt >= traffic.dataUpdatedAt ? latest.data?.retention : traffic.data?.retention,
    loading: options.packets && latest.isPending,
    fetching: options.packets && (latest.isFetching || earlier.some((page) => page.isFetching)),
    errors: [...new Set(errors)],
    loadOlder: cursor ? () => setPaging({ key, cursors: [...cursors, cursor] }) : undefined,
    resetPaging: () => setPaging({ key, cursors: [] }),
  }
}
