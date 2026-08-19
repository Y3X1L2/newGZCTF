import { describe, expect, it } from 'vitest'
import { edgeLayer, nodeLayer, regionLayer, topologyLayers } from './topologyLayers'

describe('topologyLayers', () => {
  it('paints links above region containers and devices above links', () => {
    // This is the invariant the previous canvas lacked: with every object at
    // zIndex 0, React Flow's DOM order buried links under region rectangles.
    expect(regionLayer()).toBeLessThan(edgeLayer(false))
    expect(edgeLayer(false)).toBeLessThan(nodeLayer(false))
    expect(edgeLayer(true)).toBeLessThan(nodeLayer(false))
  })

  it('raises a selected object above its unselected siblings', () => {
    expect(edgeLayer(true)).toBeGreaterThan(edgeLayer(false))
    expect(nodeLayer(true)).toBeGreaterThan(nodeLayer(false))
  })

  it('keeps a selected link below every device so node hit areas keep winning', () => {
    expect(topologyLayers.edgeSelected).toBeLessThan(topologyLayers.node)
  })

  it('uses strictly increasing, positive layers', () => {
    const ordered = [
      topologyLayers.region,
      topologyLayers.edge,
      topologyLayers.edgeSelected,
      topologyLayers.node,
      topologyLayers.nodeSelected,
    ]
    expect(ordered[0]).toBeGreaterThan(0)
    for (let index = 1; index < ordered.length; index += 1) {
      expect(ordered[index]).toBeGreaterThan(ordered[index - 1])
    }
  })
})
