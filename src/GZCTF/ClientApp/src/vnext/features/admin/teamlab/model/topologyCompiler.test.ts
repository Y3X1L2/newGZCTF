import { describe, expect, it } from 'vitest'
import { compileTopologyDocument } from './topologyCompiler'
import type { TopologyDocument, TopologyNode } from './topologyDocument'

const position = (x: number, y: number) => ({ x, y, width: null, height: null, collapsed: false })
const asset = (key: string, type: 'docker' | 'linux-vm' | 'windows-vm', orderIndex: number): TopologyNode => ({
  type,
  key,
  name: key,
  position: position(500, orderIndex * 140),
  imageTemplateId: orderIndex + 1,
  resources: { cpuUnits: 2, memoryMiB: 2048, storageMiB: 8192 },
  routingEnabled: key === 'gateway',
  exposePort: null,
  environment: null,
  startCommand: null,
  healthCheck: null,
  orderIndex,
  stateless: false,
  bootstrap: null,
  endpointObservation: 'optional',
  bakeAtPublish: false,
  imageDigest: null,
})

function document(): TopologyDocument {
  return {
    schemaVersion: 2,
    name: 'Multi-router range',
    nodes: {
      'switch-edge': {
        type: 'switch',
        key: 'switch-edge',
        name: 'Edge',
        networkName: 'Edge',
        position: position(0, 0),
        networkKey: 'edge',
        poolCidr: '10.10.0.0/16',
        runtimePrefixLength: 24,
        isEntry: true,
        orderIndex: 0,
      },
      'switch-app': {
        type: 'switch',
        key: 'switch-app',
        name: 'App',
        networkName: 'App',
        position: position(0, 180),
        networkKey: 'app',
        poolCidr: '172.20.0.0/16',
        runtimePrefixLength: 24,
        isEntry: false,
        orderIndex: 1,
      },
      'switch-data': {
        type: 'switch',
        key: 'switch-data',
        name: 'Data',
        networkName: 'Data',
        position: position(0, 360),
        networkKey: 'data',
        poolCidr: '192.168.40.0/24',
        runtimePrefixLength: 28,
        isEntry: false,
        orderIndex: 2,
      },
      'router-edge': { type: 'router', key: 'router-edge', name: 'Edge router', position: position(250, 90) },
      'router-data': { type: 'router', key: 'router-data', name: 'Data router', position: position(250, 270) },
      portal: asset('portal', 'docker', 0),
      gateway: asset('gateway', 'linux-vm', 1),
      dc: asset('dc', 'windows-vm', 2),
    },
    connections: {
      'r1-edge': {
        type: 'membership',
        key: 'r1-edge',
        nodeKey: 'router-edge',
        switchKey: 'switch-edge',
        hostOffset: 1,
        primary: true,
        orderIndex: 0,
      },
      'r1-app': {
        type: 'membership',
        key: 'r1-app',
        nodeKey: 'router-edge',
        switchKey: 'switch-app',
        hostOffset: 1,
        primary: false,
        orderIndex: 1,
      },
      'r2-app': {
        type: 'membership',
        key: 'r2-app',
        nodeKey: 'router-data',
        switchKey: 'switch-app',
        hostOffset: 2,
        primary: true,
        orderIndex: 0,
      },
      'r2-data': {
        type: 'membership',
        key: 'r2-data',
        nodeKey: 'router-data',
        switchKey: 'switch-data',
        hostOffset: 1,
        primary: false,
        orderIndex: 1,
      },
      'portal-edge': {
        type: 'membership',
        key: 'portal-edge',
        nodeKey: 'portal',
        switchKey: 'switch-edge',
        hostOffset: 10,
        primary: true,
        orderIndex: 0,
      },
      'gateway-app': {
        type: 'membership',
        key: 'gateway-app',
        nodeKey: 'gateway',
        switchKey: 'switch-app',
        hostOffset: 20,
        primary: true,
        orderIndex: 0,
      },
      'gateway-data': {
        type: 'membership',
        key: 'gateway-data',
        nodeKey: 'gateway',
        switchKey: 'switch-data',
        hostOffset: 20,
        primary: false,
        orderIndex: 1,
      },
      'dc-data': {
        type: 'membership',
        key: 'dc-data',
        nodeKey: 'dc',
        switchKey: 'switch-data',
        hostOffset: 10,
        primary: true,
        orderIndex: 0,
      },
      'edge-app-route': {
        type: 'route',
        key: 'edge-app-route',
        fromSwitchKey: 'switch-edge',
        toSwitchKey: 'switch-app',
        viaNodeKey: 'router-edge',
        direction: 'bidirectional',
      },
      'app-data-route': {
        type: 'route',
        key: 'app-data-route',
        fromSwitchKey: 'switch-app',
        toSwitchKey: 'switch-data',
        viaNodeKey: 'router-data',
        direction: 'from-to',
      },
      'gateway-route': {
        type: 'route',
        key: 'gateway-route',
        fromSwitchKey: 'switch-app',
        toSwitchKey: 'switch-data',
        viaNodeKey: 'gateway',
        direction: 'bidirectional',
      },
      'dc-after-portal': {
        type: 'dependency',
        key: 'dc-after-portal',
        assetKey: 'dc',
        dependsOnKey: 'portal',
        condition: 'service-ready',
      },
    },
    observation: { flowMetadataEnabled: true, onDemandPcapEnabled: true, endpointObservation: 'required' },
  }
}

describe('compileTopologyDocument', () => {
  it('compiles multi-router, multi-NIC and dependency intent deterministically', () => {
    const first = compileTopologyDocument(document())
    const second = compileTopologyDocument(document())

    expect(first).toEqual(second)
    expect(first.networks.map((item) => item.key)).toEqual(['app', 'data', 'edge'])
    expect(first.infrastructure).toHaveLength(5)
    expect(first.assets.find((item) => item.key === 'gateway')?.interfaces).toHaveLength(2)
    expect(first.connections).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ key: 'app-data-route', viaNodeKey: 'router-data', direction: 'from-to' }),
        expect.objectContaining({ key: 'gateway-route', viaAssetKey: 'gateway', viaNodeKey: null }),
      ])
    )
    expect(first.dependencies).toEqual([{ assetKey: 'dc', dependsOnKey: 'portal', condition: 'service-ready' }])
    expect(first.editor.infrastructure['router-edge']).toEqual(position(250, 90))
  })
})
