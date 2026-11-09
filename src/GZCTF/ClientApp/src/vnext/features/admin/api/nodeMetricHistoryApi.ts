import { contractFailure, isNumber, isRecord } from './contractParsers'
import { runtimeJsonClient, type RuntimeJsonClient } from './runtimeJsonClient'

export type MetricHistoryRange = '1h' | '24h' | '7d'
export interface NodeMetricHistoryPoint {
  timestamp: number
  cpuLoad: number | null
  memoryLoad: number | null
  nodeCount: number
  sampleCount: number
}
export interface NodeMetricHistory {
  fromUtc: number
  toUtc: number
  intervalSeconds: number
  points: NodeMetricHistoryPoint[]
}

const load = (value: unknown) => value === null || (isNumber(value) && value >= 0 && value <= 1)
const count = (value: unknown) => isNumber(value) && Number.isInteger(value) && value >= 0
const isPoint = (value: unknown): value is NodeMetricHistoryPoint => isRecord(value) &&
  isNumber(value.timestamp) && load(value.cpuLoad) && load(value.memoryLoad) &&
  count(value.nodeCount) && count(value.sampleCount)

export function createNodeMetricHistoryApi(client: RuntimeJsonClient) {
  return {
    async history(range: MetricHistoryRange, nodeId?: string): Promise<NodeMetricHistory> {
      const value = await client.get('/api/v1/nodes/metrics/history', { range, nodeId })
      if (!isRecord(value) || !isNumber(value.fromUtc) || !isNumber(value.toUtc) ||
          value.toUtc <= value.fromUtc || !isNumber(value.intervalSeconds) || value.intervalSeconds <= 0 ||
          !Array.isArray(value.points) || value.points.length > 336 || !value.points.every(isPoint)) {
        return contractFailure('Node metric history', value)
      }
      const points = value.points
      if (points.some((point, index) => point.timestamp < (value.fromUtc as number) ||
          point.timestamp >= (value.toUtc as number) || (index > 0 && point.timestamp <= points[index - 1].timestamp))) {
        return contractFailure('Node metric history', value)
      }
      return { fromUtc: value.fromUtc, toUtc: value.toUtc, intervalSeconds: value.intervalSeconds, points }
    },
  }
}

export const nodeMetricHistoryApi = createNodeMetricHistoryApi(runtimeJsonClient)
