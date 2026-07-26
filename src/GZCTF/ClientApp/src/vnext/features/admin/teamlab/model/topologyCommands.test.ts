import { describe, expect, it } from 'vitest'
import {
  connectTopology,
  copyTopologyFragment,
  deleteTopologyItems,
  pasteTopologyFragment,
  updateTopologyConnection,
} from './topologyCommands'
import type { TopologyDocument } from './topologyDocument'

const position = { x: 0, y: 0, width: null, height: null, collapsed: false }
const asset = (key: string) => ({
  type: 'docker' as const,
  key,
  name: key,
  position,
  imageTemplateId: 1,
  resources: { cpuUnits: 1, memoryMiB: 128, storageMiB: 256 },
  routingEnabled: false,
  exposePort: null,
  environment: null,
  startCommand: null,
  healthCheck: null,
  orderIndex: 0,
  stateless: false,
  bootstrap: null,
  endpointObservation: 'disabled' as const,
  bakeAtPublish: false,
  imageDigest: null,
})

function document(): TopologyDocument {
  return {
    schemaVersion: 2,
    name: 'Commands',
    observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true, endpointObservation: 'optional' },
    nodes: {
      sw1: {
        type: 'switch',
        key: 'sw1',
        name: 'One',
        networkName: 'Network one',
        position,
        networkKey: 'net1',
        poolCidr: '10.0.0.0/16',
        runtimePrefixLength: 24,
        isEntry: true,
        orderIndex: 0,
      },
      sw2: {
        type: 'switch',
        key: 'sw2',
        name: 'Two',
        networkName: 'Network two',
        position,
        networkKey: 'net2',
        poolCidr: '172.16.0.0/16',
        runtimePrefixLength: 24,
        isEntry: false,
        orderIndex: 1,
      },
      router: { type: 'router', key: 'router', name: 'Router', position },
      a: asset('a'),
      b: asset('b'),
    },
    connections: {
      'router-sw1': {
        type: 'membership',
        key: 'router-sw1',
        nodeKey: 'router',
        switchKey: 'sw1',
        hostOffset: 1,
        primary: true,
        orderIndex: 0,
      },
      'router-sw2': {
        type: 'membership',
        key: 'router-sw2',
        nodeKey: 'router',
        switchKey: 'sw2',
        hostOffset: 1,
        primary: false,
        orderIndex: 1,
      },
      'a-sw1': {
        type: 'membership',
        key: 'a-sw1',
        nodeKey: 'a',
        switchKey: 'sw1',
        hostOffset: 10,
        primary: true,
        orderIndex: 0,
      },
      route: {
        type: 'route',
        key: 'route',
        fromSwitchKey: 'sw1',
        toSwitchKey: 'sw2',
        viaNodeKey: 'router',
        direction: 'bidirectional',
      },
      dependency: {
        type: 'dependency',
        key: 'dependency',
        assetKey: 'b',
        dependsOnKey: 'a',
        condition: 'network-ready',
      },
    },
  }
}

describe('topology commands', () => {
  it('pastes collision-safe keys and keeps only internal relationships', () => {
    const source = document()
    const fragment = copyTopologyFragment(source, new Set(['sw1', 'a']))
    const pasted = pasteTopologyFragment(source, fragment).document
    const copiedNodes = Object.values(pasted.nodes).filter((item) => item.key.includes('-copy'))
    const copiedConnections = Object.values(pasted.connections).filter((item) => item.key.includes('-copy'))

    expect(copiedNodes).toHaveLength(2)
    expect(copiedConnections).toHaveLength(1)
    expect(copiedConnections[0]).toMatchObject({ type: 'membership', nodeKey: 'a-copy', switchKey: 'sw1-copy' })
    expect(copiedConnections.some((item) => item.type === 'dependency' || item.type === 'route')).toBe(false)
    expect(pasted.nodes['sw1-copy']).toMatchObject({ networkKey: 'net1-copy' })

    const pastedAgain = pasteTopologyFragment(pasted, fragment).document
    expect(pastedAgain.nodes['sw1-copy-2']).toMatchObject({ networkKey: 'net1-copy-2' })
  })

  it('deletes all dangling memberships, routes and dependencies atomically', () => {
    const source = document()
    const changed = deleteTopologyItems(source, { nodeKeys: new Set(['sw1', 'a']), connectionKeys: new Set() })

    expect(changed.before).toBe(source)
    expect(Object.keys(changed.document.connections)).toEqual(['router-sw2'])
  })

  it('connects a directional route only after both router memberships exist', () => {
    const source = { ...document(), connections: { ...document().connections } }
    delete (source.connections as Record<string, unknown>).route
    const changed = connectTopology(source, {
      type: 'route',
      fromSwitchKey: 'sw1',
      toSwitchKey: 'sw2',
      viaNodeKey: 'router',
      direction: 'from-to',
    })
    expect(changed.document.connections[changed.value]).toMatchObject({ type: 'route', direction: 'from-to' })

    const route = changed.document.connections[changed.value]
    if (route?.type !== 'route') throw new Error('Expected route')
    const updated = updateTopologyConnection(changed.document, { ...route, direction: 'bidirectional' })
    expect(updated.document.connections[changed.value]).toMatchObject({ direction: 'bidirectional' })
  })
})
