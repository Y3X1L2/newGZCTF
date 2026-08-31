import type { TeamLabConnectionDirection, TeamLabDependencyCondition } from '../api/teamlabContracts'
import { isTopologyAsset, type TopologyConnection, type TopologyDocument, type TopologyNode, type TopologyPosition } from './topologyDocument'
import {
  REGION_HEADER_HEIGHT,
  REGION_PADDING_BOTTOM,
  REGION_PADDING_X,
  clampRegionSize,
  nodeSize,
  regionSizeForMembers,
} from './topologyGeometry'
import { buildKeyRemap, dependencyConnectionKey, nextTopologyKey, topologyKeys } from './topologyKeys'
import type { TopologySelection } from './topologySelection'

export interface TopologyCommandResult<T = void> {
  document: TopologyDocument
  before: TopologyDocument
  value: T
}

export interface TopologyFragment {
  nodes: readonly TopologyNode[]
  connections: readonly TopologyConnection[]
}

export type TopologyConnectRequest =
  | {
      type: 'membership'
      nodeKey: string
      switchKey: string
      key?: string
      hostOffset?: number
      primary?: boolean
      orderIndex?: number
    }
  | {
      type: 'route'
      fromSwitchKey: string
      toSwitchKey: string
      viaNodeKey: string
      direction?: TeamLabConnectionDirection
      key?: string
    }
  | {
      type: 'dependency'
      assetKey: string
      dependsOnKey: string
      condition?: TeamLabDependencyCondition
      key?: string
    }

const result = <T>(before: TopologyDocument, document: TopologyDocument, value: T): TopologyCommandResult<T> => ({
  before,
  document,
  value,
})

function node(document: TopologyDocument, key: string) {
  const value = document.nodes[key]
  if (!value) throw new Error(`Topology node '${key}' does not exist.`)
  return value
}

function withConnections(document: TopologyDocument, connections: Record<string, TopologyConnection>) {
  return { ...document, connections }
}

function isMembershipConnection(
  connection: TopologyConnection
): connection is Extract<TopologyConnection, { type: 'membership' }> {
  return connection.type === 'membership'
}

export function addTopologyNode(document: TopologyDocument, value: TopologyNode) {
  if (topologyKeys(document).has(value.key)) throw new Error(`Topology key '${value.key}' already exists.`)
  if (value.type === 'switch' && topologyKeys(document).has(value.networkKey)) {
    throw new Error(`Topology key '${value.networkKey}' already exists.`)
  }
  return result(document, { ...document, nodes: { ...document.nodes, [value.key]: value } }, value.key)
}

export function updateTopologyNode(document: TopologyDocument, value: TopologyNode) {
  if (!document.nodes[value.key]) throw new Error(`Topology node '${value.key}' does not exist.`)
  return result(document, { ...document, nodes: { ...document.nodes, [value.key]: value } }, value.key)
}

export function updateTopologyConnection(document: TopologyDocument, value: TopologyConnection) {
  if (!document.connections[value.key]) {
    throw new Error(`Topology connection '${value.key}' does not exist.`)
  }
  return result(document, { ...document, connections: { ...document.connections, [value.key]: value } }, value.key)
}

export function moveTopologyNode(document: TopologyDocument, key: string, x: number, y: number) {
  const current = node(document, key)
  const updated = { ...current, position: { ...current.position, x, y } } as TopologyNode
  return updateTopologyNode(document, updated)
}

export function bulkMoveTopologyNodes(
  document: TopologyDocument,
  keys: Iterable<string>,
  delta: { x: number; y: number }
) {
  const nodes = { ...document.nodes }
  for (const key of [...keys].sort()) {
    const current = node(document, key)
    nodes[key] = {
      ...current,
      position: { ...current.position, x: current.position.x + delta.x, y: current.position.y + delta.y },
    } as TopologyNode
  }
  return result(document, { ...document, nodes }, undefined)
}

