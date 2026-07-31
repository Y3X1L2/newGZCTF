import { connectTopology } from '../../model/topologyCommands'
import { isTopologyAsset, type TopologyDocument } from '../../model/topologyDocument'

export type CanvasConnectionMode = 'network' | 'dependency'

function connectRouterNetworks(document: TopologyDocument, routerKey: string) {
  const switchKeys = Object.values(document.connections)
    .filter((connection) => connection.type === 'membership' && connection.nodeKey === routerKey)
    .map((connection) => (connection.type === 'membership' ? connection.switchKey : ''))
    .sort()
  let current = document
  for (let left = 0; left < switchKeys.length; left += 1) {
    for (let right = left + 1; right < switchKeys.length; right += 1) {
      const exists = Object.values(current.connections).some(
        (connection) =>
          connection.type === 'route' &&
          connection.viaNodeKey === routerKey &&
          ((connection.fromSwitchKey === switchKeys[left] && connection.toSwitchKey === switchKeys[right]) ||
            (connection.fromSwitchKey === switchKeys[right] && connection.toSwitchKey === switchKeys[left]))
      )
      if (!exists) {
        current = connectTopology(current, {
          type: 'route',
          fromSwitchKey: switchKeys[left],
          toSwitchKey: switchKeys[right],
          viaNodeKey: routerKey,
        }).document
      }
    }
  }
  return current
}

export function connectCanvasNodes(
  document: TopologyDocument,
  sourceKey: string,
  targetKey: string,
  mode: CanvasConnectionMode
) {
  const source = document.nodes[sourceKey]
  const target = document.nodes[targetKey]
  if (!source || !target) throw new Error('连接端点不存在。')

  if (mode === 'dependency') {
    if (!isTopologyAsset(source) || !isTopologyAsset(target)) throw new Error('启动依赖只能连接两个计算资产。')
    return connectTopology(document, {
      type: 'dependency',
      assetKey: target.key,
      dependsOnKey: source.key,
    }).document
  }

  const networkSwitch = source.type === 'switch' ? source : target.type === 'switch' ? target : null
  const endpoint = source.type === 'switch' ? target : source
  if (!networkSwitch) throw new Error('网络连接必须连接到交换机。')
  const connected = connectTopology(document, {
    type: 'membership',
    nodeKey: endpoint.key,
    switchKey: networkSwitch.key,
  }).document
  return endpoint.type === 'router' ? connectRouterNetworks(connected, endpoint.key) : connected
}
