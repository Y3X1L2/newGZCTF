import { useState } from 'react'
import useSWR from 'swr'
import { nodeMetricHistoryApi, type MetricHistoryRange } from '../api/nodeMetricHistoryApi'

export function useNodeMetricHistory(nodeId?: string) {
  const [range, setRange] = useState<MetricHistoryRange>('24h')
  const request = useSWR(
    ['vnext:admin:node-history', nodeId ?? 'all', range],
    () => nodeMetricHistoryApi.history(range, nodeId),
    { refreshInterval: 60_000, revalidateOnFocus: true, keepPreviousData: false }
  )
  return { ...request, range, setRange }
}