/** All nodes visually owned by a network region: its switch plus every asset member. */
export function networkMembersOf(document: TopologyDocument, networkKey: string): string[] {
  const switches = Object.values(document.nodes).filter(
    (item): item is Extract<TopologyNode, { type: 'switch' }> => item.type === 'switch' && item.networkKey === networkKey
  )
  const switchKeys = new Set(switches.map((item) => item.key))
  const members = new Set<string>(switchKeys)
  // An asset belongs to a region only when ALL of its memberships point into that
  // network. Cross-network devices (routers, dual-homed assets) are border nodes
  // and never move with a single region.
  const assetNetworks = new Map<string, Set<string>>()
  for (const connection of Object.values(document.connections)) {
    if (connection.type !== 'membership') continue
    const owner = document.nodes[connection.switchKey]
    if (owner?.type !== 'switch') continue
    const networks = assetNetworks.get(connection.nodeKey) ?? new Set<string>()
    networks.add(owner.networkKey)
    assetNetworks.set(connection.nodeKey, networks)
  }
  for (const [nodeKey, networks] of assetNetworks) {
    if (networks.size === 1 && networks.has(networkKey)) members.add(nodeKey)
  }
  return [...members].sort()
}

/**
 * Derives a region box from its member bounding box. Used as a fallback when no
 * layout has been recorded yet so collapse/resize never teleports a fresh region
 * (created this session, pasted, or legacy data) to the canvas origin.
 *
 * Member extents come from {@link nodeSize} — the node's intrinsic type size —
 * never from `position.width`, which only regions may carry.
 */
function derivedRegionLayout(document: TopologyDocument, networkKey: string): TopologyPosition {
  const members = networkMembersOf(document, networkKey)
  let minX = Number.POSITIVE_INFINITY
  let minY = Number.POSITIVE_INFINITY
  let maxX = Number.NEGATIVE_INFINITY
  let maxY = Number.NEGATIVE_INFINITY
  for (const key of members) {
    const current = document.nodes[key]
    if (!current) continue
    const size = nodeSize(current)
    minX = Math.min(minX, current.position.x)
    minY = Math.min(minY, current.position.y)
    maxX = Math.max(maxX, current.position.x + size.width)
    maxY = Math.max(maxY, current.position.y + size.height)
  }
  if (members.length === 0) {
    const empty = regionSizeForMembers([])
    return { x: 0, y: 0, width: empty.width, height: empty.height, collapsed: false }
  }
  const size = clampRegionSize({
    width: maxX - minX + REGION_PADDING_X * 2,
    height: maxY - minY + REGION_HEADER_HEIGHT + REGION_PADDING_BOTTOM,
  })
  return {
    x: minX - REGION_PADDING_X,
    y: minY - REGION_HEADER_HEIGHT,
    width: size.width,
    height: size.height,
    collapsed: false,
  }
}

export function updateNetworkLayout(
  document: TopologyDocument,
  networkKey: string,
  layout: TopologyPosition
): TopologyCommandResult<string> {
  return result(
    document,
    { ...document, networkLayouts: { ...document.networkLayouts, [networkKey]: layout } },
    networkKey
  )
}

export function setNetworkCollapsed(
  document: TopologyDocument,
  networkKey: string,
  collapsed: boolean
): TopologyCommandResult<string> {
  const current = document.networkLayouts[networkKey]
  return updateNetworkLayout(document, networkKey, {
    ...(current ?? derivedRegionLayout(document, networkKey)),
    collapsed,
  })
}

export function resizeNetworkRegion(
  document: TopologyDocument,
  networkKey: string,
  width: number,
  height: number
): TopologyCommandResult<string> {
  const current = document.networkLayouts[networkKey] ?? derivedRegionLayout(document, networkKey)
  // Clamping here (not only in the renderer) keeps an out-of-range drag from
  // ever reaching persistence, where it would later be read back as a size.
  return updateNetworkLayout(document, networkKey, { ...current, ...clampRegionSize({ width, height }) })
}

/**
 * Returns a region to the size its members imply, without moving the members.
 * A concrete size is written rather than `null` so the canvas, the persisted
 * record and the auto-layout output all describe the same box.
 */
export function fitNetworkRegionToMembers(document: TopologyDocument, networkKey: string): TopologyCommandResult<string> {
  const current = document.networkLayouts[networkKey] ?? derivedRegionLayout(document, networkKey)
  const memberHeights = networkMembersOf(document, networkKey)
    .filter((key) => document.nodes[key]?.type !== 'switch')
    .map((key) => nodeSize(document.nodes[key]).height)
  return updateNetworkLayout(document, networkKey, { ...current, ...regionSizeForMembers(memberHeights) })
}

