import type { Edge } from '@xyflow/react'
import type { TopologyConnection } from '../../model/topologyDocument'

export interface TeamLabEdgeData extends Record<string, unknown> {
  connection: TopologyConnection | null
  label: string
}

export type TeamLabFlowEdge = Edge<TeamLabEdgeData, 'network' | 'dependency' | 'traffic'>
