import { describe, expect, it, vi } from 'vitest'
import { createNodeMetricHistoryApi } from './nodeMetricHistoryApi'
import { runtimeJsonClient } from './runtimeJsonClient'

const point = { timestamp: 60_000, cpuLoad: 0.2, memoryLoad: 0.5, nodeCount: 2, sampleCount: 12 }
const response = { fromUtc: 60_000, toUtc: 3_660_000, intervalSeconds: 60,
  points: Array.from({ length: 60 }, (_, i) => i === 0 ? point :
    { timestamp: 60_000 + i * 60_000, cpuLoad: null, memoryLoad: null, nodeCount: 0, sampleCount: 0 }) }

describe('historical node metrics adapter', () => {
  it('requests server history for the selected scope and preserves missing readings', async () => {
    const get = vi.fn().mockResolvedValue(response)
    const result = await createNodeMetricHistoryApi({ ...runtimeJsonClient, get }).history('1h', 'node-1')
    expect(get).toHaveBeenCalledWith('/api/v1/nodes/metrics/history', { range: '1h', nodeId: 'node-1' })
    expect(result.points[1].cpuLoad).toBeNull()
    expect(result.points[0].timestamp).toBe(60_000)
  })
  it.each([
    { ...response, points: [{ ...point, cpuLoad: 20 }] },
    { ...response, points: [point, point] },
    { ...response, points: [{ ...point, timestamp: 180_000 }] },
    { ...response, points: Array(337).fill(point) },
    { ...response, points: response.points.filter((_, index) => index !== 2) },
    { ...response, intervalSeconds: 1 },
    { ...response, fromUtc: 8640000000000001 },
    '<html>not an API</html>',
  ])('rejects malformed history rather than plotting it', async value => {
    const api = createNodeMetricHistoryApi({ ...runtimeJsonClient, get: vi.fn().mockResolvedValue(value) })
    await expect(api.history('1h')).rejects.toMatchObject({ kind: 'contract' })
  })
})
