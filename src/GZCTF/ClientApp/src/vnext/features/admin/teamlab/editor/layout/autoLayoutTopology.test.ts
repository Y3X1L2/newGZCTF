import { describe, expect, it } from 'vitest'
import { createLargeTopologyFixture } from '../../testing/largeTopologyFixture'
import { autoLayoutTopology } from './autoLayoutTopology'

describe('autoLayoutTopology', () => {
  it('produces a deterministic non-overlapping layout for the large topology limit', () => {
    const source = createLargeTopologyFixture()
    const first = autoLayoutTopology(source)
    const second = autoLayoutTopology(source)
    const nodes = Object.values(first.nodes)

    expect(first.nodes).toEqual(second.nodes)
    expect(first.connections).toBe(source.connections)
    expect(new Set(nodes.map((node) => `${node.position.x}:${node.position.y}`)).size).toBe(nodes.length)

    for (let left = 0; left < nodes.length; left += 1) {
      for (let right = left + 1; right < nodes.length; right += 1) {
        const a = nodes[left].position
        const b = nodes[right].position
        const overlaps = a.x < b.x + 208 && a.x + 208 > b.x && a.y < b.y + 102 && a.y + 102 > b.y
        expect(overlaps, `${nodes[left].key} overlaps ${nodes[right].key}`).toBe(false)
      }
    }
  })
})
