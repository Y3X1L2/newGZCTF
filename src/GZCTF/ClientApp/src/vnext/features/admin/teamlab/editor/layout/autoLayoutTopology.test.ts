import { describe, expect, it } from 'vitest'
import {
  DIAGRAM_TARGET_ASPECT,
  MAX_REGION_HEIGHT,
  MAX_REGION_WIDTH,
  nodeSize,
} from '../../model/topologyGeometry'
import type { TopologyDocument, TopologyPosition } from '../../model/topologyDocument'
import { createLargeTopologyFixture } from '../../testing/largeTopologyFixture'
import { autoLayoutTopology } from './autoLayoutTopology'
import { buildTopologyGraph } from './topologyGraph'

interface Box {
  x: number
  y: number
  width: number
  height: number
}

const nodeBox = (document: TopologyDocument, key: string): Box => {
  const node = document.nodes[key]
  return { x: node.position.x, y: node.position.y, ...nodeSize(node) }
}

const regionBox = (layout: TopologyPosition): Box => ({
  x: layout.x,
  y: layout.y,
  width: layout.width ?? 0,
  height: layout.height ?? 0,
})

const overlaps = (a: Box, b: Box) =>
  a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y

const contains = (outer: Box, inner: Box) =>
  inner.x >= outer.x &&
  inner.y >= outer.y &&
  inner.x + inner.width <= outer.x + outer.width &&
  inner.y + inner.height <= outer.y + outer.height

const centreOf = (box: Box) => ({ x: box.x + box.width / 2, y: box.y + box.height / 2 })

function diagramBounds(document: TopologyDocument) {
  const boxes = [
    ...Object.keys(document.nodes).map((key) => nodeBox(document, key)),
    ...Object.values(document.networkLayouts).map(regionBox),
  ]
  const minX = Math.min(...boxes.map((box) => box.x))
  const minY = Math.min(...boxes.map((box) => box.y))
  const maxX = Math.max(...boxes.map((box) => box.x + box.width))
  const maxY = Math.max(...boxes.map((box) => box.y + box.height))
  return { width: maxX - minX, height: maxY - minY }
}

