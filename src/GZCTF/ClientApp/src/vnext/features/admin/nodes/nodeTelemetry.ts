import { NodeStatus } from '@Api'
import type { NodeSummary } from '../api'

export interface NodeTelemetrySample {
  time: number
  cpu: number
  memory: number
  coverage: string
}

export function liveTelemetryNodes(nodes: NodeSummary[], now = Date.now()) {
  return nodes.filter(
    (node) =>
      (node.status === NodeStatus.Online || node.status === NodeStatus.Busy) &&
      node.lastHeartbeat !== null &&
      now - node.lastHeartbeat < 60_000 &&
      now >= node.lastHeartbeat - 5_000 &&
      Number.isFinite(node.cpuLoad) &&
      node.cpuLoad >= 0 &&
      node.cpuLoad <= 1 &&
      Number.isFinite(node.memoryLoad) &&
      node.memoryLoad >= 0 &&
      node.memoryLoad <= 1
  )
}

export function telemetrySample(nodes: NodeSummary[], now = Date.now()): NodeTelemetrySample | null {
  const live = liveTelemetryNodes(nodes, now)
  if (!live.length) return null
  return {
    time: Math.max(...live.map((node) => node.lastHeartbeat as number)),
    cpu: (live.reduce((sum, node) => sum + node.cpuLoad, 0) / live.length) * 100,
    memory: (live.reduce((sum, node) => sum + node.memoryLoad, 0) / live.length) * 100,
    coverage: live
      .map((node) => node.id)
      .sort()
      .join(','),
  }
}
