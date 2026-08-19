import type {
  TeamLabTopologyAsset,
  TeamLabTopologyDetail,
  TeamLabTopologyEditor,
  TeamLabTopologyInfrastructure,
  TeamLabTopologyInterface,
  UpdateTeamLabTopologyRequest,
} from '../api/teamlabContracts'
import { compileTopologyDocument } from './topologyCompiler'
import {
  ASSET_NODE_HEIGHT,
  INFRA_NODE_HEIGHT,
  MEMBER_GAP_X,
  MEMBER_GAP_Y,
  MIN_REGION_WIDTH,
  NODE_WIDTH,
  REGION_HEADER_HEIGHT,
  REGION_PADDING_X,
} from './topologyGeometry'
import {
  type TopologyAssetNode,
  type TopologyConnection,
  type TopologyDocument,
  type TopologyNode,
  type TopologyPosition,
} from './topologyDocument'
import { dependencyConnectionKey, nextTopologyKey } from './topologyKeys'

export type VmDeviceType = 'linux-vm' | 'windows-vm'
export type VmDeviceTypeResolver = (asset: TeamLabTopologyAsset) => VmDeviceType

export interface TopologyMapperOptions {
  resolveVmDeviceType: VmDeviceTypeResolver
}

const sorted = <T extends { key: string }>(items: readonly T[]) => [...items].sort((a, b) => a.key.localeCompare(b.key))

/**
 * Reads a persisted region box. Regions are the only user-resizable objects, so
 * this is the only place a width/height may survive the mapping.
 */
function regionPosition(editor: TeamLabTopologyEditor, networkKey: string, fallbackIndex: number): TopologyPosition {
  const source = editor.networks[networkKey]
  if (!source) return fallbackPosition(fallbackIndex)
  return { ...source }
}

/**
 * Reads a persisted node position and **drops any width/height**.
 *
 * `TeamLabEditorItem` is one wire shape shared by networks, assets and
 * infrastructure, but only a network region can be resized. A resized region's
 * size used to leak onto its implicit switch (which has no infrastructure entry
 * of its own and therefore fell back to the network entry), and auto layout then
 * padded that fake node size into an even larger region on every round.
 */
function nodePosition(
  editor: TeamLabTopologyEditor,
  kind: 'asset' | 'infrastructure',
  key: string,
  fallbackIndex: number
): TopologyPosition {
  const source = kind === 'asset' ? editor.assets[key] : editor.infrastructure[key]
  if (!source) return fallbackPosition(fallbackIndex)
  return { x: source.x, y: source.y, width: null, height: null, collapsed: false }
}

/**
 * Origin for an implicit switch that has no infrastructure entry: the top-centre
 * of its region's header band, matching where auto layout puts it. Only the
 * region's x/y are read; its width/height stay region-only data.
 */
function switchPositionFromNetwork(
  editor: TeamLabTopologyEditor,
  networkKey: string,
  fallbackIndex: number
): TopologyPosition {
  const region = editor.networks[networkKey]
  if (!region) return fallbackPosition(fallbackIndex)
  const width = region.width ?? MIN_REGION_WIDTH
  return {
    x: region.x + Math.max(REGION_PADDING_X, (width - NODE_WIDTH) / 2),
    y: region.y + REGION_HEADER_HEIGHT - INFRA_NODE_HEIGHT / 2,
    width: null,
    height: null,
    collapsed: false,
  }
}

function fallbackPosition(fallbackIndex: number): TopologyPosition {
  return {
    x: (fallbackIndex % 4) * (NODE_WIDTH + MEMBER_GAP_X),
    y: Math.floor(fallbackIndex / 4) * (ASSET_NODE_HEIGHT + MEMBER_GAP_Y),
    width: null,
    height: null,
    collapsed: false,
  }
}

function mapAsset(
  asset: TeamLabTopologyAsset,
  editor: TeamLabTopologyEditor,
  index: number,
  options: TopologyMapperOptions
): TopologyAssetNode {
  return {
    type: asset.kind === 'docker' ? 'docker' : options.resolveVmDeviceType(asset),
    key: asset.key,
    name: asset.name,
    position: nodePosition(editor, 'asset', asset.key, index),
    imageTemplateId: asset.imageTemplateId,
    resources: { ...asset.resources },
    exposePort: asset.exposePort,
    healthCheck: asset.healthCheck ? { ...asset.healthCheck } : null,
    orderIndex: asset.orderIndex,
    endpointObservation: asset.endpointObservation,
    devicePackageId: asset.devicePackageId ?? null,
    deviceParameters: deviceParametersText(asset.deviceParameters),
    connectorId: asset.connectorId ?? null,
  }
}

/** The editor keeps parameters as editable JSON text; the contract carries a parsed object. */
function deviceParametersText(parameters: unknown): string | null {
  if (parameters === null || parameters === undefined) return null
  if (typeof parameters !== 'object' || Array.isArray(parameters)) return null
  const entries = Object.keys(parameters as Record<string, unknown>)
  return entries.length === 0 ? null : JSON.stringify(parameters, null, 2)
}

function switchForNetwork(networkKey: string, infrastructure: readonly TeamLabTopologyInfrastructure[]) {
  return sorted(infrastructure).find((item) => item.kind === 'managed-switch' && item.networkKey === networkKey)
}