describe('autoLayoutTopology', () => {
  it('is deterministic and leaves connections untouched', () => {
    const source = createLargeTopologyFixture()
    const first = autoLayoutTopology(source)
    const second = autoLayoutTopology(source)
    expect(first.nodes).toEqual(second.nodes)
    expect(first.networkLayouts).toEqual(second.networkLayouts)
    expect(first.connections).toBe(source.connections)
  })

  it('produces a non-overlapping layout for the large topology limit', () => {
    const layout = autoLayoutTopology(createLargeTopologyFixture())
    const keys = Object.keys(layout.nodes)

    for (let left = 0; left < keys.length; left += 1) {
      for (let right = left + 1; right < keys.length; right += 1) {
        const a = nodeBox(layout, keys[left])
        const b = nodeBox(layout, keys[right])
        expect(overlaps(a, b), `${keys[left]} overlaps ${keys[right]}`).toBe(false)
      }
    }

    const regions = Object.entries(layout.networkLayouts)
    for (let left = 0; left < regions.length; left += 1) {
      for (let right = left + 1; right < regions.length; right += 1) {
        const [leftKey, a] = regions[left]
        const [rightKey, b] = regions[right]
        expect(overlaps(regionBox(a), regionBox(b)), `${leftKey} overlaps ${rightKey}`).toBe(false)
      }
    }
  })

  it('keeps every region member inside its own region box', () => {
    const layout = autoLayoutTopology(createLargeTopologyFixture())
    // The graph index is the authority on region ownership, so the assertion
    // uses it rather than re-deriving membership rules inside the test.
    const graph = buildTopologyGraph(layout)

    let checked = 0
    for (const [networkKey, memberKeys] of graph.membersByNetwork) {
      const region = layout.networkLayouts[networkKey]
      expect(region, `${networkKey} has no region layout`).toBeDefined()
      for (const key of memberKeys) {
        expect(contains(regionBox(region), nodeBox(layout, key)), `${key} escapes ${networkKey}`).toBe(true)
        checked += 1
      }
    }
    expect(checked).toBeGreaterThan(0)
  })

  it('never emits a node-level width or height, so region sizes cannot leak onto nodes', () => {
    const source = createLargeTopologyFixture()
    const seeded: TopologyDocument = {
      ...source,
      nodes: Object.fromEntries(
        Object.entries(source.nodes).map(([key, node]) => [
          key,
          { ...node, position: { ...node.position, width: 900, height: 1400 } },
        ])
      ),
    }
    const layout = autoLayoutTopology(seeded)
    for (const node of Object.values(layout.nodes)) {
      expect(node.position.width, `${node.key} kept a node width`).toBeNull()
      expect(node.position.height, `${node.key} kept a node height`).toBeNull()
    }
  })

  it('rebuilds an oversized manual region from its members instead of compounding it', () => {
    const source = createLargeTopologyFixture()
    const first = autoLayoutTopology(source)
    const oversized: TopologyDocument = {
      ...first,
      networkLayouts: Object.fromEntries(
        Object.entries(first.networkLayouts).map(([key, layout]) => [key, { ...layout, width: 3800, height: 2600 }])
      ),
    }
    const rebuilt = autoLayoutTopology(oversized)

    // Idempotence: re-running over its own output must not change any size.
    expect(rebuilt.networkLayouts).toEqual(first.networkLayouts)
    for (const region of Object.values(rebuilt.networkLayouts)) {
      expect(region.width).toBeLessThan(MAX_REGION_WIDTH)
      expect(region.height).toBeLessThan(MAX_REGION_HEIGHT)
    }
  })

  it('stays stable across repeated layout rounds', () => {
    let document = autoLayoutTopology(createLargeTopologyFixture())
    const signature = JSON.stringify(document.networkLayouts)
    for (let round = 0; round < 5; round += 1) document = autoLayoutTopology(document)
    expect(JSON.stringify(document.networkLayouts)).toBe(signature)
  })

  it('orders routing depth into left-to-right columns', () => {
    const source = createLargeTopologyFixture()
    const layout = autoLayoutTopology(source)
    const entry = centreOf(regionBox(layout.networkLayouts['network-00']))
    // network-01..03 are one, two and three route hops from the entry network.
    // Depth advances along +x so every link travels the way its handles point
    // (target on a card's left edge, source on its right).
    const hops = ['network-01', 'network-02', 'network-03'].map((key) =>
      centreOf(regionBox(layout.networkLayouts[key]))
    )
    expect(hops[0].x).toBeGreaterThan(entry.x)
    expect(hops[1].x).toBeGreaterThan(hops[0].x)
    expect(hops[2].x).toBeGreaterThan(hops[1].x)
  })

  it('packs a wide topology toward the target diagram aspect', () => {
    const layout = autoLayoutTopology(createLargeTopologyFixture())
    const bounds = diagramBounds(layout)
    const aspect = bounds.width / bounds.height
    // A single strip (aspect > 6) or a single column (aspect < 0.5) is what the
    // previous ring layout produced; the tier packer must stay in between.
    expect(aspect).toBeGreaterThan(DIAGRAM_TARGET_ASPECT / 4)
    expect(aspect).toBeLessThan(DIAGRAM_TARGET_ASPECT * 4)
  })

  it('keeps a small two-network scene compact and readable', () => {
    const source = createLargeTopologyFixture()
    const smallKeys = ['switch-00', 'switch-01', 'asset-000', 'asset-004', 'router-00']
    const small: TopologyDocument = {
      ...source,
      nodes: Object.fromEntries(Object.entries(source.nodes).filter(([key]) => smallKeys.includes(key))),
      connections: Object.fromEntries(
        Object.entries(source.connections).filter(([, connection]) => {
          if (connection.type === 'membership')
            return smallKeys.includes(connection.nodeKey) && smallKeys.includes(connection.switchKey)
          if (connection.type === 'route')
            return smallKeys.includes(connection.fromSwitchKey) && smallKeys.includes(connection.toSwitchKey)
          return false
        })
      ),
      networkLayouts: {},
    }
    const layout = autoLayoutTopology(small)
    const bounds = diagramBounds(layout)
    // The old ring layout produced 304x792 (aspect 0.38) for this shape, which
    // wasted almost the whole width of a 16:9 canvas after fitView.
    expect(bounds.width / bounds.height).toBeGreaterThan(0.6)
    expect(Object.keys(layout.networkLayouts)).toHaveLength(2)
  })

  it('groups disconnected networks after the routed topology', () => {
    const source = createLargeTopologyFixture()
    const withoutRoutes: TopologyDocument = {
      ...source,
      connections: Object.fromEntries(
        Object.entries(source.connections).filter(([, connection]) => connection.type !== 'route')
      ),
      networkLayouts: {},
    }
    const layout = autoLayoutTopology(withoutRoutes)
    const entry = regionBox(layout.networkLayouts['network-00'])
    const isolated = regionBox(layout.networkLayouts['network-05'])
    // Isolated networks share the trailing block and must not overlap the entry.
    expect(overlaps(entry, isolated)).toBe(false)
    expect(Object.keys(layout.networkLayouts)).toHaveLength(32)
  })

  it('places border routers clear of every region box', () => {
    const layout = autoLayoutTopology(createLargeTopologyFixture())
    const routers = Object.values(layout.nodes).filter((node) => node.type === 'router')
    expect(routers.length).toBeGreaterThan(0)
    for (const router of routers) {
      for (const [networkKey, region] of Object.entries(layout.networkLayouts)) {
        expect(
          overlaps(nodeBox(layout, router.key), regionBox(region)),
          `${router.key} overlaps region ${networkKey}`
        ).toBe(false)
      }
    }
  })

  it('lays out an incomplete draft without a switch on a finite grid', () => {
    const source = createLargeTopologyFixture()
    const draft: TopologyDocument = {
      ...source,
      nodes: {
        'asset-000': source.nodes['asset-000'],
        'asset-001': source.nodes['asset-001'],
        'asset-002': source.nodes['asset-002'],
      },
      connections: {},
      networkLayouts: {},
    }
    const layout = autoLayoutTopology(draft)
    const keys = Object.keys(layout.nodes)
    for (const key of keys) {
      expect(Number.isFinite(layout.nodes[key].position.x)).toBe(true)
      expect(Number.isFinite(layout.nodes[key].position.y)).toBe(true)
    }
    for (let left = 0; left < keys.length; left += 1) {
      for (let right = left + 1; right < keys.length; right += 1) {
        expect(overlaps(nodeBox(layout, keys[left]), nodeBox(layout, keys[right]))).toBe(false)
      }
    }
  })

  it('returns a single-node document unchanged', () => {
    const source = createLargeTopologyFixture()
    const single: TopologyDocument = { ...source, nodes: { 'asset-000': source.nodes['asset-000'] } }
    expect(autoLayoutTopology(single)).toBe(single)
  })

  it('lays out a very large topology fast enough to stay interactive', () => {
    const source = createLargeTopologyFixture()
    const started = performance.now()
    autoLayoutTopology(source)
    // The previous full-scan overlap search took seconds on this scale and froze
    // the main thread; the spatial index keeps it well inside one frame budget.
    expect(performance.now() - started).toBeLessThan(400)
  })
})
