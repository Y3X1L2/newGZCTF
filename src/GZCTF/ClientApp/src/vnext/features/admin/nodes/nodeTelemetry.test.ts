import { describe, expect, it } from 'vitest'
import { NodeStatus } from '@Api'
import type { NodeSummary } from '../api'
import { liveTelemetryNodes, telemetrySample } from './nodeTelemetry'

const now = 1_000_000
const node = (changes: Partial<NodeSummary> = {}) =>
  ({
    id: 'node-1',
    status: NodeStatus.Online,
    cpuLoad: 0.2,
    memoryLoad: 0.6,
    lastHeartbeat: now,
    ...changes,
  }) as NodeSummary

describe('node telemetry facts', () => {
  it('excludes stale, offline, missing and invalid readings rather than recording zero', () => {
    const nodes = [
      node(),
      node({ id: 'stale', lastHeartbeat: now - 60_001 }),
      node({ id: 'offline', status: NodeStatus.Offline }),
      node({ id: 'missing', lastHeartbeat: null }),
      node({ id: 'invalid', cpuLoad: NaN }),
    ]
    expect(liveTelemetryNodes(nodes, now).map((item) => item.id)).toEqual(['node-1'])
    expect(telemetrySample(nodes, now)).toMatchObject({ cpu: 20, memory: 60, time: now })
    expect(telemetrySample(nodes.slice(1), now)).toBeNull()
  })

})