function memberships(
  ownerKey: string,
  interfaces: readonly TeamLabTopologyInterface[],
  switchKeysByNetwork: ReadonlyMap<string, string>,
  occupied: Set<string>
): TopologyConnection[] {
  return sorted(interfaces).map((item) => {
    const switchKey = switchKeysByNetwork.get(item.networkKey)
    if (!switchKey) throw new Error(`Interface '${item.key}' references unknown network '${item.networkKey}'.`)
    const key = nextTopologyKey(`membership-${ownerKey}-${item.key}`, occupied)
    occupied.add(key)
    return {
      type: 'membership',
      key,
      interfaceKey: item.key,
      nodeKey: ownerKey,
      switchKey,
      hostOffset: item.hostOffset,
      primary: item.primary,
      orderIndex: item.orderIndex,
    }
  })
}

export function mapTopologyDetailToDocument(
  detail: TeamLabTopologyDetail,
  options: TopologyMapperOptions
): TopologyDocument {
  if (detail.schemaVersion !== 1 && detail.schemaVersion !== 2)
    throw new Error(`Topology schema version ${detail.schemaVersion} is not supported by this editor.`)

  const nodes: Record<string, TopologyNode> = {}
  const connections: Record<string, TopologyConnection> = {}
  const explicitKeys = [
    ...detail.definition.infrastructure.map((item) => item.key),
    ...detail.definition.assets.map((item) => item.key),
  ]
  if (new Set(explicitKeys).size !== explicitKeys.length)
    throw new Error('Topology infrastructure and asset keys must be globally unique.')
  const occupied = new Set(explicitKeys)
  const switchKeysByNetwork = new Map<string, string>()

  sorted(detail.definition.networks).forEach((network, index) => {
    const infrastructure = switchForNetwork(network.key, detail.definition.infrastructure)
    const switchKey = infrastructure?.key ?? nextTopologyKey(`switch-${network.key}`, occupied)
    occupied.add(switchKey)
    switchKeysByNetwork.set(network.key, switchKey)
    nodes[switchKey] = {
      type: 'switch',
      implicit: infrastructure === undefined,
      key: switchKey,
      name: infrastructure?.name ?? network.name,
      networkName: network.name,
      // An implicit switch has no infrastructure entry of its own, so it starts
      // at its network's origin — but never inherits the network's *size*.
      position: detail.editor.infrastructure[switchKey]
        ? nodePosition(detail.editor, 'infrastructure', switchKey, index)
        : switchPositionFromNetwork(detail.editor, network.key, index),
      networkKey: network.key,
      poolCidr: network.addressPool.poolCidr,
      runtimePrefixLength: network.addressPool.runtimePrefixLength,
      isEntry: network.isEntry,
      orderIndex: network.orderIndex,
    }
  })

  sorted(detail.definition.infrastructure)
    .filter((item) => item.kind === 'managed-router')
    .forEach((router, index) => {
      occupied.add(router.key)
      nodes[router.key] = {
        type: 'router',
        key: router.key,
        name: router.name,
        position: nodePosition(detail.editor, 'infrastructure', router.key, detail.definition.networks.length + index),
      }
      for (const connection of memberships(router.key, router.interfaces, switchKeysByNetwork, occupied)) {
        connections[connection.key] = connection
      }
    })

  sorted(detail.definition.assets).forEach((asset, index) => {
    occupied.add(asset.key)
    nodes[asset.key] = mapAsset(asset, detail.editor, detail.definition.networks.length + index, options)
    for (const connection of memberships(asset.key, asset.interfaces, switchKeysByNetwork, occupied)) {
      connections[connection.key] = connection
    }
  })

  for (const route of sorted(detail.definition.connections)) {
    const fromSwitchKey = switchKeysByNetwork.get(route.fromNetworkKey)
    const toSwitchKey = switchKeysByNetwork.get(route.toNetworkKey)
    const viaNodeKey = route.viaNodeKey ?? route.viaAssetKey
    if (!fromSwitchKey || !toSwitchKey || !viaNodeKey)
      throw new Error(`Route '${route.key}' has unresolved topology references.`)
    connections[route.key] = {
      type: 'route',
      key: route.key,
      fromSwitchKey,
      toSwitchKey,
      viaNodeKey,
      direction: route.direction,
    }
    occupied.add(route.key)
  }

  for (const dependency of detail.definition.dependencies) {
    const preferred = dependencyConnectionKey(dependency.assetKey, dependency.dependsOnKey, dependency.condition)
    const key = nextTopologyKey(preferred, occupied)
    occupied.add(key)
    connections[key] = { type: 'dependency', key, ...dependency }
  }

  return {
    schemaVersion: 2,
    name: detail.definition.name,
    nodes,
    connections,
    observation: { ...detail.definition.observation },
    networkLayouts: Object.fromEntries(
      sorted(detail.definition.networks).map((network, index) => [
        network.key,
        regionPosition(detail.editor, network.key, index),
      ])
    ),
  }
}

export function mapDocumentToUpdateRequest(document: TopologyDocument, revision: number): UpdateTeamLabTopologyRequest {
  return { ...compileTopologyDocument(document), revision }
}