/** Moves the region origin and every member by the same delta, keeping the region visual container consistent. */
export function moveNetworkRegion(
  document: TopologyDocument,
  networkKey: string,
  delta: { x: number; y: number }
): TopologyCommandResult<string> {
  const current = document.networkLayouts[networkKey] ?? derivedRegionLayout(document, networkKey)
  let next = updateNetworkLayout(document, networkKey, {
    ...current,
    x: current.x + delta.x,
    y: current.y + delta.y,
  }).document
  next = bulkMoveTopologyNodes(next, networkMembersOf(document, networkKey), delta).document
  return result(document, next, networkKey)
}

function defaultHostOffset(document: TopologyDocument, switchKey: string) {
  const used = Object.values(document.connections)
    .filter(isMembershipConnection)
    .filter((item) => item.switchKey === switchKey)
    .map((item) => item.hostOffset)
  let candidate = 1
  while (used.includes(candidate)) candidate += 1
  return candidate
}

export function connectTopology(document: TopologyDocument, request: TopologyConnectRequest) {
  const occupied = topologyKeys(document)
  let connection: TopologyConnection
  const connections = { ...document.connections }

  if (request.type === 'membership') {
    const endpoint = node(document, request.nodeKey)
    const targetSwitch = node(document, request.switchKey)
    if (targetSwitch.type !== 'switch' || endpoint.type === 'switch') {
      throw new Error('A membership must connect an asset or router to a switch.')
    }
    if (
      Object.values(connections).some(
        (item) => item.type === 'membership' && item.nodeKey === endpoint.key && item.switchKey === targetSwitch.key
      )
    ) {
      throw new Error(`Node '${endpoint.key}' is already connected to switch '${targetSwitch.key}'.`)
    }
    const existing = Object.values(connections)
      .filter(isMembershipConnection)
      .filter((item) => item.nodeKey === endpoint.key)
    const primary = request.primary ?? existing.length === 0
    if (primary) {
      for (const item of existing) connections[item.key] = { ...item, primary: false }
    }
    const key = request.key ?? nextTopologyKey(`${endpoint.key}-${targetSwitch.key}-nic`, occupied)
    connection = {
      type: 'membership',
      key,
      nodeKey: endpoint.key,
      switchKey: targetSwitch.key,
      hostOffset: request.hostOffset ?? defaultHostOffset(document, targetSwitch.key),
      primary,
      orderIndex: request.orderIndex ?? existing.length,
    }
  } else if (request.type === 'route') {
    const from = node(document, request.fromSwitchKey)
    const to = node(document, request.toSwitchKey)
    const via = node(document, request.viaNodeKey)
    if (from.type !== 'switch' || to.type !== 'switch' || from.key === to.key) {
      throw new Error('A route must connect two different switches.')
    }
    if (via.type !== 'router') throw new Error('A route must use a managed router.')
    const attached = new Set(
      Object.values(connections)
        .filter(isMembershipConnection)
        .filter((item) => item.nodeKey === via.key)
        .map((item) => item.switchKey)
    )
    if (!attached.has(from.key) || !attached.has(to.key)) {
      throw new Error(`Routing node '${via.key}' must be attached to both switches.`)
    }
    const key = request.key ?? nextTopologyKey(`route-${from.key}-${to.key}-via-${via.key}`, occupied)
    connection = {
      type: 'route',
      key,
      fromSwitchKey: from.key,
      toSwitchKey: to.key,
      viaNodeKey: via.key,
      direction: request.direction ?? 'bidirectional',
    }
  } else {
    const asset = node(document, request.assetKey)
    const dependency = node(document, request.dependsOnKey)
    if (!isTopologyAsset(asset) || !isTopologyAsset(dependency) || asset.key === dependency.key) {
      throw new Error('A dependency must connect two different assets.')
    }
    const condition = request.condition ?? 'network-ready'
    const key = request.key ?? nextTopologyKey(dependencyConnectionKey(asset.key, dependency.key, condition), occupied)
    connection = {
      type: 'dependency',
      key,
      assetKey: asset.key,
      dependsOnKey: dependency.key,
      condition,
    }
  }

  if (connections[connection.key]) throw new Error(`Topology key '${connection.key}' already exists.`)
  connections[connection.key] = connection
  return result(document, withConnections(document, connections), connection.key)
}

export function disconnectTopology(document: TopologyDocument, connectionKey: string) {
  if (!document.connections[connectionKey]) throw new Error(`Topology connection '${connectionKey}' does not exist.`)
  const connections = { ...document.connections }
  delete connections[connectionKey]
  return result(document, withConnections(document, connections), connectionKey)
}

