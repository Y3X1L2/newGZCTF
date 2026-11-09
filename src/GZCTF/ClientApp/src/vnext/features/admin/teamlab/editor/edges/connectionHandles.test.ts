import { getSmoothStepPath, Position } from '@xyflow/react'
import { describe, expect, it } from 'vitest'
import { createEmptyTopologyDocument, type TopologyNode } from '../../model/topologyDocument'
import { nodeSize } from '../../model/topologyGeometry'
import { createTopologyNode } from '../nodeFactory'
import { connectionHandles, networkPathOptions, type ExistingSourceRoute } from './connectionHandles'

// Sample the actual rounded SVG rendered by React Flow, independently of the
// zero-radius orthogonal collision checks used to rank candidates.
function renderedPoints(path: string) {
  let current = { x: 0, y: 0 }
  const points: { x: number; y: number }[] = []
  for (const command of path.match(/[MLQ][^MLQ]*/g) ?? []) {
    const values = (command.slice(1).match(/-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? []).map(Number)
    const next = { x: values.at(-2)!, y: values.at(-1)! }
    if (command[0] === 'M') {
      current = next
      points.push(next)
      continue
    }
    const steps = Math.max(1, Math.ceil(Math.hypot(next.x - current.x, next.y - current.y)))
    for (let step = 1; step <= steps; step++) {
      const t = step / steps
      points.push(command[0] === 'Q'
        ? { x: (1 - t) ** 2 * current.x + 2 * (1 - t) * t * values[0] + t ** 2 * next.x,
          y: (1 - t) ** 2 * current.y + 2 * (1 - t) * t * values[1] + t ** 2 * next.y }
        : { x: current.x + t * (next.x - current.x), y: current.y + t * (next.y - current.y) })
    }
    current = next
  }
  return points
}

function renderedRoute(source: TopologyNode, target: TopologyNode, nodes: TopologyNode[], occupied: ExistingSourceRoute[] = []) {
  const handles = connectionHandles(source, target, nodes, occupied)
  const side = (id: string, fallback: Position) => id.includes('-') ? id.split('-')[1] as Position : fallback
  const from = side(handles.sourceHandle, Position.Right)
  const to = side(handles.targetHandle, Position.Left)
  const anchor = (node: TopologyNode, position: Position) => {
    const size = nodeSize(node)
    return { x: node.position.x + (position === Position.Left ? 0 : position === Position.Right ? size.width : size.width / 2),
      y: node.position.y + (position === Position.Top ? 0 : position === Position.Bottom ? size.height : size.height / 2) }
  }
  const a = anchor(source, from)
  const b = anchor(target, to)
  const [path] = getSmoothStepPath({ sourceX: a.x, sourceY: a.y, targetX: b.x, targetY: b.y,
    sourcePosition: from, targetPosition: to, ...networkPathOptions })
  const hits = nodes.filter(node => {
    const { width, height } = nodeSize(node)
    return renderedPoints(path).some(point => point.x > node.position.x + 0.1 && point.x < node.position.x + width - 0.1 &&
      point.y > node.position.y + 0.1 && point.y < node.position.y + height - 0.1)
  }).map(node => node.key)
  return { ...handles, path, hits }
}

describe('connectionHandles', () => {
  const document = createEmptyTopologyDocument('Network')
  const network = createTopologyNode(document, 'switch', { x: 100, y: 0 })
  const asset = createTopologyNode(document, 'docker', { x: 0, y: 170 })
  const lab = [
    { ...network, key: 'switch-internal', position: { ...network.position, x: 626, y: -203.8 } },
    ...[
      ['dc01', 504, -42.2], ['oa01', 752, -42.2], ['win7', 504, 85.8], ['web01', 328, 341.8],
    ].map(([key, x, y]) => ({ ...asset, key: String(key), position: { ...asset.position, x: Number(x), y: Number(y) } })),
  ]

  it('joins a member below its switch through the clear gap instead of looping around both cards', () => {
    expect(connectionHandles(asset, network)).toEqual({ sourceHandle: 'source-top', targetHandle: 'target-bottom' })
  })

  it('faces a neighbouring network on either side without changing the actual connection direction', () => {
    const neighbour = { ...network, position: { ...network.position, x: 600 } }
    expect(connectionHandles(network, neighbour)).toEqual({ sourceHandle: 'source', targetHandle: 'target' })
    expect(connectionHandles(neighbour, network)).toEqual({ sourceHandle: 'source-left', targetHandle: 'target-right' })
  })

  it('uses bottom-to-top anchors when an author moves a card above its network', () => {
    const above = { ...asset, position: { ...asset.position, y: -220 } }
    expect(connectionHandles(above, network)).toEqual({ sourceHandle: 'source-bottom', targetHandle: 'target-top' })
  })

  it('preserves a safe editing projection while an incomplete draft references a missing node', () => {
    expect(connectionHandles(undefined, network)).toEqual({ sourceHandle: 'source', targetHandle: 'target' })
  })

  it.each(['win7', 'web01'])('routes the real Lab2 %s connection around DC01 and the other member cards', key => {
    const source = lab.find(node => node.key === key)!
    const route = renderedRoute(source, lab[0], lab)
    expect(route.hits, route.path).toEqual([])
    if (key === 'win7') {
      expect(route.sourceHandle).toBe('source')
      expect(route.targetHandle).toBe('target-bottom')
    }
  })

  it.each(['dc01', 'oa01'])('keeps the unobstructed Lab2 %s star link facing the bottom of its switch', key => {
    const route = renderedRoute(lab.find(node => node.key === key)!, lab[0], lab)
    expect(route.hits, route.path).toEqual([])
    expect(route.sourceHandle).toBe('source-top')
    expect(route.targetHandle).toBe('target-bottom')
  })

  it('recalculates the clear route when an author drags another card into the old corridor', () => {
    const source = lab.find(node => node.key === 'win7')!
    const before = renderedRoute(source, lab[0], lab)
    const moved = lab.map(node => node.key === 'oa01' ? { ...node, position: { ...node.position, x: 730, y: 85.8 } } : node)
    const after = renderedRoute(source, lab[0], moved)
    expect(after.hits, after.path).toEqual([])
    expect(after.sourceHandle).not.toBe(before.sourceHandle)
    expect(renderedRoute(source, lab[0], moved)).toEqual(after)
  })

  it.each([false, true])('keeps all five Lab2 links visible without a long shared web01 trunk (reverse=%s)', reverse => {
    const entry = { ...network, key: 'switch-entry', position: { ...network.position, x: 24, y: -75.8 } }
    const nodes = [...lab, entry]
    const source = lab.find(node => node.key === 'web01')!
    const targets = reverse ? [lab[0], entry] : [entry, lab[0]]
    const first = renderedRoute(source, targets[0], nodes)
    const second = renderedRoute(source, targets[1], nodes, [{ ...first, target: targets[0] }])
    expect(first.hits, first.path).toEqual([])
    expect(second.hits, second.path).toEqual([])
    expect(second.sourceHandle).not.toBe(first.sourceHandle)
    const a = new Set(renderedPoints(first.path).map(point => `${Math.round(point.x)},${Math.round(point.y)}`))
    const commonPixels = new Set(renderedPoints(second.path)
      .map(point => `${Math.round(point.x)},${Math.round(point.y)}`).filter(point => a.has(point)))
    expect(commonPixels.size).toBeLessThan(32)
    for (const key of ['dc01', 'oa01', 'win7']) {
      const route = renderedRoute(lab.find(node => node.key === key)!, lab[0], nodes)
      expect(route.hits, route.path).toEqual([])
      if (key !== 'win7') {
        expect(route.sourceHandle).toBe('source-top')
        expect(route.targetHandle).toBe('target-bottom')
      }
    }
  })
})
