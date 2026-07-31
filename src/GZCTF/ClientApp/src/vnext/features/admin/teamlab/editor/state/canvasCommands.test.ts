import { describe, expect, it } from 'vitest'
import { addTopologyNode } from '../../model/topologyCommands'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
import { createTopologyNode } from '../nodeFactory'
import { connectCanvasNodes } from './canvasCommands'

describe('canvas commands', () => {
  it('uses model commands to attach a router and create its default route', () => {
    let document = createEmptyTopologyDocument('Routing')
    for (const [type, x] of [
      ['switch', 0],
      ['switch', 300],
      ['router', 150],
    ] as const) {
      document = addTopologyNode(document, createTopologyNode(document, type, { x, y: 0 })).document
    }
    const [left, right] = Object.values(document.nodes).filter((node) => node.type === 'switch')
    const router = Object.values(document.nodes).find((node) => node.type === 'router')
    if (!left || !right || !router) throw new Error('Fixture is incomplete.')

    document = connectCanvasNodes(document, router.key, left.key, 'network')
    document = connectCanvasNodes(document, router.key, right.key, 'network')

    expect(Object.values(document.connections).filter((connection) => connection.type === 'membership')).toHaveLength(2)
    expect(Object.values(document.connections).filter((connection) => connection.type === 'route')).toHaveLength(1)
  })

  it('rejects invalid network gestures through the model boundary', () => {
    let document = createEmptyTopologyDocument('Invalid')
    document = addTopologyNode(document, createTopologyNode(document, 'docker', { x: 0, y: 0 })).document
    document = addTopologyNode(document, createTopologyNode(document, 'linux-vm', { x: 200, y: 0 })).document
    const keys = Object.keys(document.nodes)
    expect(() => connectCanvasNodes(document, keys[0], keys[1], 'network')).toThrow('交换机')
  })
})