function referencesNode(connection: TopologyConnection, deleted: ReadonlySet<string>) {
  if (connection.type === 'membership') return deleted.has(connection.nodeKey) || deleted.has(connection.switchKey)
  if (connection.type === 'route') {
    return (
      deleted.has(connection.fromSwitchKey) || deleted.has(connection.toSwitchKey) || deleted.has(connection.viaNodeKey)
    )
  }
  return deleted.has(connection.assetKey) || deleted.has(connection.dependsOnKey)
}

export function deleteTopologyItems(document: TopologyDocument, selection: TopologySelection) {
  const nodes = { ...document.nodes }
  for (const key of selection.nodeKeys) delete nodes[key]

  const connections = Object.fromEntries(
    Object.entries(document.connections).filter(
      ([key, connection]) => !selection.connectionKeys.has(key) && !referencesNode(connection, selection.nodeKeys)
    )
  )
  return result(document, { ...document, nodes, connections }, undefined)
}

function connectionIsInternal(connection: TopologyConnection, selected: ReadonlySet<string>) {
  if (connection.type === 'membership') return selected.has(connection.nodeKey) && selected.has(connection.switchKey)
  if (connection.type === 'route') {
    return (
      selected.has(connection.fromSwitchKey) &&
      selected.has(connection.toSwitchKey) &&
      selected.has(connection.viaNodeKey)
    )
  }
  return selected.has(connection.assetKey) && selected.has(connection.dependsOnKey)
}

export function copyTopologyFragment(document: TopologyDocument, nodeKeys: ReadonlySet<string>): TopologyFragment {
  return {
    nodes: [...nodeKeys].sort().map((key) => node(document, key)),
    connections: Object.values(document.connections)
      .filter((connection) => connectionIsInternal(connection, nodeKeys))
      .sort((left, right) => left.key.localeCompare(right.key)),
  }
}

export function pasteTopologyFragment(
  document: TopologyDocument,
  fragment: TopologyFragment,
  offset = { x: 32, y: 32 }
) {
  const occupied = topologyKeys(document)
  const nodeRemap = buildKeyRemap(
    fragment.nodes.map((item) => item.key),
    '-copy',
    occupied
  )
  for (const value of nodeRemap.values()) occupied.add(value)
  const networkRemap = buildKeyRemap(
    fragment.nodes
      .filter((item) => item.type === 'switch')
      .map((item) => (item.type === 'switch' ? item.networkKey : '')),
    '-copy',
    occupied
  )
  for (const value of networkRemap.values()) occupied.add(value)
  const connectionRemap = buildKeyRemap(
    fragment.connections.map((item) => item.key),
    '-copy',
    occupied
  )

  const nodes = { ...document.nodes }
  for (const source of fragment.nodes) {
    const key = nodeRemap.get(source.key)!
    nodes[key] = {
      ...source,
      key,
      ...(source.type === 'switch' ? { networkKey: networkRemap.get(source.networkKey)! } : {}),
      position: { ...source.position, x: source.position.x + offset.x, y: source.position.y + offset.y },
    } as TopologyNode
  }

  const connections = { ...document.connections }
  for (const source of fragment.connections) {
    const key = connectionRemap.get(source.key)!
    if (source.type === 'membership') {
      connections[key] = {
        ...source,
        key,
        nodeKey: nodeRemap.get(source.nodeKey)!,
        switchKey: nodeRemap.get(source.switchKey)!,
      }
    } else if (source.type === 'route') {
      connections[key] = {
        ...source,
        key,
        fromSwitchKey: nodeRemap.get(source.fromSwitchKey)!,
        toSwitchKey: nodeRemap.get(source.toSwitchKey)!,
        viaNodeKey: nodeRemap.get(source.viaNodeKey)!,
      }
    } else {
      connections[key] = {
        ...source,
        key,
        assetKey: nodeRemap.get(source.assetKey)!,
        dependsOnKey: nodeRemap.get(source.dependsOnKey)!,
      }
    }
  }

  return result(document, { ...document, nodes, connections }, new Set(nodeRemap.values()))
}

export function duplicateTopologyNodes(document: TopologyDocument, nodeKeys: ReadonlySet<string>) {
  return pasteTopologyFragment(document, copyTopologyFragment(document, nodeKeys))
}
