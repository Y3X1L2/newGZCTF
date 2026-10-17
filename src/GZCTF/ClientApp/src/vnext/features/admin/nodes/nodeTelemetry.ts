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

export function appendTelemetrySample(samples: NodeTelemetrySample[], next: NodeTelemetrySample | null) {
  if (!next) return []
  const last = samples.at(-1)
  if (last?.coverage !== next.coverage) return [next]
  if (next.time - last.time > 60_000) return [next]
  if (next.time <= last.time) return samples
  return [...samples, next].filter((sample) => next.time - sample.time <= 10 * 60_000).slice(-60)
}

export function telemetryPoints(samples: NodeTelemetrySample[], key: 'cpu' | 'memory') {
  const start = samples[0]?.time ?? 0
  const duration = Math.max(10_000, (samples.at(-1)?.time ?? start) - start)
  return samples
    .map((sample) => `${20 + ((sample.time - start) / duration) * 680},${160 - sample[key] * 1.4}`)
    .join(' ')
}
