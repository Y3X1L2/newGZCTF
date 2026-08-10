import type {
  CreateTeamLabTopologyRequest,
  TeamLabTopologyEditor,
  TeamLabTopologyInterface,
} from '../api/teamlabContracts'
import {
  isTopologyAsset,
  type TopologyConnection,
  type TopologyDocument,
  type TopologyNode,
  type TopologyPosition,
} from './topologyDocument'

export class TopologyCompileError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'TopologyCompileError'
  }
}

const byKey = <T extends { key: string }>(left: T, right: T) => left.key.localeCompare(right.key)

function editorItem(position: TopologyPosition) {
  return { ...position }
}

function requireNode(document: TopologyDocument, key: string): TopologyNode {
  const node = document.nodes[key]
  if (!node) throw new TopologyCompileError(`Topology node '${key}' does not exist.`)
  return node
}

function switchNetworkKey(document: TopologyDocument, switchKey: string) {
  const node = requireNode(document, switchKey)
  if (node.type !== 'switch') throw new TopologyCompileError(`Topology node '${switchKey}' is not a switch.`)
  return node.networkKey
}

function interfacesFor(document: TopologyDocument, nodeKey: string): TeamLabTopologyInterface[] {
  return Object.values(document.connections)
    .filter(
      (connection): connection is Extract<TopologyConnection, { type: 'membership' }> =>
        connection.type === 'membership' && connection.nodeKey === nodeKey
    )
    .map((connection) => ({
      key: connection.interfaceKey ?? connection.key,
      networkKey: switchNetworkKey(document, connection.switchKey),
      hostOffset: connection.hostOffset,
      primary: connection.primary,
      orderIndex: connection.orderIndex,
    }))
    .sort(byKey)
}

function compileEditor(document: TopologyDocument): TeamLabTopologyEditor {
  const networks: Record<string, ReturnType<typeof editorItem>> = {}
  const assets: Record<string, ReturnType<typeof editorItem>> = {}
  const infrastructure: Record<string, ReturnType<typeof editorItem>> = {}

  for (const node of Object.values(document.nodes).sort(byKey)) {
    if (node.type === 'switch') {
      const layout = document.networkLayouts[node.networkKey]
      networks[node.networkKey] = layout ? editorItem(layout) : editorItem(node.position)
      if (!node.implicit || node.name !== node.networkName) infrastructure[node.key] = editorItem(node.position)
    } else if (node.type === 'router') {
      infrastructure[node.key] = editorItem(node.position)
    } else {
      assets[node.key] = editorItem(node.position)
    }
  }

  return { networks, assets, infrastructure }
}

export function compileTopologyDocument(document: TopologyDocument): CreateTeamLabTopologyRequest {
  const nodes = Object.values(document.nodes).sort(byKey)
  const connections = Object.values(document.connections).sort(byKey)
  const switches = nodes.filter((node) => node.type === 'switch')
  const networkKeys = new Set<string>()
  for (const node of switches) {
    if (networkKeys.has(node.networkKey)) {
      throw new TopologyCompileError(`Network key '${node.networkKey}' is owned by more than one switch.`)
    }
    networkKeys.add(node.networkKey)
  }

  return {
    schemaVersion: 2,
    name: document.name,
    networks: switches.map((node) => ({
      key: node.networkKey,
      name: node.networkName,
      addressPool: { poolCidr: node.poolCidr, runtimePrefixLength: node.runtimePrefixLength },
      isEntry: node.isEntry,
      orderIndex: node.orderIndex,
    })),
    infrastructure: nodes
      .filter(
        (node) =>
          node.type === 'router' ||
          (node.type === 'switch' && (!node.implicit || node.name !== node.networkName))
      )
      .map((node) =>
        node.type === 'switch'
          ? {
              key: node.key,
              name: node.name,
              kind: 'managed-switch' as const,
              interfaces: [],
              networkKey: node.networkKey,
            }
          : {
              key: node.key,
              name: node.name,
              kind: 'managed-router' as const,
              interfaces: interfacesFor(document, node.key),
              networkKey: null,
            }
      ),
    assets: nodes.filter(isTopologyAsset).map((node) => ({
      key: node.key,
      name: node.name,
      kind: node.type === 'docker' ? 'docker' : 'vm',
      imageTemplateId: node.imageTemplateId,
      resources: { ...node.resources },
      interfaces: interfacesFor(document, node.key),
      exposePort: node.exposePort,
      healthCheck: node.healthCheck ? { ...node.healthCheck } : null,
      orderIndex: node.orderIndex,
      endpointObservation: node.endpointObservation,
    })),
    connections: connections
      .filter((connection) => connection.type === 'route')
      .map((connection) => {
        const via = requireNode(document, connection.viaNodeKey)
        if (via.type !== 'router') throw new TopologyCompileError(`Route '${connection.key}' must use a managed router.`)
        return {
          key: connection.key,
          fromNetworkKey: switchNetworkKey(document, connection.fromSwitchKey),
          toNetworkKey: switchNetworkKey(document, connection.toSwitchKey),
          viaNodeKey: via.type === 'router' ? via.key : null,
          viaAssetKey: null,
          direction: connection.direction,
        }
      }),
    dependencies: connections
      .filter((connection) => connection.type === 'dependency')
      .map((connection) => ({
        assetKey: connection.assetKey,
        dependsOnKey: connection.dependsOnKey,
        condition: connection.condition,
      })),
    observation: { ...document.observation },
    editor: compileEditor(document),
  }
}
