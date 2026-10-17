import { MarkerType } from '@xyflow/react'
import type { TopologyDocument, TopologySwitchNode } from '../../model/topologyDocument'
import { connectionHandles } from '../edges/connectionHandles'
import type { TeamLabFlowEdge } from '../edges/edgeTypes'
import { edgeLayer } from './topologyLayers'

function switchOf(document: TopologyDocument, key: string): TopologySwitchNode | undefined {
  const node = document.nodes[key]
  return node?.type === 'switch' ? node : undefined
}

/** Pure projection: persisted connections become deterministic, independently routed flow edges. */
export function flowEdges(document: TopologyDocument): TeamLabFlowEdge[] {
  const edges: TeamLabFlowEdge[] = []
  const obstacles = Object.values(document.nodes)
  for (const connection of Object.values(document.connections).toSorted((a, b) => a.key.localeCompare(b.key))) {
    if (connection.type === 'membership') {
      edges.push({
        id: connection.key,
        source: connection.nodeKey,
        target: connection.switchKey,
        ...connectionHandles(document.nodes[connection.nodeKey], document.nodes[connection.switchKey], obstacles,
          edges.filter(edge => edge.source === connection.nodeKey).map(edge => ({
            sourceHandle: edge.sourceHandle ?? 'source', targetHandle: edge.targetHandle ?? 'target',
            target: document.nodes[edge.target],
          }))),
        type: 'network',
        selected: false,
        zIndex: edgeLayer(false),
        // A membership link inside one region needs no label: it would repeat the
        // region title on every device. Only the primary NIC is worth marking.
        data: {
          connection,
          label: connection.primary ? '主网卡' : '',
          tone: 'membership',
          networkKey: switchOf(document, connection.switchKey)?.networkKey ?? connection.switchKey,
        },
      })
      continue
    }
    if (connection.type === 'route') {
      edges.push({
        id: connection.key,
        source: connection.fromSwitchKey,
        target: connection.toSwitchKey,
        ...connectionHandles(document.nodes[connection.fromSwitchKey], document.nodes[connection.toSwitchKey], obstacles),
        type: 'network',
        selected: false,
        zIndex: edgeLayer(false),
        markerStart: connection.direction === 'bidirectional' ? { type: MarkerType.ArrowClosed } : undefined,
        markerEnd: { type: MarkerType.ArrowClosed },
        data: {
          connection,
          label: connection.direction === 'bidirectional' ? '双向路由' : '单向路由',
          tone: 'route',
        },
      })
    }
  }
  return edges
}
