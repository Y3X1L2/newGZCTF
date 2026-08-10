import { describe, expect, it } from 'vitest'
import { createLargeTopologyFixture } from '../../testing/largeTopologyFixture'
import { autoLayoutTopology } from './autoLayoutTopology'

function regionContains(
  region: { x: number; y: number; width?: number | null; height?: number | null },
  node: { position: { x: number; y: number; width?: number | null; height?: number | null } }
) {
  const width = node.position.width ?? 208
  const height = node.position.height ?? 102
  return node.position.x >= region.x &&
    node.position.y >= region.y &&
    node.position.x + width <= region.x + (region.width ?? 0) &&
    node.position.y + height <= region.y + (region.height ?? 0)
}

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

    const regions = Object.entries(first.networkLayouts)
    for (let left = 0; left < regions.length; left += 1) {
      for (let right = left + 1; right < regions.length; right += 1) {
        const [leftKey, a] = regions[left]
        const [rightKey, b] = regions[right]
        const overlaps = a.x < b.x + (b.width ?? 0) && a.x + (a.width ?? 0) > b.x && a.y < b.y + (b.height ?? 0) && a.y + (a.height ?? 0) > b.y
        expect(overlaps, `${leftKey} overlaps ${rightKey}`).toBe(false)
      }
    }
  })

  it('packs disconnected network regions into a compact group instead of one rank per network', () => {
    const source = createLargeTopologyFixture()
    const withoutRoutes = {
      ...source,
      connections: Object.fromEntries(Object.entries(source.connections).filter(([, connection]) => connection.type !== 'route')),
    }
    const layout = autoLayoutTopology(withoutRoutes)
    const entry = layout.networkLayouts['network-00']
    const firstIsolated = layout.networkLayouts['network-01']
    const secondIsolated = layout.networkLayouts['network-02']
    expect(firstIsolated.x).toBeGreaterThan(entry.x + (entry.width ?? 0))
    expect(secondIsolated.x).toBeGreaterThan(firstIsolated.x)
    expect(Math.abs(secondIsolated.y - firstIsolated.y)).toBeLessThanOrEqual(4)
  })

  it('places an entry network at the centre and direct route branches around it', () => {
    const source = createLargeTopologyFixture()
    const branched = {
      ...source,
      connections: {
        ...source.connections,
        'route-entry-04': {
          type: 'route' as const,
          key: 'route-entry-04',
          fromSwitchKey: 'switch-00',
          toSwitchKey: 'switch-04',
          viaNodeKey: 'router-01',
          direction: 'bidirectional' as const,
        },
        'route-entry-08': {
          type: 'route' as const,
          key: 'route-entry-08',
          fromSwitchKey: 'switch-00',
          toSwitchKey: 'switch-08',
          viaNodeKey: 'router-02',
          direction: 'bidirectional' as const,
        },
      },
    }
    const layout = autoLayoutTopology(branched)
    const centre = layout.networkLayouts['network-00']
    const centreX = centre.x + (centre.width ?? 0) / 2
    const centreY = centre.y + (centre.height ?? 0) / 2
    expect(Math.abs(centreX)).toBeLessThanOrEqual(4)
    expect(Math.abs(centreY)).toBeLessThanOrEqual(4)

    const directions = ['network-01', 'network-04', 'network-08'].map((networkKey) => {
      const region = layout.networkLayouts[networkKey]
      return `${Math.sign(region.x + (region.width ?? 0) / 2 - centreX)}:${Math.sign(region.y + (region.height ?? 0) / 2 - centreY)}`
    })
    expect(new Set(directions).size).toBe(3)

    const chainCentres = ['network-01', 'network-02', 'network-03'].map((networkKey) => {
      const region = layout.networkLayouts[networkKey]
      return {
        x: region.x + (region.width ?? 0) / 2,
        y: region.y + (region.height ?? 0) / 2,
      }
    })
    // A routed chain keeps increasing its distance from the entry, while later
    // hops fan out from the root ray so a deep branch is not rendered as one
    // unreadable vertical or horizontal line.
    expect(chainCentres[0].y).toBeLessThan(centreY)
    expect(Math.hypot(chainCentres[1].x - centreX, chainCentres[1].y - centreY))
      .toBeGreaterThan(Math.hypot(chainCentres[0].x - centreX, chainCentres[0].y - centreY))
    expect(Math.hypot(chainCentres[2].x - centreX, chainCentres[2].y - centreY))
      .toBeGreaterThan(Math.hypot(chainCentres[1].x - centreX, chainCentres[1].y - centreY))
    expect(chainCentres.some((centre) => Math.abs(centre.x - centreX) > 4)).toBe(true)
  })

  it('rebuilds oversized manual regions into a compact overview when requested', () => {
    const source = createLargeTopologyFixture()
    const oversized = {
      ...source,
      networkLayouts: Object.fromEntries(
        Object.entries(source.networkLayouts).map(([key, layout]) => [
          key,
          { ...layout, width: 4000, height: 3000 },
        ])
      ),
    }

    const layout = autoLayoutTopology(oversized)

    for (const region of Object.values(layout.networkLayouts)) {
      expect(region.width).toBeLessThan(4000)
      expect(region.height).toBeLessThan(3000)
    }
  })

  it('sizes a region from its actual members without overlap or clipping', () => {
    const source = createLargeTopologyFixture()
    const layout = autoLayoutTopology({
      ...source,
      nodes: {
        ...source.nodes,
        'asset-000': { ...source.nodes['asset-000'], position: { ...source.nodes['asset-000'].position, width: 400, height: 240 } },
        'asset-001': { ...source.nodes['asset-001'], position: { ...source.nodes['asset-001'].position, width: 360, height: 180 } },
      },
    })
    const region = layout.networkLayouts['network-00']
    const first = layout.nodes['asset-000']
    const second = layout.nodes['asset-001']
    expect(regionContains(region, first)).toBe(true)
    expect(regionContains(region, second)).toBe(true)
    const firstWidth = first.position.width ?? 208
    expect(first.position.x + firstWidth).toBeLessThanOrEqual(second.position.x)
  })

  it('keeps a finite grid for incomplete drafts without a switch', () => {
    const source = createLargeTopologyFixture()
    const layout = autoLayoutTopology({
      ...source,
      nodes: {
        'asset-000': source.nodes['asset-000'],
        'asset-001': source.nodes['asset-001'],
      },
      connections: {},
      networkLayouts: {},
    })
    for (const node of Object.values(layout.nodes)) {
      expect(Number.isFinite(node.position.x)).toBe(true)
      expect(Number.isFinite(node.position.y)).toBe(true)
    }
  })

  it('uses actual node dimensions in incomplete-draft grids', () => {
    const source = createLargeTopologyFixture()
    const layout = autoLayoutTopology({
      ...source,
      nodes: {
        'asset-000': { ...source.nodes['asset-000'], position: { ...source.nodes['asset-000'].position, width: 400, height: 240 } },
        'asset-001': source.nodes['asset-001'],
      },
      connections: {},
      networkLayouts: {},
    })
    const first = layout.nodes['asset-000'].position
    const second = layout.nodes['asset-001'].position
    expect(first.x + (first.width ?? 208) <= second.x || second.x + (second.width ?? 208) <= first.x ||
      first.y + (first.height ?? 102) <= second.y || second.y + (second.height ?? 102) <= first.y).toBe(true)
  })

  it('keeps multi-network assets at the centre of all attached regions', () => {
    const source = createLargeTopologyFixture()
    const layout = autoLayoutTopology({
      ...source,
      connections: {
        ...source.connections,
        'asset-000-network-01': {
          type: 'membership', key: 'asset-000-network-01', nodeKey: 'asset-000', switchKey: 'switch-01',
          hostOffset: 40, primary: false, orderIndex: 1,
        },
        'asset-000-network-02': {
          type: 'membership', key: 'asset-000-network-02', nodeKey: 'asset-000', switchKey: 'switch-02',
          hostOffset: 40, primary: false, orderIndex: 2,
        },
      },
    })
    const regions = ['network-00', 'network-01', 'network-02'].map((key) => layout.networkLayouts[key])
    const centre = regions.reduce((total, region) => ({
      x: total.x + region.x + (region.width ?? 0) / 2,
      y: total.y + region.y + (region.height ?? 0) / 2,
    }), { x: 0, y: 0 })
    const asset = layout.nodes['asset-000'].position
    const assetCentre = { x: asset.x + (asset.width ?? 208) / 2, y: asset.y + (asset.height ?? 102) / 2 }
    expect(Math.abs(assetCentre.x - centre.x / regions.length)).toBeLessThanOrEqual(256)
    expect(Math.abs(assetCentre.y - centre.y / regions.length)).toBeLessThanOrEqual(256)
  })
})
