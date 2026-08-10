import { nextTopologyKey, topologyKeys } from '../model/topologyKeys'
import type { TopologyDocument, TopologyNode, TopologyNodeType, TopologyPosition } from '../model/topologyDocument'

const positionAt = (x: number, y: number): TopologyPosition => ({
  x,
  y,
  width: null,
  height: null,
  collapsed: false,
})

export function createTopologyNode(
  document: TopologyDocument,
  type: TopologyNodeType,
  point: { x: number; y: number }
): TopologyNode {
  const occupied = topologyKeys(document)
  const orderIndex = Object.keys(document.nodes).length
  const position = positionAt(point.x, point.y)
  if (type === 'switch') {
    const key = nextTopologyKey('switch', occupied)
    occupied.add(key)
    const networkKey = nextTopologyKey('network', occupied)
    const networkIndex = Object.values(document.nodes).filter((node) => node.type === 'switch').length
    return {
      type,
      key,
      name: `交换机 ${networkIndex + 1}`,
      networkName: `网段 ${networkIndex + 1}`,
      position,
      networkKey,
      poolCidr: `10.${Math.min(networkIndex + 1, 254)}.0.0/16`,
      runtimePrefixLength: 24,
      isEntry: networkIndex === 0,
      orderIndex,
    }
  }
  if (type === 'router') {
    return { type, key: nextTopologyKey('router', occupied), name: '路由器', position }
  }

  const names: Record<Exclude<TopologyNodeType, 'switch' | 'router'>, string> = {
    docker: 'Docker 服务',
    'linux-vm': 'Linux 虚拟机',
    'windows-vm': 'Windows 虚拟机',
  }
  return {
    type,
    key: nextTopologyKey(type, occupied),
    name: names[type],
    position,
    imageTemplateId: 0,
    resources:
      type === 'docker'
        ? { cpuUnits: 1, memoryMiB: 512, storageMiB: 1024 }
        : { cpuUnits: 2, memoryMiB: 2048, storageMiB: 20_480 },
    exposePort: null,
    healthCheck: null,
    orderIndex,
    endpointObservation: 'optional',
  }
}
