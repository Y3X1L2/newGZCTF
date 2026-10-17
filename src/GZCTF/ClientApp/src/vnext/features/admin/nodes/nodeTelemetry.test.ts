import { describe, expect, it } from 'vitest'
import { NodeStatus } from '@Api'
import type { NodeSummary } from '../api'
import { appendTelemetrySample, liveTelemetryNodes, telemetryPoints, telemetrySample } from './nodeTelemetry'

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

  it('does not fabricate samples on duplicate heartbeats and resets when coverage changes', () => {
    const first = telemetrySample([node()], now)!
    const samples = appendTelemetrySample([], first)
    expect(appendTelemetrySample(samples, { ...first })).toBe(samples)
    expect(appendTelemetrySample(samples, { ...first, coverage: 'node-2', time: now + 10_000 })).toHaveLength(1)
    expect(appendTelemetrySample(samples, null)).toEqual([])
  })

  it('uses elapsed timestamps for chart positions and bounds session retention', () => {
    const samples = [0, 10_000, 40_000].map((offset) => ({
      time: now + offset,
      cpu: 50,
      memory: 25,
      coverage: 'node-1',
    }))
    expect(telemetryPoints(samples, 'cpu')).toBe('20,90 190,90 700,90')
    expect(appendTelemetrySample(samples, { ...samples[0], time: now + 700_000 })).toHaveLength(1)
  })
})
