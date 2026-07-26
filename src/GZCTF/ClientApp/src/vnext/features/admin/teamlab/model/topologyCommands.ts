import type { TeamLabConnectionDirection, TeamLabDependencyCondition } from '../api/teamlabContracts'
import { isTopologyAsset, type TopologyConnection, type TopologyDocument, type TopologyNode } from './topologyDocument'
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
    if (via.type !== 'router' && (!isTopologyAsset(via) || !via.routingEnabled)) {
      throw new Error('A route must use a managed router or routing-enabled asset.')
    }
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
