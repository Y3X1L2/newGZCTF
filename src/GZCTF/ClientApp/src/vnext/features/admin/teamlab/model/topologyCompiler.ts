import type {
  CreateTeamLabTopologyRequest,
  TeamLabTopologyEditor,
  TeamLabTopologyInterface,
} from '../api/teamlabContracts'
import { networkMembersOf } from './topologyCommands'
import {
  isTopologyAsset,
  type TopologyConnection,
  type TopologyDocument,
  type TopologyNode,
  type TopologyPosition,
} from './topologyDocument'
import {
  MIN_REGION_HEIGHT,
  MIN_REGION_WIDTH,
  clampRegionSize,
  nodeSize,
  regionSizeForMembers,
} from './topologyGeometry'

export class TopologyCompileError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'TopologyCompileError'
  }
}

const byKey = <T extends { key: string }>(left: T, right: T) => left.key.localeCompare(right.key)

/**
 * Region entry: the only editor record allowed to persist a size, because a
 * region is the only user-resizable object on the canvas.
 */
function regionEditorItem(layout: TopologyPosition) {
  const size = clampRegionSize({
    width: layout.width ?? MIN_REGION_WIDTH,
    height: layout.height ?? MIN_REGION_HEIGHT,
  })
  return { x: layout.x, y: layout.y, ...size, collapsed: layout.collapsed }
}

/**
 * Node entry: position only. Writing a node width/height would let it be read
 * back as a real node size and inflate the region that contains it.
 */
function nodeEditorItem(position: TopologyPosition) {
  return { x: position.x, y: position.y, width: null, height: null, collapsed: false }
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

/** Author-edited JSON text becomes the contract object; blank text unsets the parameters. */
function parseDeviceParameters(text: string | null | undefined, assetKey: string): unknown {
  const trimmed = (text ?? '').trim()
  if (!trimmed) return null
  try {
    return JSON.parse(trimmed)
  } catch {
    throw new TopologyCompileError(`资产 '${assetKey}' 的设备包参数不是合法 JSON。`)
  }
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
  const networks: Record<string, ReturnType<typeof regionEditorItem>> = {}
  const assets: Record<string, ReturnType<typeof nodeEditorItem>> = {}
  const infrastructure: Record<string, ReturnType<typeof nodeEditorItem>> = {}

  for (const node of Object.values(document.nodes).sort(byKey)) {
    if (node.type === 'switch') {
      const layout = document.networkLayouts[node.networkKey]
      const memberHeights = networkMembersOf(document, node.networkKey)
        .filter((key) => document.nodes[key]?.type !== 'switch')
        .map((key) => nodeSize(document.nodes[key]).height)
      const derived = regionSizeForMembers(memberHeights)
      networks[node.networkKey] = regionEditorItem(
        layout ?? { ...node.position, ...derived, collapsed: false }
      )
      if (!node.implicit || node.name !== node.networkName) infrastructure[node.key] = nodeEditorItem(node.position)
    } else if (node.type === 'router') {
      infrastructure[node.key] = nodeEditorItem(node.position)
    } else {
      assets[node.key] = nodeEditorItem(node.position)
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
      devicePackageId: node.devicePackageId ?? null,
      deviceParameters: parseDeviceParameters(node.deviceParameters, node.key),
      connectorId: node.connectorId ?? null,
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
