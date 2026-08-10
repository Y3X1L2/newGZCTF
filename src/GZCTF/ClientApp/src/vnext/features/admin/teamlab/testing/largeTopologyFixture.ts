import type {
  TopologyAssetNode,
  TopologyConnection,
  TopologyDocument,
  TopologyNode,
  TopologyPosition,
} from '../model/topologyDocument'

const position = (x: number, y: number): TopologyPosition => ({
  x,
  y,
  width: null,
  height: null,
  collapsed: false,
})

function asset(index: number, networkIndex: number): TopologyAssetNode {
  const type = index % 4 === 1 ? 'linux-vm' : index % 4 === 2 ? 'windows-vm' : 'docker'
  return {
    type,
    key: `asset-${index.toString().padStart(3, '0')}`,
    name: `${type} ${index + 1}`,
    position: position(300 + (index % 4) * 210, networkIndex * 240 + 70),
    imageTemplateId: type === 'docker' ? 1001 : type === 'linux-vm' ? 2001 : 3001,
    resources: type === 'docker'
      ? { cpuUnits: 1, memoryMiB: 512, storageMiB: 1024 }
      : { cpuUnits: 2, memoryMiB: 2048, storageMiB: 20_480 },
    exposePort: type === 'docker' ? 8080 : null,
    healthCheck: type === 'docker' ? { kind: 'tcp', port: 8080 } : null,
    orderIndex: index,
    endpointObservation: 'optional',
  }
}

export function createLargeTopologyFixture(): TopologyDocument {
  const nodes: Record<string, TopologyNode> = {}
  const connections: Record<string, TopologyConnection> = {}

  for (let networkIndex = 0; networkIndex < 32; networkIndex += 1) {
    const switchKey = `switch-${networkIndex.toString().padStart(2, '0')}`
    nodes[switchKey] = {
      type: 'switch',
      key: switchKey,
      name: `交换机 ${networkIndex + 1}`,
      networkName: `业务网段 ${networkIndex + 1}`,
      networkKey: `network-${networkIndex.toString().padStart(2, '0')}`,
      position: position(30, networkIndex * 240 + 70),
      poolCidr: `10.${networkIndex + 1}.0.0/16`,
      runtimePrefixLength: 24,
      isEntry: networkIndex === 0,
      orderIndex: networkIndex,
    }

    for (let offset = 0; offset < 4; offset += 1) {
      const assetIndex = networkIndex * 4 + offset
      const current = asset(assetIndex, networkIndex)
      nodes[current.key] = current
      connections[`nic-${assetIndex.toString().padStart(3, '0')}-primary`] = {
        type: 'membership',
        key: `nic-${assetIndex.toString().padStart(3, '0')}-primary`,
        nodeKey: current.key,
        switchKey,
        hostOffset: offset + 10,
        primary: true,
        orderIndex: 0,
      }
      if (offset > 0) {
        connections[`dependency-${assetIndex.toString().padStart(3, '0')}`] = {
          type: 'dependency',
          key: `dependency-${assetIndex.toString().padStart(3, '0')}`,
          assetKey: current.key,
          dependsOnKey: `asset-${(assetIndex - 1).toString().padStart(3, '0')}`,
          condition: 'service-ready',
        }
      }
      if (offset === 3 && networkIndex < 31) {
        connections[`nic-${assetIndex.toString().padStart(3, '0')}-secondary`] = {
          type: 'membership',
          key: `nic-${assetIndex.toString().padStart(3, '0')}-secondary`,
          nodeKey: current.key,
          switchKey: `switch-${(networkIndex + 1).toString().padStart(2, '0')}`,
          hostOffset: 30,
          primary: false,
          orderIndex: 1,
        }
      }
    }
  }

  for (let routerIndex = 0; routerIndex < 8; routerIndex += 1) {
    const routerKey = `router-${routerIndex.toString().padStart(2, '0')}`
    const firstNetwork = routerIndex * 4
    nodes[routerKey] = {
      type: 'router',
      key: routerKey,
      name: `区域路由器 ${routerIndex + 1}`,
      position: position(1180, firstNetwork * 240 + 360),
    }
    for (let offset = 0; offset < 4; offset += 1) {
      const networkIndex = firstNetwork + offset
      const nicKey = `router-nic-${routerIndex}-${offset}`
      connections[nicKey] = {
        type: 'membership',
        key: nicKey,
        nodeKey: routerKey,
        switchKey: `switch-${networkIndex.toString().padStart(2, '0')}`,
        hostOffset: 1,
        primary: offset === 0,
        orderIndex: offset,
      }
      if (offset > 0) {
        const routeKey = `route-${routerIndex}-${offset}`
        connections[routeKey] = {
          type: 'route',
          key: routeKey,
          fromSwitchKey: `switch-${(networkIndex - 1).toString().padStart(2, '0')}`,
          toSwitchKey: `switch-${networkIndex.toString().padStart(2, '0')}`,
          viaNodeKey: routerKey,
          direction: 'bidirectional',
        }
      }
    }
  }

  return {
    schemaVersion: 2,
    name: '大型综合演练基准场景',
    nodes,
    connections,
    observation: {
      flowMetadataEnabled: true,
      onDemandPcapEnabled: true,
      endpointObservation: 'optional',
    },
    networkLayouts: {},
  }
}
