import type { TopologyConnection, TopologyDocument, TopologySwitchNode } from '../../model/topologyDocument'

/**
 * Pre-computed adjacency for one topology document.
 *
 * Layout and canvas projection both used to answer "which network owns this
 * node?" and "which switches does this node touch?" by scanning every
 * connection (or every network's member list) per node. That is quadratic and
 * measurably froze the main thread on large scenes. Building these indexes once
 * makes every later lookup O(1).
 */
export interface TopologyGraph {
  /** Switches sorted by network key, then key, for deterministic output. */
  readonly switches: readonly TopologySwitchNode[]
  /** Network key -> its switch key. */
  readonly switchByNetwork: ReadonlyMap<string, string>
  /** Switch key -> its network key. */
  readonly networkBySwitch: ReadonlyMap<string, string>
  /** Node key -> every network it has a membership in. */
  readonly networksOfNode: ReadonlyMap<string, ReadonlySet<string>>
  /** Network key -> the nodes it visually owns (switch first, then sorted assets). */
  readonly membersByNetwork: ReadonlyMap<string, readonly string[]>
  /** Node key -> the network whose region owns it, when exactly one does. */
  readonly ownerNetworkOfNode: ReadonlyMap<string, string>
  /** Node key -> number of membership endpoints, for node badges. */
  readonly membershipCounts: ReadonlyMap<string, number>
  /** Switch key -> switch keys reachable through one route hop. */
  readonly routeAdjacency: ReadonlyMap<string, ReadonlySet<string>>
  /** Router/border node keys that connect more than one network. */
  readonly borderNodeKeys: ReadonlySet<string>
  /** Route connections keyed by their via node, for border-node placement. */
  readonly routesByViaNode: ReadonlyMap<string, readonly Extract<TopologyConnection, { type: 'route' }>[]>
}

export function buildTopologyGraph(document: TopologyDocument): TopologyGraph {
  const switches = Object.values(document.nodes)
    .filter((node): node is TopologySwitchNode => node.type === 'switch')
    .toSorted((left, right) => left.networkKey.localeCompare(right.networkKey) || left.key.localeCompare(right.key))

  const switchByNetwork = new Map<string, string>()
  const networkBySwitch = new Map<string, string>()
  for (const node of switches) {
    if (!switchByNetwork.has(node.networkKey)) switchByNetwork.set(node.networkKey, node.key)
    networkBySwitch.set(node.key, node.networkKey)
  }

  const networksOfNode = new Map<string, Set<string>>()
  const membershipCounts = new Map<string, number>()
  const routeAdjacency = new Map<string, Set<string>>(switches.map((node) => [node.key, new Set<string>()]))
  const routesByViaNode = new Map<string, Extract<TopologyConnection, { type: 'route' }>[]>()

  for (const connection of Object.values(document.connections)) {
    if (connection.type === 'membership') {
      const networkKey = networkBySwitch.get(connection.switchKey)
      membershipCounts.set(connection.nodeKey, (membershipCounts.get(connection.nodeKey) ?? 0) + 1)
      membershipCounts.set(connection.switchKey, (membershipCounts.get(connection.switchKey) ?? 0) + 1)
      if (networkKey === undefined) continue
      const networks = networksOfNode.get(connection.nodeKey) ?? new Set<string>()
      networks.add(networkKey)
      networksOfNode.set(connection.nodeKey, networks)
      continue
    }
    if (connection.type !== 'route') continue
    routeAdjacency.get(connection.fromSwitchKey)?.add(connection.toSwitchKey)
    routeAdjacency.get(connection.toSwitchKey)?.add(connection.fromSwitchKey)
    const routes = routesByViaNode.get(connection.viaNodeKey) ?? []
    routes.push(connection)
    routesByViaNode.set(connection.viaNodeKey, routes)
  }

  // A node belongs to a region only when *all* of its memberships point into
  // that one network. Routers and dual-homed assets are border nodes: they are
  // placed between regions and never move with a single region.
  const ownerNetworkOfNode = new Map<string, string>()
  const borderNodeKeys = new Set<string>()
  for (const [nodeKey, networks] of networksOfNode) {
    if (networks.size === 1 && !routesByViaNode.has(nodeKey)) {
      ownerNetworkOfNode.set(nodeKey, [...networks][0])
    } else {
      borderNodeKeys.add(nodeKey)
    }
  }
  for (const nodeKey of routesByViaNode.keys()) borderNodeKeys.add(nodeKey)

  const membersByNetwork = new Map<string, string[]>()
  for (const node of switches) {
    const current = membersByNetwork.get(node.networkKey) ?? []
    current.push(node.key)
    membersByNetwork.set(node.networkKey, current)
  }
  const ownedAssets = new Map<string, string[]>()
  for (const [nodeKey, networkKey] of ownerNetworkOfNode) {
    const current = ownedAssets.get(networkKey) ?? []
    current.push(nodeKey)
    ownedAssets.set(networkKey, current)
  }
  for (const [networkKey, assetKeys] of ownedAssets) {
    const current = membersByNetwork.get(networkKey) ?? []
    membersByNetwork.set(networkKey, [...current, ...assetKeys.toSorted((a, b) => a.localeCompare(b))])
  }

  return {
    switches,
    switchByNetwork,
    networkBySwitch,
    networksOfNode,
    membersByNetwork,
    ownerNetworkOfNode,
    membershipCounts,
    routeAdjacency,
    borderNodeKeys,
    routesByViaNode,
  }
}

/**
 * Breadth-first routing depth from the entry network. Depth drives the vertical
 * tier a region is placed on, so the reader sees traffic flow top-to-bottom:
 * entry network first, then each routing hop below it.
 */
export interface RoutingDepth {
  /** Switch key -> hop distance from the entry switch. */
  readonly depths: ReadonlyMap<string, number>
  /** Switch key -> the depth-1 switch its branch descends from. */
  readonly branchRoots: ReadonlyMap<string, string>
  readonly entrySwitchKey: string | null
}

export function computeRoutingDepth(graph: TopologyGraph): RoutingDepth {
  const entry = graph.switches.find((node) => node.isEntry) ?? graph.switches[0]
  const depths = new Map<string, number>()
  const branchRoots = new Map<string, string>()
  if (!entry) return { depths, branchRoots, entrySwitchKey: null }

  depths.set(entry.key, 0)
  const queue = [entry.key]
  for (let index = 0; index < queue.length; index += 1) {
    const current = queue[index]
    const depth = depths.get(current) ?? 0
    const neighbours = [...(graph.routeAdjacency.get(current) ?? [])].sort((a, b) => a.localeCompare(b))
    for (const next of neighbours) {
      if (depths.has(next)) continue
      depths.set(next, depth + 1)
      branchRoots.set(next, current === entry.key ? next : (branchRoots.get(current) ?? next))
      queue.push(next)
    }
  }
  return { depths, branchRoots, entrySwitchKey: entry.key }
}
