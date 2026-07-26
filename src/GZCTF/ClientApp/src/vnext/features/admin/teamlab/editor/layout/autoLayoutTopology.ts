import { graphlib, layout } from '@dagrejs/dagre'
import type { TopologyConnection, TopologyDocument, TopologyNode } from '../../model/topologyDocument'

const NODE_WIDTH = 208
const NODE_HEIGHT = 102
const GRID_SIZE = 8

function routeDistances(document: TopologyDocument) {
  const switches = Object.values(document.nodes)
    .filter((node) => node.type === 'switch')
    .toSorted((left, right) => left.key.localeCompare(right.key))
  const adjacency = new Map(switches.map((node) => [node.key, new Set<string>()]))
  for (const connection of Object.values(document.connections)) {
    if (connection.type !== 'route') continue
    adjacency.get(connection.fromSwitchKey)?.add(connection.toSwitchKey)
    adjacency.get(connection.toSwitchKey)?.add(connection.fromSwitchKey)
  }

  const entry = switches.find((node) => node.isEntry) ?? switches[0]
  const distances = new Map<string, number>()
  if (!entry) return distances
  distances.set(entry.key, 0)
  const queue = [entry.key]
  for (let index = 0; index < queue.length; index += 1) {
    const current = queue[index]
    const distance = distances.get(current) ?? 0
    for (const next of [...(adjacency.get(current) ?? [])].sort()) {
      if (distances.has(next)) continue
      distances.set(next, distance + 1)
      queue.push(next)
    }
  }
  return distances
}

function orientedRoute(
  connection: Extract<TopologyConnection, { type: 'route' }>,
  distances: ReadonlyMap<string, number>
) {
  const fromRank = distances.get(connection.fromSwitchKey) ?? Number.MAX_SAFE_INTEGER
  const toRank = distances.get(connection.toSwitchKey) ?? Number.MAX_SAFE_INTEGER
  if (fromRank < toRank) return [connection.fromSwitchKey, connection.toSwitchKey] as const
  if (toRank < fromRank) return [connection.toSwitchKey, connection.fromSwitchKey] as const
  return connection.fromSwitchKey.localeCompare(connection.toSwitchKey) <= 0
    ? ([connection.fromSwitchKey, connection.toSwitchKey] as const)
    : ([connection.toSwitchKey, connection.fromSwitchKey] as const)
}

function addLayoutEdges(graph: graphlib.Graph, document: TopologyDocument) {
  const distances = routeDistances(document)
  const nodeKeys = new Set(Object.keys(document.nodes))
  const routedNodes = new Set(
    Object.values(document.connections)
      .filter((connection) => connection.type === 'route')
      .map((connection) => (connection.type === 'route' ? connection.viaNodeKey : ''))
  )

  for (const connection of Object.values(document.connections).toSorted((left, right) =>
    left.key.localeCompare(right.key)
  )) {
    if (connection.type === 'route') {
      if (![connection.fromSwitchKey, connection.toSwitchKey, connection.viaNodeKey].every((key) => nodeKeys.has(key)))
        continue
      const [source, target] = orientedRoute(connection, distances)
      graph.setEdge(source, connection.viaNodeKey, { minlen: 1, weight: 8 }, `${connection.key}:in`)
      graph.setEdge(connection.viaNodeKey, target, { minlen: 1, weight: 8 }, `${connection.key}:out`)
      continue
    }
    if (connection.type === 'membership') {
      if (!nodeKeys.has(connection.switchKey) || !nodeKeys.has(connection.nodeKey)) continue
      if (routedNodes.has(connection.nodeKey)) continue
      graph.setEdge(
        connection.switchKey,
        connection.nodeKey,
        { minlen: 1, weight: connection.primary ? 6 : 2 },
        connection.key
      )
      continue
    }
    if (!nodeKeys.has(connection.dependsOnKey) || !nodeKeys.has(connection.assetKey)) continue
    graph.setEdge(connection.dependsOnKey, connection.assetKey, { minlen: 1, weight: 3 }, connection.key)
  }
}

function nodeSize(node: TopologyNode) {
  return {
    width: node.position.width ?? NODE_WIDTH,
    height: node.position.height ?? NODE_HEIGHT,
  }
}

function snap(value: number) {
  return Math.round(value / GRID_SIZE) * GRID_SIZE
}

export function autoLayoutTopology(document: TopologyDocument): TopologyDocument {
  const nodes = Object.values(document.nodes).toSorted((left, right) => left.key.localeCompare(right.key))
  if (nodes.length < 2) return document

  const graph = new graphlib.Graph({ directed: true, multigraph: true })
  graph.setGraph({
    rankdir: 'LR',
    ranker: 'network-simplex',
    acyclicer: 'greedy',
    align: 'UL',
    ranksep: 150,
    nodesep: 68,
    edgesep: 28,
    marginx: 40,
    marginy: 40,
  })
  graph.setDefaultEdgeLabel(() => ({}))
  for (const node of nodes) graph.setNode(node.key, nodeSize(node))
  addLayoutEdges(graph, document)
  layout(graph)

  const nextNodes = Object.fromEntries(
    nodes.map((node) => {
      const result = graph.node(node.key) as { x: number; y: number; width: number; height: number }
      return [
        node.key,
        {
          ...node,
          position: {
            ...node.position,
            x: snap(result.x - result.width / 2),
            y: snap(result.y - result.height / 2),
          },
        },
      ]
    })
  )
  return { ...document, nodes: nextNodes }
}
