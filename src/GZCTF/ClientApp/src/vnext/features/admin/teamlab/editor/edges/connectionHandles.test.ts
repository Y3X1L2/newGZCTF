import { describe, expect, it } from 'vitest'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
import { createTopologyNode } from '../nodeFactory'
import { connectionHandles } from './connectionHandles'

describe('connectionHandles', () => {
  const document = createEmptyTopologyDocument('Network')
  const network = createTopologyNode(document, 'switch', { x: 100, y: 0 })
  const asset = createTopologyNode(document, 'docker', { x: 0, y: 170 })

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
})
