import type { TopologyDocument } from './topologyDocument'

export interface TopologySelection {
  nodeKeys: ReadonlySet<string>
  connectionKeys: ReadonlySet<string>
}

export const emptyTopologySelection = (): TopologySelection => ({
  nodeKeys: new Set(),
  connectionKeys: new Set(),
})

export function selectAllTopology(document: TopologyDocument): TopologySelection {
  return {
    nodeKeys: new Set(Object.keys(document.nodes)),
    connectionKeys: new Set(Object.keys(document.connections)),
  }
}

export function normalizeTopologySelection(
  document: TopologyDocument,
  selection: TopologySelection
): TopologySelection {
  return {
    nodeKeys: new Set([...selection.nodeKeys].filter((key) => key in document.nodes)),
    connectionKeys: new Set([...selection.connectionKeys].filter((key) => key in document.connections)),
  }
}
