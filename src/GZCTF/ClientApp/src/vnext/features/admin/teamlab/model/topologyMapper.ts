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

function position(
  editor: TeamLabTopologyEditor,
  kind: 'network' | 'asset' | 'infrastructure',
  key: string,
  fallbackIndex: number
): TopologyPosition {
  const source =
    kind === 'network' ? editor.networks[key] : kind === 'asset' ? editor.assets[key] : editor.infrastructure[key]
  return source
    ? { ...source }
    : {
        x: (fallbackIndex % 4) * 260,
        y: Math.floor(fallbackIndex / 4) * 180,
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
    position: position(editor, 'asset', asset.key, index),
    imageTemplateId: asset.imageTemplateId,
    resources: { ...asset.resources },
    routingEnabled: asset.routingEnabled,
    exposePort: asset.exposePort,
    environment: asset.environment ? { ...asset.environment } : null,
    startCommand: asset.startCommand,
    healthCheck: asset.healthCheck ? { ...asset.healthCheck } : null,
    orderIndex: asset.orderIndex,
    stateless: asset.stateless,
    bootstrap: asset.bootstrap ? { ...asset.bootstrap, parameters: { ...asset.bootstrap.parameters } } : null,
    endpointObservation: asset.endpointObservation,
    bakeAtPublish: asset.bakeAtPublish,
    imageDigest: asset.imageDigest,
  }
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
      position: detail.editor.infrastructure[switchKey]
        ? position(detail.editor, 'infrastructure', switchKey, index)
        : position(detail.editor, 'network', network.key, index),
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
        position: position(detail.editor, 'infrastructure', router.key, detail.definition.networks.length + index),
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
      sorted(detail.definition.networks).map((network) => [
        network.key,
        position(detail.editor, 'network', network.key, 0),
      ])
    ),
  }
}

export function mapDocumentToUpdateRequest(document: TopologyDocument, revision: number): UpdateTeamLabTopologyRequest {
  return { ...compileTopologyDocument(document), revision }
}
